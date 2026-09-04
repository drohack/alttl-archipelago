using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The router decides what counts as a check. Getting it wrong in one
/// direction sends the server locations that do not exist; in the other, a
/// player solves something and nothing happens.
/// </summary>
public class CheckRouterTests
{
    private static SlotData Seed()
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "Books 3", LevelIndex = 13, Instance = 1 });
        data.Slots.Add(new SlotEntry { LevelId = "Books 3", LevelIndex = 13, Instance = 2 });
        data.ControllerGroups["Books 3"] = new Dictionary<string, string>
        {
            ["Design (Shuffle)"] = "Design (Shuffle)",
            ["Height (Draggables)"] = "Height",
        };
        foreach (var name in new[]
                 {
                     "Books 3 - Solution 1", "Books 3 - Solution 2",
                     "Books 3 - Design (Shuffle)", "Books 3 - Height",
                     "Books 3 - Beaten",
                     "Books 3 #2 - Solution 1", "Books 3 #2 - Design (Shuffle)",
                 })
        {
            data.Requirements[name] = new Requirement();
        }
        return data;
    }

    [Fact]
    public void ASolvedControllerBecomesItsGroupsLocation()
    {
        var router = new CheckRouter(Seed());
        Assert.Equal("Books 3 - Height", router.ForController(0, "Height (Draggables)"));
    }

    [Fact]
    public void TheSecondInstanceGetsItsOwnLocation()
    {
        // Sixteen of seventy-nine slots in a typical run are repeats of a level
        // that appears earlier. Routing both to the first instance would make
        // the repeat uncheckable.
        var router = new CheckRouter(Seed());
        Assert.Equal("Books 3 #2 - Design (Shuffle)",
                     router.ForController(1, "Design (Shuffle)"));
    }

    [Fact]
    public void AControllerWhoseLocationTheSeedDoesNotHaveIsNotACheck()
    {
        // Instance 2 has no Height location in this seed. Sending it would be
        // a location the server has never heard of.
        var router = new CheckRouter(Seed());
        Assert.Null(router.ForController(1, "Height (Draggables)"));
    }

    [Fact]
    public void AnUnknownControllerRoutesNowhereRatherThanGuessing()
    {
        var router = new CheckRouter(Seed());
        Assert.Null(router.ForController(0, "Something The Sweep Never Saw"));
        Assert.Null(router.ForController(0, ""));
        Assert.Null(router.ForController(0, null));
    }

    [Fact]
    public void ASlotIndexOutsideTheRunRoutesNowhere()
    {
        var router = new CheckRouter(Seed());
        Assert.Null(router.ForController(99, "Height (Draggables)"));
        Assert.Null(router.ForController(-1, "Height (Draggables)"));
        Assert.Null(router.ForSolution(99, 1));
        Assert.Null(router.ForBeaten(99));
        Assert.Empty(router.ForSlot(99));
    }

    [Fact]
    public void SolutionsAreCountedOrdinally()
    {
        var router = new CheckRouter(Seed());
        Assert.Equal("Books 3 - Solution 1", router.ForSolution(0, 1));
        Assert.Equal("Books 3 - Solution 2", router.ForSolution(0, 2));
        Assert.Null(router.ForSolution(0, 3));       // the seed has only two
        Assert.Null(router.ForSolution(0, 0));       // ordinals are 1-based
    }

    [Fact]
    public void ASlotListsEveryLocationItCanProduce()
    {
        var router = new CheckRouter(Seed());
        Assert.Equal(
            new[]
            {
                "Books 3 - Solution 1", "Books 3 - Solution 2",
                "Books 3 - Design (Shuffle)", "Books 3 - Height",
                "Books 3 - Beaten",
            },
            router.ForSlot(0));
    }

    [Fact]
    public void AMergedGroupIsListedOnceNotOncePerMember()
    {
        // A mutually-dependent pair is two controllers wearing one group.
        // Counting it twice would leave a card permanently short of complete.
        var data = Seed();
        data.ControllerGroups["Books 3"]["Another (Draggables)"] = "Height";

        var router = new CheckRouter(data);
        Assert.Single(router.ForSlot(0), n => n == "Books 3 - Height");
    }

    [Fact]
    public void TheRealSeedRoutesEveryControllerItShipsToARealLocation()
    {
        // The end-to-end contract, against the payload the generator actually
        // produces: anything the router does resolve must be a location the
        // seed contains, on every slot in the run.
        var data = ExampleSeed.Load();
        var router = new CheckRouter(data);

        int routed = 0;
        for (int i = 0; i < data.Slots.Count; i++)
        {
            if (!data.ControllerGroups.TryGetValue(data.Slots[i].LevelId, out var groups))
            {
                Assert.Fail($"slot {i} {data.Slots[i].LevelId} has no controller map");
            }

            foreach (var controller in groups.Keys)
            {
                var name = router.ForController(i, controller);
                if (name == null) continue;          // legitimately not a check
                Assert.True(data.Requirements.ContainsKey(name),
                    $"{name} is not a location in this seed");
                routed++;
            }
        }

        Assert.True(routed > 0, "the real seed routed no controller at all");
    }

    [Fact]
    public void EveryPartLocationInTheRealSeedIsReachableFromSomeController()
    {
        // The other direction, and the one that loses progress: a part location
        // no controller maps to can never be checked, so the seed would contain
        // an item nobody can reach.
        var data = ExampleSeed.Load();
        var router = new CheckRouter(data);

        var routable = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < data.Slots.Count; i++)
        {
            if (!data.ControllerGroups.TryGetValue(data.Slots[i].LevelId, out var groups)) continue;
            foreach (var controller in groups.Keys)
            {
                var name = router.ForController(i, controller);
                if (name != null) routable.Add(name);
            }
        }

        var parts = data.Requirements.Keys
            .Where(n => !n.EndsWith(" - Beaten", StringComparison.Ordinal)
                        && !n.Contains(" - Solution ", StringComparison.Ordinal)
                        && n != LocationNames.Credits)
            .ToList();

        var orphans = parts.Where(n => !routable.Contains(n)).ToList();
        Assert.True(orphans.Count == 0,
            $"{orphans.Count} part location(s) no controller can check, e.g. "
            + string.Join(", ", orphans.Take(5)));
    }

    [Fact]
    public void ABeatenLocationIsAnEventAndIsNeverSent()
    {
        // Beaten locations have address None on the server. Sending one is
        // rejected every time, so it must never enter the owed queue.
        var router = new CheckRouter(Seed());

        Assert.True(router.IsLocalEvent("Books 3 - Beaten"));
        Assert.False(router.IsLocalEvent("Books 3 - Solution 1"));
        Assert.False(router.IsLocalEvent("Books 3 - Height"));
        Assert.False(router.IsLocalEvent(null));
    }

    [Fact]
    public void BeatenPuzzlesAreCountedFromWhatWasCollected()
    {
        // The Level Beaten token rides on that event location, so the server
        // never delivers it. Counting received items gave zero forever and the
        // credits could never unlock.
        var router = new CheckRouter(Seed());
        var done = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal(0, router.BeatenCount(done.Contains));

        done.Add("Books 3 - Beaten");
        Assert.Equal(1, router.BeatenCount(done.Contains));

        // A solution check is not a beaten puzzle.
        done.Add("Books 3 - Solution 1");
        Assert.Equal(1, router.BeatenCount(done.Contains));
    }

    [Fact]
    public void EveryBeatenLocationInTheRealSeedIsAnEvent()
    {
        var data = ExampleSeed.Load();
        var router = new CheckRouter(data);

        var beaten = data.Requirements.Keys
            .Where(n => n.EndsWith(" - Beaten", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(beaten);
        foreach (var name in beaten)
        {
            Assert.True(router.IsLocalEvent(name),
                $"{name} would be sent to a server that has no address for it");
        }
    }
}
