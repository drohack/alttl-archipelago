using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using HarmonyLib;
using ALTTLModKit;

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
    /// <summary>
    /// Which pack a divider card announces, 1-based, or 0 if that position is
    /// not a divider.
    ///
    /// Needed because the dividers REUSE the game's five chapter cards - there
    /// are only five and a run can have many more packs - so the card's own
    /// printed number is whatever chapter it came from. Pack 6 borrows chapter
    /// 1's card and prints "1", which reads as a bug because it is one.
    /// </summary>
    internal static int PackNumberAt(int trackPosition)
    {
        if (trackPosition < 0 || trackPosition >= _plan.Count) return 0;
        if (_plan[trackPosition] != Divider) return 0;

        var n = 0;
        for (int i = 0; i <= trackPosition; i++)
        {
            if (_plan[i] == Divider) n++;
        }
        return n;
    }

    /// <summary>
    /// The track position to open the level select on: the last card the
    /// player can actually do something with.
    ///
    /// The game opens the track at its far end, which on a 40-puzzle run means
    /// scrolling back past everything every single time. The last PLAYABLE
    /// slot is where a player is actually working - see FarthestPlayableSlot,
    /// which skips slots whose remaining checks are all blocked.
    /// </summary>
    internal static int PositionToOpenOn()
    {
        var slot = FarthestPlayableSlot();
        if (slot < 0) return -1;

        for (int i = 0; i < _plan.Count; i++)
        {
            if (_plan[i] == slot) return i;
        }
        return -1;
    }

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
    ///
    /// The click path calls StartLevel with its DEFAULT startLevelIndex of 0 -
    /// the level to start is decided elsewhere - so that argument alone cannot
    /// confirm the arm belongs to this launch. In 0.3.0 the only other evidence
    /// was _pendingFrame with a two-frame budget, and the arm was cleared
    /// before the budget was checked, so any unrelated launch in between
    /// consumed it. ResolveSlotFor now takes either of two proofs - the level
    /// index matching, or the arm being fresh - and clears only on use.
    /// </summary>
    private static int _pendingSlot = -1;

    /// <summary>
    /// The frame <see cref="_pendingSlot"/> was armed on.
    ///
    /// Used to ACCEPT an arm, never to reject one - which is the difference
    /// between this and the version that shipped in 0.3.0. There it was the
    /// only evidence, with a two-frame budget, and the arm was cleared before
    /// the budget was even checked; a Cat Trap restart landing nine frames
    /// later ate it and the level rebuilt unseeded.
    ///
    /// It is still worth having alongside the index match, because the index
    /// match rests on ActiveLevelInterface already pointing at the NEW level
    /// when a click reaches StartLevel. That is what the surrounding code
    /// implies - the card selection sets the active level and StartLevel takes
    /// its index argument as a default 0 - but it is an inference, not
    /// something measured. An arm placed this frame or last is a launch that
    /// followed its own click either way, so the two signals together do not
    /// depend on that inference being right.
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
        // The cached "home" campaign level belongs to the run that is ending -
        // it was chosen to avoid that run's slots, and the next run has
        // different ones.
        Navigation.Reset();

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

        // TWO PASSES, AND THE FIRST ONE IS THE POINT.
        //
        // This used to ask only HasWorkLeft - "is anything here uncollected" -
        // which is not the same question as "is there anything to DO". A card
        // whose remaining checks are all behind an ability you have not found
        // still counts as unfinished, so the arrow cheerfully opened a puzzle
        // the player could not advance. droha: "I was expecting it to bring me
        // to the next level with something to do."
        //
        // Play already knew better: FirstPlayableSlot has always filtered on
        // reachability. The arrow simply never got the same treatment.
        for (int step = 0; step < count; step++)
        {
            var slot = (start + step) % count;
            if (!_state.IsOpen(slot)) continue;
            if (!HasReachableWork(slot)) continue;
            return slot;
        }

        // Nothing reachable anywhere. Fall back to merely unfinished rather
        // than refusing to move: an arrow that does nothing is worse than an
        // arrow that lands somewhere honest, and the card's own badge already
        // says the work there is blocked.
        for (int step = 0; step < count; step++)
        {
            var slot = (start + step) % count;
            if (!_state.IsOpen(slot)) continue;
            if (!HasWorkLeft(slot)) continue;
            return slot;
        }
        return -1;
    }

    /// <summary>Any uncollected location on this slot, reachable or not.</summary>
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
    /// Any uncollected location on this slot the run can actually earn now.
    ///
    /// The predicate behind Play, the next arrow and the level-select opening
    /// scroll. It lived inline in three places and the arrow used a weaker
    /// version of it, which is exactly how the three drifted apart.
    /// </summary>
    private static bool HasReachableWork(int slot)
    {
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var router = Checks.Router;

        // Before the run is fully wired up, treat the slot as playable rather
        // than hiding it - the same fallback the callers used individually.
        if (router == null || progress == null || abilities == null) return true;

        var packs = _state?.PacksHeld ?? 0;
        foreach (var name in router.ForSlot(slot))
        {
            if (Checks.Ledger.IsCollected(name)) continue;
            if (!progress.IsReachable(name, packs, abilities)) continue;
            return true;
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

        // Arming is NOT the same as playing, and conflating them was a bug.
        // AfterGetNextLevelIndex arms from a postfix on GetNextLevelIndex,
        // which the game also calls while building the post-level UI - so this
        // used to announce a slot the player had not started. The 0.3.0
        // playtest log caught it:
        //
        //     checks: now playing slot 22
        //     checks: playing Breadtags as slot 19 (resolved from the running level)
        //
        // Checks.EnsureSlot corrected it there by looking at the running level,
        // but only because the running level happened to be resolvable; on a
        // repeated level its fallback picks the earliest open instance, which
        // can file a check against the wrong card. EnterSlot now happens where
        // the launch actually resolves, in BeforeStartLevel.
        _pendingSlot = slot;
        _pendingFrame = UnityEngine.Time.frameCount;
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
    /// <summary>
    /// The FIRST open slot with something the player can do now.
    ///
    /// What Play should open. FarthestPlayableSlot is the right answer for
    /// where to SCROLL the level select - the far end of what is available is
    /// where a player is working - but the wrong one for Play, which on a
    /// fresh run jumped past three perfectly playable puzzles to land on the
    /// fourth. Play means "carry on", and carrying on starts at the earliest
    /// thing not yet done.
    /// </summary>
    internal static int FirstPlayableSlot()
    {
        if (_state == null) return -1;

        for (int slot = 0; slot < _state.Slots.Count; slot++)
        {
            if (!_state.IsOpen(slot)) continue;
            if (HasReachableWork(slot)) return slot;
        }
        return -1;
    }

    internal static int FarthestPlayableSlot()
    {
        if (_state == null) return -1;

        for (int slot = _state.Slots.Count - 1; slot >= 0; slot--)
        {
            if (!_state.IsOpen(slot)) continue;
            if (HasReachableWork(slot)) return slot;
        }
        return -1;
    }

    // Track.LaunchSlot USED TO LIVE HERE AND IS DELETED ON PURPOSE.
    //
    // It called manager.StartLevel(index, true, false, -1) - note the third
    // argument, forceReload: false. That was wrong twice over.
    //
    // First, StartLevel loads a level UNDERNEATH whatever screen is up without
    // changing the game state, which is what made the daily-page rescue run
    // away: the state stayed DailyTidy, the watchdog saw it again next frame,
    // and hundreds of levels initialised on top of each other. See
    // DailyGuard.RescueAttempts.
    //
    // Second, forceReload: false. A generator slot survives that because
    // BeforeStartLevel upgrades the flag to true on its way through; a
    // campaign slot takes the `Seed < 0` early return and never gets the
    // upgrade, so relaunching an already-loaded campaign level would not
    // reload it. Harmless while the campaign was unreachable, and a real bug
    // the moment base_weight made those levels routine.
    //
    // It had no callers left. Anything that wants to open a slot should do
    // what DailyGuard.Rescue does: queue the slot with
    // TitleScreen.QueueSlotForGameplay and let the game's own state transition
    // carry it.

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
    ///
    /// IT IS A BACKSTOP, NOT THE MECHANISM. droha asked the right question -
    /// why poll for something that has an event - and the answer is that the
    /// events cover different subsets: MenuActivated misses the route in from
    /// the title, SetupSections only fires when the game rebuilds. Both now
    /// call Badges.RepaintSoon, so between them the first paint is event
    /// driven and this timer no longer decides when the player sees the run's
    /// furniture. What it still earns its keep for is the case the comment
    /// above describes: a route nobody has found, where the data is replaced
    /// but no icons are built, which renders as an empty coloured screen.
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

                // ONLY on creation. Re-asserting it on every menu activation
                // replayed the unlock animation on every visit, dragging the
                // track around and starting a level nobody clicked.
                //
                // MEASURED 2026-09-09, because the surrounding claim was wrong.
                // This used to say the track "plays its unlock animation and
                // then CLEARS it", which contradicts content-report.md:110
                // ("this card has already played its unlock animation") and is
                // not what happens: reading the save either side of opening the
                // level select shows the flags unchanged, 8 true and 1 false
                // both times. Nothing clears anything.
                //
                // That matters because it settles a suspected bug as not one.
                // Vanilla creates a completion row of its own whenever a level
                // is beaten - "finish N, create N+1" - and those rows arrive
                // with the flag false and are skipped by the `continue` below,
                // so they never get repaired. It looked like the replay bug
                // coming back once campaign levels became drawable. It cannot:
                // a replay needs the flag re-SET, and nothing re-sets it.
                //
                // A vanilla row also cannot make a locked card playable, which
                // was the other worry. IsRefused gates on _state.IsOpen, the
                // run's own pack state, and never reads the save.
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

        // The other half of the event coverage. MenuActivated fires on every
        // open EXCEPT from the title; SetupSections fires on the rebuild, which
        // is what the title route does raise. Between the two, the first paint
        // is driven by an event on every route we know of, and the poll below
        // is left as a backstop for one we do not.
        Badges.RepaintSoon();
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

            // ASKED FOR HERE, APPLIED LATER. See TickScroll.
            _scrollPending = 0.35f;

            // The menu is being built right now, so every poll that decorates
            // it is due immediately. Without this the badges, dots and tag
            // arrive whenever their own timers next come round - up to a
            // second after the screen has settled, which reads as the mod
            // popping in late.
            _sinceTrackCheck = float.MaxValue;
            Badges.RepaintSoon();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"track: SetupSections override failed: {e.Message}");
        }
    }

    /// <summary>
    /// Scroll the track to the last card worth looking at.
    ///
    /// The game opens the level select at the far END of the track. On the
    /// vanilla campaign that is roughly where the player is; on a run it is
    /// forty cards past anything they can do, so every visit starts with a
    /// scroll back. Reported from a playtest as "it scrolls me to the end
    /// every time".
    ///
    /// FarthestPlayableSlot is the right target rather than "last unlocked":
    /// it already skips a slot whose remaining checks are all blocked by an
    /// ability or a pack, so this lands on work rather than on a locked card.
    ///
    /// Failure here is cosmetic, so it is swallowed rather than allowed to
    /// take the section layout down with it - the track is already built and
    /// correct by this point, and a run that opens at the wrong scroll
    /// position is a nuisance, not a broken one.
    /// </summary>
    private static float _scrollPending;

    /// <summary>
    /// Apply the opening scroll once the level select has settled.
    ///
    /// Doing it inside SetupSections did not work, and the log is what proved
    /// it: the mod picked the right card - "opening the level select on card
    /// 3" - and the screen still arrived at the far end. The game finishes
    /// opening AFTER the sections are built and scrolls where it likes, so a
    /// scroll set during setup is simply overwritten.
    ///
    /// A short delay rather than a frame count: the settle is a transition, not
    /// a fixed number of frames, and a frame budget is the kind of guess that
    /// has already cost this project a day elsewhere.
    /// </summary>
    internal static void TickScroll(float dt)
    {
        if (_scrollPending <= 0f) return;

        _scrollPending -= dt;
        if (_scrollPending > 0f) return;
        _scrollPending = 0f;

        OpenOnTheWorkingEnd();
    }

    private static void OpenOnTheWorkingEnd()
    {
        try
        {
            var position = PositionToOpenOn();
            if (position < 0) return;

            // SetScrollToItem is on LevelsTrack, NOT on LevelsOverviewScrollbar.
            // The metadata puts its name next to OverviewItemUnlock, which made
            // the scrollbar look like the owner; it is not, and the compiler
            // said so. Neighbouring names in the string heap are not evidence
            // of a declaring type - that inference has now been wrong twice.
            var track = CampaignTrack();
            if (track == null) return;

            track.SetScrollToItem(position);
            Plugin.Logger.LogInfo(
                $"track: opening the level select on card {position}, the last "
                + "one with something to do");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: could not set the scroll: {e.Message}");
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
    /// The divider for pack N, cycling the game's chapter cards when the run
    /// has more packs than the game has chapters.
    ///
    /// This used to return null past the last chapter, on the reasoning that "a
    /// repeated chapter number reads as a bug, and a run with no divider is
    /// merely plainer than one with". The shortfall turned out to be far worse
    /// than plainer. The game has exactly FIVE chapter cards; items.MAX_PACKS is
    /// 14, so a default 79-puzzle run asks for fifteen dividers. It got five:
    /// breaks before slots 4, 8, 12, 16 and 21, and then nothing at all, so
    /// packs 5 to 14 - fifty-eight cards - ran together as one unbroken block.
    /// That is not plainer, it is the track losing its structure exactly where
    /// a long run needs it most.
    ///
    /// Cycling is safe: a LevelInterface already appears more than once on a
    /// track whenever the run repeats a level, which 16 of 79 slots do in the
    /// reference seed, so nothing here is new. And the repeated NUMBER the old
    /// comment worried about is not what the player reads - AfterSetupSections
    /// titles each section from the pack ("Pack 7"), so the heading stays
    /// correct and only the card art behind it comes round again.
    ///
    /// Instantiating fresh cards was the alternative and was rejected: a cloned
    /// LevelInterface carries a level id and registers with the manager, and
    /// inventing levels to use as scenery is a much larger risk than a repeated
    /// picture.
    /// </summary>
    private static LevelInterface? DividerFor(List<LevelInterface> chapters, int pack)
    {
        if (pack < 0 || chapters.Count == 0) return null;
        return chapters[pack % chapters.Count];
    }

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
            return !IsRefused(__instance, announce: true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"track: click check failed, allowing: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Whether this card must not be launched, and why.
    ///
    /// Shared by the click prefix and the DoStartLevel backstop so the two can
    /// never disagree - which they did in 0.3.0, and that gap is the whole bug.
    /// Only the DIVIDER refusal had been moved up to OnPointerClick; the locked
    /// -slot and locked-credits refusals stayed on DoStartLevel, which runs
    /// AFTER OnPointerClick has selected the card and started the menu
    /// transition. Returning false that late leaves the player in a transition
    /// to nothing: a flat single-colour screen with no way out. That is exactly
    /// the failure already recorded for chapter cards in
    /// docs/verification-log.md:446-455, and these two cases were simply never
    /// moved with it.
    ///
    /// <paramref name="announce"/> is set only on the click, so a refusal
    /// explains itself once rather than twice.
    /// </summary>
    private static bool IsRefused(LevelIcon icon, bool announce)
    {
        if (_state == null) return false;

        var planned = SlotIndexOf(icon);

        // A pack divider is scenery. Vanilla chapter cards are not playable and
        // ours must not be either. SlotIndexOf also returns Divider for a card
        // it could not place, which is the conservative answer.
        if (planned == Divider) return true;

        // The credits card is not a slot. It is playable only once enough
        // puzzles have actually been beaten.
        if (planned == CreditsCard)
        {
            var left = Credits.Remaining(Plugin.Seed);
            if (left <= 0) return false;

            if (announce)
            {
                Plugin.Logger.LogInfo($"track: credits locked, {left} puzzle(s) to go");
                Toasts.Show($"Beat {left} more puzzle(s) to reach the credits", Toasts.Notice);
            }
            return true;
        }

        if (planned < 0) return false;

        if (!_state.IsOpen(planned))
        {
            if (announce)
            {
                Plugin.Logger.LogInfo(
                    $"track: slot {planned} is not unlocked yet ({_state.OpenSlots} open)");
                Toasts.Show("That puzzle is still locked", Toasts.Notice);
            }
            return true;
        }

        return false;
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

            // The refusal itself lives in IsRefused, already applied at the
            // click. Repeated here as a backstop for launch routes that do not
            // go through OnPointerClick at all - a controller, or the boot
            // command - without announcing a second time.
            if (IsRefused(__instance, announce: false))
            {
                _pendingSlot = -1;
                return false;
            }

            var planned = SlotIndexOf(__instance);
            if (planned == CreditsCard) return true;   // allowed, and not a slot

            var slot = planned;
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
    private static void BeforeStartLevel(
        ref int startLevelIndex, ref bool forceReload, ref int randomSeed)
    {
        if (_state == null) return;

        var manager = GameManager.Instance?.levelManager;

        // WHICH level is about to start. The arrow route passes the index
        // outright; the click route leaves it at its default 0, so the index
        // has to come from somewhere else.
        //
        // THE ARMED CARD FIRST, and the active interface only as a fallback.
        // This used to ask the active interface alone, on the reasoning that
        // "the card selection already made the level active". It does not:
        // opening the level select tears the previous level down, so at the
        // moment a card click reaches StartLevel the active interface is NULL.
        // target came out -1, this returned before applying anything, and the
        // clicked card launched a generator with no baked seed - a different
        // layout from the one the run intends.
        //
        // droha found it as "Play opens a different Calendar level than
        // clicking the first card". Play was the correct one; every card click
        // was wrong. It hid for so long because it is invisible on a hand-made
        // level, which is most of them, and because the arrow route - the one
        // the harness exercises - passes a real index and never takes this
        // branch.
        //
        // The click prefix knows exactly which card was clicked, which is more
        // authoritative than anything reconstructed afterwards.
        var target = startLevelIndex > 0
            ? startLevelIndex
            : (PendingLevelIndex() ?? manager?.ActiveLevelInterface?.LevelIndex ?? -1);
        if (target < 0) return;

        var slot = ResolveSlotFor(target);

        // A level the run does not contain, or one we could not place. Falling
        // through used to mean no seed and no reload, and the comment below
        // says what that looks like: an EMPTY level with no way out. That soft
        // lock is never the better outcome, so a generator level still gets a
        // reload and a stable seed derived from its index.
        if (slot < 0)
        {
            if (IsGenerator(manager, target))
            {
                var stable = unchecked((int)(target * 2654435761u));
                randomSeed = stable == 0 ? 1 : stable;
                forceReload = true;
                Plugin.Logger.LogWarning(
                    $"track: level {target} is not a slot in this run - "
                    + "launching it seeded rather than empty");
            }
            return;
        }

        // The slot the launch actually resolved to is the slot being played.
        // This is the one place that is true for every route - click, arrow,
        // retry, cat-trap restart - so it is where the run records it.
        Checks.EnterSlot(slot);

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

    /// <summary>
    /// Which slot a launch of <paramref name="target"/> belongs to.
    ///
    /// This replaces a two-frame TTL on the pending slot, and the 0.3.0
    /// playtest log says why it had to:
    ///
    ///     track: ignoring a pending slot 22 set 9 frames ago
    ///     trap: 1 cat(s) reset the puzzle
    ///
    /// GetNextLevelIndex is a postfix, and the game calls it while building the
    /// post-level UI - not only when launching - so finishing a puzzle armed a
    /// slot speculatively. The old code then cleared the arm BEFORE testing its
    /// age, so the next unrelated StartLevel ate it. A Cat Trap restart landing
    /// in that window rebuilt the running puzzle with randomSeed -1, silently
    /// swapping the player's seeded layout for the generator's stock one; two
    /// instances of one generator that both hit this came out identical, which
    /// is what was reported as "the same random level twice".
    ///
    /// Matching on the level index proves what the frame count only guessed at.
    /// </summary>
    /// <summary>
    /// Does the run hold this level index at all, and if so where?
    ///
    /// A plain membership question, unlike ResolveSlotFor, which picks the
    /// RIGHT slot for a launch. Navigation uses it to choose a level the run
    /// does NOT contain as its route back to the track.
    /// </summary>
    internal static int SlotForLevelIndex(int levelIndex)
    {
        for (int slot = 0; slot < _order.Count; slot++)
        {
            if (_order[slot] == levelIndex) return slot;
        }
        return -1;
    }

    /// <summary>
    /// The level index of the card the player just clicked, if one is armed.
    ///
    /// Null rather than -1 so the caller can tell "no arm" from "arm says
    /// index 0" - index 0 is a chapter card and never a slot, but the
    /// distinction is worth keeping honest.
    ///
    /// Only trusted while the arm is FRESH. A click and its StartLevel happen
    /// within a frame or two of each other; anything older is a leftover from
    /// a launch that never happened, and letting that pick the target is how
    /// the stale-slot bug worked.
    /// </summary>
    private static int? PendingLevelIndex()
    {
        if (_pendingSlot < 0 || _pendingSlot >= _order.Count) return null;
        if (UnityEngine.Time.frameCount - _pendingFrame > 2) return null;
        return _order[_pendingSlot];
    }

    private static int ResolveSlotFor(int target)
    {
        if (_state == null) return -1;

        // 1. The armed slot, on either of two independent proofs that this
        //    launch is the one it was armed for: the level index matches, or it
        //    was armed within the last couple of frames, which only a launch
        //    following its own click can be. A stale arm for a different level
        //    satisfies neither and is now inert rather than something to race
        //    against - which is what let a Cat Trap restart consume it.
        var pending = _pendingSlot;
        if (pending >= 0 && pending < _order.Count)
        {
            var fresh = UnityEngine.Time.frameCount - _pendingFrame <= 2;
            if (_order[pending] == target || fresh)
            {
                _pendingSlot = -1;                   // one launch, one use
                return pending;
            }
        }

        // 2. A relaunch of the level already running - a Cat Trap reset, a
        //    retry, a restart from the pause menu. It must keep the seed it
        //    already had, and it must NOT consume an arm meant for the next
        //    puzzle.
        var current = Checks.CurrentSlot;
        if (current >= 0 && current < _order.Count && _order[current] == target)
            return current;

        // 3. Anything else launching a level the run does contain: the earliest
        //    open slot for it with work left, so a repeated level lands on the
        //    instance the player still has to play.
        for (int slot = 0; slot < _order.Count; slot++)
        {
            if (_order[slot] != target) continue;
            if (!_state.IsOpen(slot)) continue;
            if (!HasWorkLeft(slot)) continue;
            return slot;
        }

        // 4. In the run but already finished. Replaying it should replay the
        //    layout it was played on, not a fresh one.
        for (int slot = 0; slot < _order.Count; slot++)
        {
            if (_order[slot] == target) return slot;
        }

        return -1;
    }

    /// <summary>
    /// Whether a level builds itself from a seed, and so comes up empty without
    /// one. Read from the interface rather than the slot table, because this is
    /// asked about levels that are NOT in the run.
    /// </summary>
    private static bool IsGenerator(LevelManager? manager, int levelIndex)
    {
        try
        {
            var li = manager?.GetLevelInterface(levelIndex);
            return li != null && (li.IsRandomizable || li.IsDailyTidy);
        }
        catch
        {
            return false;
        }
    }

}
