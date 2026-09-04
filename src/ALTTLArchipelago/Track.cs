using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// The seed's puzzles, on the game's own level select.
///
/// This hooks the extension point the GAME already uses: ArchiveMenu derives
/// from LevelSelect and overrides exactly these two methods to show a
/// different set of levels in the same track UI. A mod patching them is doing
/// what the game does, not fighting it - which is why a 10-card mixed track
/// rendered correctly, scrollbar and star fractions and all, the first time it
/// was tried.
///
/// Two things about the level select that cost time to learn:
///
/// - The menu object is BUILT ONCE AND CACHED. An override installed after
///   first construction changes nothing until something forces a rebuild.
/// - Unlock state is not a flag. A card is unlocked exactly when the save has
///   a LevelCompletionData entry for it, so revealing a slot means creating
///   one.
/// </summary>
internal static class Track
{
    /// <summary>The run, or null when not connected - then the game is vanilla.</summary>
    private static TrackState? _state;

    /// <summary>Level index for each slot, in track order.</summary>
    private static readonly List<int> _order = new();

    /// <summary>
    /// What the card at this track position is: a slot index, or one of the
    /// two sentinels below. Anything reading the track must go through this
    /// rather than assume one card per slot.
    /// </summary>
    internal static int SlotAt(int trackPosition)
        => trackPosition >= 0 && trackPosition < _plan.Count
            ? _plan[trackPosition]
            : NotASlot;

    /// <summary>Sentinel for a card that is not one of the run's puzzles.</summary>
    internal const int NotASlot = -1;

    /// <summary>Not a slot: a divider between packs.</summary>
    private const int Divider = -1;

    /// <summary>Not a slot: the finale.</summary>
    private const int CreditsCard = -2;

    /// <summary>
    /// What each position in the built track IS.
    ///
    /// The track is no longer one card per slot: pack dividers and the credits
    /// card are cards too. Reading a slot index off a card's position was
    /// already an assumption; with dividers in the list it would simply be
    /// wrong, and a wrong slot index launches the wrong puzzle and files a
    /// check against the wrong location. So the mapping is built at the same
    /// moment the track is, and read back rather than inferred.
    /// </summary>
    private static readonly List<int> _plan = new();


    /// <summary>
    /// The slot whose card was just clicked, read by the StartLevel patch.
    /// Cleared on use so a launch from anywhere else cannot inherit it.
    /// </summary>
    private static int _pendingSlot = -1;

    /// <summary>
    /// The frame the pending slot was set on.
    ///
    /// The click path calls StartLevel with its DEFAULT startLevelIndex of 0 -
    /// the level to start is decided elsewhere - so the argument cannot be used
    /// to confirm the pending slot belongs to this launch. What can is time: a
    /// real launch follows the click immediately, while a click whose launch
    /// was dropped is followed by nothing.
    /// </summary>
    private static int _pendingFrame = -1;

    internal static bool Active => _state != null;
    internal static TrackState? State => _state;

    internal static void Begin(SlotData slot)
    {
        _state = new TrackState(slot);
        _order.Clear();
        foreach (var entry in slot.Slots) _order.Add(entry.LevelIndex);

        Plugin.Logger.LogInfo(
            $"track: {_order.Count} puzzles, {_state.OpenSlots} open, "
            + $"{_state.PackTotal} packs");
        ApplyUnlocks();
        Rebuild();
    }

    internal static void End()
    {
        _state = null;
        _creditsSearched = false;
        _credits = null;
        _chapters = null;
        _creditsShown = false;
        _plan.Clear();
        _order.Clear();
        Rebuild();
    }

    /// <summary>
    /// Whether the credits card was on the track last time it was built.
    ///
    /// The Credits item can arrive at any moment, and nothing else would
    /// trigger a rebuild - so the card did not show up until something else
    /// happened to force one, which in play meant restarting the game.
    /// </summary>
    private static bool _creditsShown;

    internal static void TickCreditsCard()
    {
        if (_state == null) return;
        if (Inventory.HasCredits == _creditsShown) return;

        _creditsShown = Inventory.HasCredits;
        Plugin.Logger.LogInfo("track: the credits card is now on the track");
        Rebuild();
    }

    /// <summary>
    /// The next open slot with something still to collect, in track order.
    /// Returns -1 when the run has nothing left to point at.
    /// </summary>
    internal static int NextUnfinishedSlot()
    {
        if (_state == null) return -1;

        // Forward from where the player IS, wrapping only once nothing is left
        // ahead. Searching from zero every time sent "next" back to the start
        // of the run after finishing a puzzle near the end, which is not what
        // an arrow pointing forwards should do.
        var from = Checks.CurrentSlot;
        var count = _state.Slots.Count;
        var start = from >= 0 ? from + 1 : 0;

        for (int step = 0; step < count; step++)
        {
            var slot = (start + step) % count;
            if (!_state.IsOpen(slot)) continue;
            if (!HasWorkLeft(slot)) continue;
            return slot;
        }
        return -1;
    }

    private static bool HasWorkLeft(int slot)
    {
        var router = Checks.Router;
        if (router == null) return true;

        foreach (var name in router.ForSlot(slot))
        {
            if (!Checks.Ledger.IsCollected(name)) return true;
        }
        return false;
    }

    /// <summary>
    /// Mark a slot as the one about to launch, and return its level index.
    ///
    /// Used by routes that bypass the card click - the "next" arrow - so the
    /// launch still gets its baked seed and forced reload.
    /// </summary>
    internal static int ArmSlot(int slot)
    {
        if (_state == null || slot < 0 || slot >= _state.Slots.Count) return -1;

        _pendingSlot = slot;
        _pendingFrame = UnityEngine.Time.frameCount;
        Checks.EnterSlot(slot);
        return _state.Slots[slot].LevelIndex;
    }

    /// <summary>
    /// The last open slot with something the player can actually do now.
    ///
    /// Scans from the END of the track backwards. "Can do now" is stricter than
    /// "unfinished": a slot whose remaining checks are all blocked by an
    /// ability or a pack is skipped, because sending someone to a puzzle they
    /// cannot progress is worse than sending them nowhere.
    /// </summary>
    internal static int FarthestPlayableSlot()
    {
        if (_state == null) return -1;

        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var router = Checks.Router;

        for (int slot = _state.Slots.Count - 1; slot >= 0; slot--)
        {
            if (!_state.IsOpen(slot)) continue;
            if (router == null || progress == null || abilities == null) return slot;

            foreach (var name in router.ForSlot(slot))
            {
                if (Checks.Ledger.IsCollected(name)) continue;
                if (!progress.IsReachable(name, _state.PacksHeld, abilities)) continue;
                return slot;
            }
        }
        return -1;
    }

    /// <summary>
    /// Start a slot's puzzle, with its baked seed and a forced reload.
    ///
    /// Goes through ArmSlot so the StartLevel patch applies both - without them
    /// a generator level comes up empty. Returns false if it could not start,
    /// so a caller can fall back to the game's own behaviour.
    /// </summary>
    internal static bool LaunchSlot(int slot)
    {
        try
        {
            var manager = GameManager.Instance?.levelManager;
            if (manager == null) return false;

            var index = ArmSlot(slot);
            if (index < 0) return false;

            manager.StartLevel(index, true, false, -1);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not launch slot {slot}: {e.Message}");
            return false;
        }
    }

    private static float _sinceTrackCheck;

    /// <summary>
    /// Rebuild the track if what is on screen does not match the plan.
    ///
    /// The rebuild used to hang off LevelsTrack.MenuActivated, and that event
    /// does not fire on every route into the level select - opening it from the
    /// TITLE does not raise it. When that happened the SetLevels and
    /// SetupSections postfixes still replaced the data, so the menu held our
    /// entries and pack sections, but nothing called LevelsTrack.Init, so no
    /// icons were ever built. The result is a level select that renders as an
    /// empty coloured screen - correct underneath, invisible on top.
    ///
    /// Comparing the card count against the plan catches that whatever route
    /// was taken, including ones nobody has found. It is the cheap check the
    /// event-based version should have had behind it from the start.
    /// </summary>
    internal static void TickTrackIntegrity(float dt)
    {
        if (_state == null || _plan.Count == 0) return;

        _sinceTrackCheck += dt;
        if (_sinceTrackCheck < 1f) return;
        _sinceTrackCheck = 0f;

        try
        {
            var track = CampaignTrack();
            if (track == null || !track.gameObject.activeInHierarchy) return;

            var items = track.trackItems;
            var showing = items == null ? 0 : items.Count;
            if (showing == _plan.Count) return;

            Plugin.Logger.LogWarning(
                $"track: {showing} cards on screen for {_plan.Count} planned - rebuilding");
            Rebuild();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: integrity check failed: {e.Message}");
        }
    }

    /// <summary>How many packs the player holds. Absolute - see TrackState.</summary>
    internal static void SetPacksHeld(int packs)
    {
        if (_state == null) return;

        var before = _state.PacksHeld;
        _state.SetPacksHeld(packs);
        if (_state.PacksHeld == before) return;

        var revealed = _state.SlotsRevealedBy(before);
        Plugin.Logger.LogInfo(
            $"track: {_state.PacksHeld}/{_state.PackTotal} packs, "
            + $"{_state.OpenSlots} puzzles open (+{revealed.Count})");

        ApplyUnlocks();
        Rebuild();
    }

    /// <summary>
    /// Give every open slot a save entry, which IS what unlocked means.
    ///
    /// Only ever adds. Removing entries would be how a pack "un-reveals" a
    /// puzzle, and nothing should ever do that - progress only moves forward,
    /// and a player mid-puzzle when a resync arrives should not lose the card
    /// out from under them.
    /// </summary>
    private static void ApplyUnlocks()
    {
        if (_state == null) return;

        try
        {
            var manager = GameManager.Instance?.levelManager;
            if (manager == null) return;

            int created = 0, flagged = 0;

            // The dividers first. A chapter card with no completion entry draws
            // as locked, which would make every pack break look like a wall.
            foreach (var chapter in ChapterCards(manager))
            {
                if (chapter == null) continue;
                if (SaveSystem.data.LevelHasCompletionData(chapter)) continue;

                SaveSystem.data.CreateLevelCompletionData(chapter, null);
                created++;

                var entry = SaveSystem.data.GetLevelCompletionData(chapter);
                if (entry != null && !entry.unlockedOnLevelSelect)
                {
                    entry.unlockedOnLevelSelect = true;
                    flagged++;
                }
            }

            for (int i = 0; i < _state.OpenSlots && i < _order.Count; i++)
            {
                var level = manager.GetLevelInterface(_order[i]);
                if (level == null) continue;

                // ONLY on creation. The flag is not "this level is unlocked",
                // it is "reveal this level the next time the select opens" -
                // the track plays its unlock animation and then CLEARS it.
                //
                // Re-asserting it on every menu activation fought that: the
                // animation replayed on every visit, dragging the track around
                // and starting a level nobody clicked. Set it once, when the
                // record is made, and let the game take it from there.
                if (SaveSystem.data.LevelHasCompletionData(level)) continue;

                SaveSystem.data.CreateLevelCompletionData(level, null);
                created++;

                // Still needed at creation: CreateLevelCompletionData writes it
                // true for archive levels but false for campaign and generator
                // ones, and a run mixes all three.
                var record = SaveSystem.data.GetLevelCompletionData(level);
                if (record != null && !record.unlockedOnLevelSelect)
                {
                    record.unlockedOnLevelSelect = true;
                    flagged++;
                }
            }

            if (created > 0 || flagged > 0)
            {
                SaveSystem.SaveGame();
                Plugin.Logger.LogInfo(
                    $"track: revealed {created} puzzle(s), unlocked {flagged}");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"track: could not apply unlocks: {e.Message}");
        }
    }

    /// <summary>
    /// Guards against a rebuild triggered from inside a rebuild.
    /// </summary>
    private static bool _rebuilding;

    /// <summary>
    /// True only for the campaign level select, not the Archive or DLC menus.
    ///
    /// Both of those DERIVE from LevelSelect, so a patch on the base class fires
    /// for all three and a plain FindObjectOfType returns whichever exists. Left
    /// unchecked, connecting replaces the Archive menu's contents with the run
    /// and the rebuild lands on an arbitrary track - which is why one menu open
    /// was rebuilding six times.
    /// </summary>
    private static bool IsCampaignSelect(LevelSelect? select)
        => select != null
           && select.TryCast<ArchiveMenu>() == null
           && select.TryCast<DLCLevelSelect>() == null;

    /// <summary>
    /// The campaign track, or null. Anything drawing on cards must use this
    /// rather than searching, or it will find the Archive's track instead.
    /// </summary>
    internal static LevelsTrack? CampaignTrack()
    {
        var select = FindCampaignSelect();
        return select == null ? null : select.levelsTrack;
    }

    /// <summary>The campaign level select, or null if it is not built yet.</summary>
    private static LevelSelect? FindCampaignSelect()
    {
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<LevelSelect>())
        {
            if (IsCampaignSelect(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Force the level select to rebuild if it already exists.
    ///
    /// Needed because the menu is constructed once and cached: reopening it
    /// does not re-run Setup, so a track installed after that first build is
    /// invisible until these four calls are made by hand.
    ///
    /// SetLevels and SetupSections alone are NOT enough - they change the data
    /// the menu holds while leaving the icons that were already laid out on
    /// screen. Init and SetInitialScrollPosition are what rebuild the track
    /// itself. Leaving them out is why the first attempt showed the vanilla
    /// campaign with the patches applied and no error anywhere.
    /// </summary>
    private static void Rebuild()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
            var select = FindCampaignSelect();
            if (select == null) return;      // not built yet; the patch will catch it

            select.SetLevels();
            select.SetupSections();

            // Re-derive which section is active and redraw the header. Without
            // this the menu keeps whatever ActiveSection it had before we
            // replaced the list, and the header that names the focused card
            // comes up blank - which reads in game as "hovering a level does
            // not show its name".
            select.SetActiveSection();
            select.SetMenuHeader();
            select.SetMenuTitle();
            select.SetMenuDetails();

            // The level select recolours its background per section. Without
            // this it keeps whatever colour the puzzle you just left was using,
            // which reads as "the level is still there behind the menu".
            select.SetSectionBackgroundColor();

            // The select's OWN track, never a search - the Archive and DLC
            // menus each have one too.
            var track = select.levelsTrack;
            if (track != null)
            {
                track.Init();
                track.SetInitialScrollPosition();
            }

            DescribeTrack(track);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not rebuild the level select: {e.Message}");
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>
    /// Log how the built track lines up with our slots.
    ///
    /// The track is not one icon per slot: chapter headers are items too - the
    /// vanilla campaign builds 85 items for 80 levels. Something has to say out
    /// loud where our slots actually landed, because an unnoticed offset here
    /// launches the puzzle next to the one that was clicked.
    /// </summary>
    private static void DescribeTrack(LevelsTrack? track)
    {
        if (track == null || track.trackItems == null) return;

        var items = track.trackItems;
        Plugin.Logger.LogInfo(
            $"track: built {items.Count} items for {_order.Count} slots");

        for (int i = 0; i < items.Count && i < 3; i++)
        {
            var level = items[i]?.level;
            Plugin.Logger.LogInfo(
                $"track:   item[{i}] {(level == null ? "<none>" : level.LevelId)}");
        }
    }

    /// <summary>
    /// Re-apply every time the levels menu opens.
    ///
    /// The postfixes below only fire when the game itself rebuilds, which it
    /// does once. This is the hook that runs on each open.
    /// </summary>
    [HarmonyPatch(typeof(LevelsTrack), nameof(LevelsTrack.MenuActivated))]
    [HarmonyPostfix]
    private static void AfterMenuActivated(LevelsTrack __instance)
    {
        if (_state == null) return;
        if (!IsCampaignSelect(__instance?.levelSelect)) return;
        ApplyUnlocks();
        Rebuild();
    }

    /// <summary>Replace the track's contents with the seed's puzzles.</summary>
    [HarmonyPatch(typeof(LevelSelect), nameof(LevelSelect.SetLevels))]
    [HarmonyPostfix]
    private static void AfterSetLevels(LevelSelect __instance)
    {
        if (_state == null || !IsCampaignSelect(__instance)) return;
        try
        {
            var manager = GameManager.Instance?.levelManager;
            if (manager == null) return;

            var levels = new Il2CppSystem.Collections.Generic.List<LevelInterface>();
            _plan.Clear();

            var bounds = _state.Boundaries;
            var dividers = ChapterCards(manager);
            var nextBound = 0;

            for (int slot = 0; slot < _order.Count; slot++)
            {
                // A divider goes in wherever a pack starts, so the breaks a
                // player scrolls past are the blocks they actually unlocked.
                while (nextBound < bounds.Count && bounds[nextBound] == slot)
                {
                    var divider = DividerFor(dividers, nextBound);
                    if (divider != null)
                    {
                        levels.Add(divider);
                        _plan.Add(Divider);
                    }
                    nextBound++;
                }

                var level = manager.GetLevelInterface(_order[slot]);
                if (level == null) continue;

                levels.Add(level);
                _plan.Add(slot);
            }

            // The finale, once the Credits item has arrived. Appended rather
            // than being one of the seed's slots, because it is not a puzzle
            // and holds no checks - it is what the run ends with, and it is on
            // the track at all only once the item granting it has been found.
            var credits = CreditsLevel(manager);
            if (credits != null && Inventory.HasCredits)
            {
                levels.Add(credits);
                _plan.Add(CreditsCard);
            }

            __instance.Levels = levels;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"track: SetLevels override failed: {e.Message}");
        }
    }

    /// <summary>
    /// One section per pack, so the track has breaks in it.
    ///
    /// Vanilla splits the track into five chapters. Partitioning a randomized
    /// run by each puzzle's ORIGINAL chapter would scatter it back into the
    /// buckets its puzzles came from - five chapters in no order, which is not
    /// a run. But one unbroken section of 79 cards is no better: there is
    /// nothing to navigate by and no sense of progress.
    ///
    /// Packs are the natural unit, because they are what the player actually
    /// earns. The first section is the free opening; after that, one per pack,
    /// bounded exactly where pack_boundaries says.
    /// </summary>
    [HarmonyPatch(typeof(LevelSelect), nameof(LevelSelect.SetupSections))]
    [HarmonyPostfix]
    private static void AfterSetupSections(LevelSelect __instance)
    {
        if (_state == null || !IsCampaignSelect(__instance)) return;
        try
        {
            var levels = __instance.Levels;
            if (levels == null || _plan.Count == 0) return;

            var sections = new Il2CppSystem.Collections.Generic.List<LevelSelect.Section>();

            // Boundaries are in SLOTS; TrackStartIndex is in CARDS, and the two
            // differ by every divider inserted before that point. Walking the
            // plan converts one to the other without having to count dividers
            // by hand.
            var breaks = new List<int> { 0 };
            for (int i = 1; i < _plan.Count; i++)
            {
                if (_plan[i] == Divider || _plan[i] == CreditsCard) breaks.Add(i);
            }
            breaks.Add(_plan.Count);

            int step = 0;
            for (int b = 0; b + 1 < breaks.Count; b++)
            {
                var start = breaks[b];
                var end = Math.Min(breaks[b + 1], levels.Count);
                if (end <= start) continue;

                var slice = new Il2CppSystem.Collections.Generic.List<LevelInterface>();
                for (int i = start; i < end; i++) slice.Add(levels[i]);

                var title = _plan[start] == CreditsCard ? "The End"
                    : step == 0 ? "Opening"
                    : $"Pack {step}";
                step++;

                sections.Add(new LevelSelect.Section
                {
                    SectionIndex = sections.Count + 1,
                    TrackStartIndex = start,
                    SectionTitle = title,
                    SectionLevels = slice,

                    // Without this the section's colour is a default-constructed
                    // Color - transparent - so the track kept whatever the
                    // puzzle you just left had painted, which looked exactly
                    // like the level was still open behind the menu.
                    BackgroundColor = SectionColour(sections.Count),
                });
            }

            if (sections.Count > 0) __instance.Sections = sections;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"track: SetupSections override failed: {e.Message}");
        }
    }

    /// <summary>
    /// A background colour per section.
    ///
    /// Taken from the game's own chapter colours where there are enough of
    /// them, so the track looks like the game rather than like a mod, and
    /// cycling after that. Any opaque colour beats the transparent default.
    /// </summary>
    private static UnityEngine.Color SectionColour(int index)
    {
        var palette = new[]
        {
            new UnityEngine.Color(0.28f, 0.35f, 0.39f),
            new UnityEngine.Color(0.35f, 0.33f, 0.42f),
            new UnityEngine.Color(0.24f, 0.36f, 0.34f),
            new UnityEngine.Color(0.40f, 0.32f, 0.30f),
            new UnityEngine.Color(0.30f, 0.31f, 0.40f),
            new UnityEngine.Color(0.26f, 0.34f, 0.30f),
        };
        return palette[index % palette.Length];
    }

    private static LevelInterface? _credits;
    private static bool _creditsSearched;

    private static List<LevelInterface>? _chapters;

    /// <summary>
    /// The game's own chapter cards, used as pack dividers.
    ///
    /// Reused rather than built: a chapter card is a LevelInterface the track
    /// already knows how to draw, complete with its number and border. Making
    /// our own would mean reproducing that presentation and keeping it in step
    /// with the game's art.
    /// </summary>
    private static List<LevelInterface> ChapterCards(LevelManager manager)
    {
        if (_chapters != null) return _chapters;

        _chapters = new List<LevelInterface>();
        try
        {
            var all = manager.LevelInterfaces;
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var level = all![i];
                if (level != null && level.LevelType == LevelType.Chapter)
                {
                    _chapters.Add(level);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not find chapter cards: {e.Message}");
        }
        return _chapters;
    }

    /// <summary>
    /// The divider for pack N, or null if the game has fewer chapter cards
    /// than the run has packs.
    ///
    /// Null rather than reusing one: a repeated chapter number reads as a bug,
    /// and a run with no divider is merely plainer than one with.
    /// </summary>
    private static LevelInterface? DividerFor(List<LevelInterface> chapters, int pack)
        => pack >= 0 && pack < chapters.Count ? chapters[pack] : null;

    /// <summary>
    /// The game's credits "level", found once and remembered.
    ///
    /// Located by its own IsCredits flag rather than a hardcoded index, so it
    /// survives the game adding content ahead of it.
    /// </summary>
    private static LevelInterface? CreditsLevel(LevelManager manager)
    {
        if (_creditsSearched) return _credits;
        _creditsSearched = true;

        try
        {
            var all = manager.LevelInterfaces;
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var level = all![i];
                if (level != null && level.IsCredits) { _credits = level; break; }
            }

            if (_credits == null) Plugin.Logger.LogWarning("track: no credits level found");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not find the credits: {e.Message}");
        }
        return _credits;
    }

    /// <summary>
    /// Which slot an icon IS, by its position in the track.
    ///
    /// Position, not level index, because a run can contain the SAME level
    /// more than once - 16 of 79 slots in the reference seed do. Those repeats
    /// carry different generator seeds and are different puzzles, so looking a
    /// card up by its level would collapse them onto the first one and launch
    /// the wrong layout.
    /// </summary>
    private static int SlotIndexOf(LevelIcon icon)
    {
        try
        {
            var track = icon.levelsTrack;
            var items = track == null ? null : track.trackItems;
            if (items != null)
            {
                var position = items.IndexOf(icon);
                if (position >= 0 && position < _plan.Count) return _plan[position];

                if (position >= 0)
                {
                    Plugin.Logger.LogWarning(
                        $"track: card at position {position} but the plan covers "
                        + $"{_plan.Count} ({items.Count} cards on the track)");
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not place a card: {e.Message}");
        }

        // No fallback to "find this level in the run". With dividers and
        // repeated levels on the track, a lookup by level is a guess, and a
        // wrong slot files a check against another puzzle's location.
        return Divider;
    }

    /// <summary>
    /// Swallow a click on a pack divider completely.
    ///
    /// Blocking DoStartLevel was not enough. A real click enters through
    /// OnPointerClick, which also selects the card and drives a transition -
    /// and a chapter card has no puzzle behind it, so that transition ends on a
    /// blank screen. Testing with DoStartLevel directly missed this entirely:
    /// the guard fired, the track stayed intact, and the bug was still there
    /// for anyone using a mouse.
    ///
    /// Refused at the click, so a divider behaves as the scenery it is.
    /// </summary>
    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.OnPointerClick))]
    [HarmonyPrefix]
    private static bool BeforeIconClicked(LevelIcon __instance)
    {
        try
        {
            if (_state == null) return true;
            if (SlotIndexOf(__instance) != Divider) return true;

            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: click check failed, allowing: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Refuse to launch a puzzle the player has not been given yet, and
    /// remember which slot is being launched.
    ///
    /// DoStartLevel is the choke point every launch path funnels into, which is
    /// why the guard sits here rather than on the click handler - verified in
    /// Phase 0 by invoking it directly.
    /// </summary>
    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.DoStartLevel))]
    [HarmonyPrefix]
    private static bool BeforeDoStartLevel(LevelIcon __instance)
    {
        try
        {
            if (_state == null) return true;

            var planned = SlotIndexOf(__instance);

            // A pack divider is scenery. Vanilla chapter cards are not
            // playable and ours must not be either.
            if (planned == Divider) return false;

            // The credits card is not a slot. It is playable only once enough
            // puzzles have actually been beaten.
            if (planned == CreditsCard)
            {
                var left = Credits.Remaining(Plugin.Seed);
                if (left <= 0) return true;

                Plugin.Logger.LogInfo($"track: credits locked, {left} puzzle(s) to go");
                Toasts.Show($"Beat {left} more puzzle(s) to reach the credits", Toasts.Notice);
                return false;
            }

            var slot = planned;
            if (!_state.IsOpen(slot))
            {
                _pendingSlot = -1;
                Plugin.Logger.LogInfo(
                    $"track: slot {slot} is not unlocked yet ({_state.OpenSlots} open)");
                return false;
            }

            _pendingSlot = slot;
            _pendingFrame = UnityEngine.Time.frameCount;

            // Recorded on the CLICK, which is the only place the exact card is
            // known. Waiting for StartLevel would lose it: that call cannot say
            // which of two cards for the same level was clicked.
            Checks.EnterSlot(slot);
            return true;
        }
        catch (Exception e)
        {
            // Never let the guard break the game: on doubt, let them play.
            _pendingSlot = -1;
            Plugin.Logger.LogWarning($"track: lock check failed, allowing: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Give the launch the seed the generator baked for that slot.
    ///
    /// Done by rewriting the argument rather than by calling StartLevel
    /// ourselves: DoStartLevel also drives the menu transition and the level
    /// -selected event, and reimplementing that to pass one extra int would be
    /// trading a one-line patch for a copy of the game's own flow.
    /// </summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.StartLevel))]
    [HarmonyPrefix]
    private static void BeforeStartLevel(ref bool forceReload, ref int randomSeed)
    {
        var slot = _pendingSlot;
        var age = UnityEngine.Time.frameCount - _pendingFrame;
        _pendingSlot = -1;                           // one launch, one use

        if (_state == null || slot < 0 || slot >= _state.Slots.Count) return;

        // Only a launch that follows its click. A click whose launch was
        // dropped - the track re-centres and selects another card - used to
        // leave the slot set, and the next StartLevel from anywhere inherited
        // it, rebuilding an unrelated level from another puzzle's seed.
        if (age > 2)
        {
            Plugin.Logger.LogWarning(
                $"track: ignoring a pending slot {slot} set {age} frames ago");
            return;
        }

        var entry = _state.Slots[slot];
        if (entry.Seed < 0) return;                  // no baked layout for this one

        randomSeed = unchecked((int)entry.Seed);

        // Both of these, or a generator level comes up EMPTY - no objects, no
        // way out, nothing in the log. Those levels are normally reached only
        // through Daily Tidy, which builds them from a seed; reached from the
        // level select they are never populated unless we say so here. The boot
        // command always passed both, which is exactly why scripted testing
        // never saw it and the first real click did.
        forceReload = true;

        Plugin.Logger.LogInfo(
            $"track: slot {slot} {entry.LevelId} launching with seed {randomSeed}, forceReload");
    }

}
