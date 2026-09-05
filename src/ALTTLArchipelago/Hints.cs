using System;
using HarmonyLib;
using UnityEngine;
using ALTTLModKit;

namespace ALTTLArchipelago;

/// <summary>
/// The game's hint notepad, gated on holding a Hint Page.
///
/// A puzzle's hint is normally free: open the notepad, scrub the scribble off
/// with the eraser, read it. In a randomizer that is the single largest thing
/// the player can help themselves to, and it was being given away for nothing
/// while three quarters of the item pool did nothing at all. Here a page costs
/// an item.
///
/// The gate deliberately does NOT refuse to open the notepad. A player who
/// holds none can still see that a hint exists and how many pages it runs to -
/// they simply cannot erase it. Knowing that help exists is not the reward;
/// reading it is.
///
/// A LEVEL IS NOT A PAGE. The notepad is an array of HintPage objects, each
/// with its own erasable surface: 74 levels hold one page, 31 hold between two
/// and five. One item opens one page, and page two of a level costs a second.
/// </summary>
internal static class Hints
{
    /// <summary>How many are held and unspent.</summary>
    internal static int Available
        => Math.Max(0, Inventory.HintPagesHeld - RunState.HintPagesOpened);


    /// <summary>
    /// The key a page is remembered under, or null if there is no run or the
    /// page cannot be identified.
    ///
    /// Keyed on the SLOT rather than the level: a generator can be drawn
    /// several times into one run, and each of those instances has its own
    /// notepad that must be paid for separately.
    /// </summary>
    private static string? KeyFor(int pageIndex)
    {
        var slot = Checks.CurrentSlot;
        if (slot < 0 || pageIndex < 0) return null;
        return $"{slot}:{pageIndex}";
    }


    /// <summary>
    /// Refuse to wipe a page nobody has paid for.
    ///
    /// This prefixes the game's own CanBeWiped rather than a button or the
    /// eraser, because CanBeWiped is what the wipe path actually consults -
    /// every route to erasing a surface passes through it. Patching a menu
    /// control instead would leave the drag-eraser working.
    ///
    /// It runs on EVERY cleanable surface, not just hint pages: the same class
    /// backs the cleaning puzzles in the game proper. Anything that is not a
    /// hint page is passed straight through untouched - see the null return
    /// from GetHintPageIndexFromSurface below, which is the discriminator.
    /// </summary>
    [HarmonyPatch(typeof(CleanableSurface), nameof(CleanableSurface.CanBeWiped),
                  MethodType.Getter)]
    [HarmonyPrefix]
    private static bool BeforeCanBeWiped(CleanableSurface __instance, ref bool __result)
    {
        // RE-ENTRANCY GUARD. Kept deliberately, with the scar it came from.
        //
        // A build of this gate asked the surface IsBeingWiped, and that call
        // reaches back into CanBeWiped - so the gate called itself. It does
        // not throw: it recurses until the stack is gone and the process dies
        // instantly, with no managed exception and nothing in any log. It
        // presented as the game simply vanishing the moment the notepad
        // opened.
        //
        // That particular call is gone, but the shape of the hazard is not:
        // anything this prefix asks a CleanableSurface may consult CanBeWiped,
        // and the failure mode is a silent hard exit rather than an error. One
        // bool is a cheap price for never meeting it again.
        if (_inGate) return true;
        _inGate = true;
        try
        {
            if (!Track.Active) return true;          // not a run; vanilla rules
            if (__instance == null) return true;

            // NOT guarded on IsCleaned, despite how much it looks like the
            // right question. Measured: a hint page whose scribble is fully
            // drawn on screen reports IsCleaned == true, so guarding on it
            // waved every page straight through and quietly disabled the whole
            // gate while every log line still looked healthy. The property
            // evidently answers something else - most likely it reads through
            // SurfaceDetails, which is not populated until the surface
            // registers with the manager.
            //
            // Not needing it costs nothing: a page already paid for is caught
            // by the RunState key below, which is durable across sessions
            // rather than merely across a notepad opening.

            var page = PageIndexOf(__instance);
            if (page < 0) return true;               // not a hint page at all

            // Only the page actually on screen. The menu keeps a FIXED POOL of
            // eight HintPage objects and reuses them, so a one-page level
            // still has seven more sitting inactive behind it - measured, not
            // assumed. Without this, anything that touches those surfaces
            // could charge for all eight, and a player holding eight Hint
            // Pages would lose the lot opening a notepad with one page in it.
            if (!IsShowing(page)) return true;

            var key = KeyFor(page);
            if (key == null) return true;            // cannot identify it; allow

            // Already paid for. Re-reading a hint you own is free, forever.
            if (RunState.IsHintPageOpen(key)) return true;

            // LOOKING IS FREE; ERASING COSTS - and the split between those two
            // is why nothing is charged here.
            //
            // This getter is the game's "may this be wiped", and it is asked
            // as soon as the notepad opens, not while the eraser moves. An
            // earlier build charged right here and so billed the player for
            // merely opening the notepad to see whether a hint existed, which
            // is a trap: they could not check and back out. Gating on
            // IsBeingWiped instead looked like the fix and was worse - it is
            // never true at poll time, so the gate silently stopped charging
            // or refusing anything at all and hints became free.
            //
            // So this half only ever says NO, and only when the player has
            // nothing to spend. The charge itself lands in AfterHintTaken
            // below, on the game's own event for a hint actually being read.
            if (Available > 0) return true;

            Refuse();
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            // Fail open, for the same reason Skips does: a player who cannot
            // read a hint they paid for is worse off than one who read a hint
            // they had not earned. This getter is also on the wipe path of
            // ordinary cleaning puzzles, so a throw here must never be able to
            // make a normal puzzle unsolvable.
            Plugin.Logger.LogWarning($"hint: gate failed, allowing: {e.Message}");
            return true;
        }
        finally
        {
            _inGate = false;
        }
    }

    [ThreadStatic]
    private static bool _inGate;


    /// <summary>
    /// Which hint page this surface belongs to, or -1 if it is not one.
    ///
    /// The game already has to answer this - a scrub arrives as a surface and
    /// the menu needs the page index - so GetHintPageIndexFromSurface is asked
    /// rather than the array being walked here.
    /// </summary>
    private static int PageIndexOf(CleanableSurface surface)
    {
        // The == null is Unity's overload, so it is also true for a destroyed
        // object whose managed wrapper still exists - which is what a torn
        // down menu leaves behind.
        if (surface == null || _menu == null) return -1;
        return _menu.GetHintPageIndexFromSurface(surface);
    }


    /// <summary>
    /// Charge for the page the player actually read.
    ///
    /// The game raises this once a hint has genuinely been uncovered - past
    /// HintManager's own hintUsedAtNormal threshold - which makes it the only
    /// honest moment to take payment. Opening the notepad and closing it again
    /// never reaches here, so looking really is free.
    /// </summary>
    [HarmonyPatch(typeof(LevelInterface), nameof(LevelInterface.HintTaken))]
    [HarmonyPostfix]
    private static void AfterHintTaken()
    {
        try
        {
            if (!Track.Active || _menu == null) return;

            var key = KeyFor(_menu.m_currentHintIndex);
            if (key == null) return;
            if (!RunState.OpenHintPage(key)) return;   // already paid for

            Plugin.Logger.LogInfo($"hint: page {key} read, {Available} left");
            Toasts.Show($"Hint Page used - {Available} left", Toasts.Notice);

            // The page the player is looking at has just changed state, so the
            // note on it is now wrong.
            RefreshNote();
        }
        catch (Exception e)
        {
            // Never let bookkeeping break reading a hint the player earned.
            Plugin.Logger.LogWarning($"hint: could not record the page: {e.Message}");
        }
    }


    /// <summary>
    /// Is this the page the notepad is currently turned to?
    /// </summary>
    private static bool IsShowing(int page)
        => _menu != null && _menu.m_currentHintIndex == page;


    /// <summary>
    /// Take a reference to the notepad as the game builds it.
    ///
    /// Deliberately NOT a FindObjectOfType inside the gate. CanBeWiped is
    /// polled while the eraser is dragged, and polled for every cleanable
    /// surface in the scene - the ordinary cleaning puzzles use the same
    /// class - so a scene-wide type search in there would run several times a
    /// frame during the one interaction the player is actively performing.
    ///
    /// Caching the search result is not enough either, and the reason is worth
    /// keeping: while a normal cleaning puzzle is being scrubbed there is
    /// often no hint menu to find, so the cache never fills and every poll
    /// pays for a failing search. Letting the menu hand itself over on
    /// creation costs one assignment and can never miss.
    /// </summary>
    [HarmonyPatch(typeof(HintMenu), nameof(HintMenu.Init))]
    [HarmonyPostfix]
    private static void AfterHintMenuInit(HintMenu __instance)
    {
        _menu = __instance;
        RefreshNote();
    }

    private static HintMenu? _menu;


    /// <summary>
    /// Turning the page changes what the note should say.
    ///
    /// Page two can be unpaid while page one is paid for, so a note written
    /// only when the notepad opens would be wrong the moment the player turns
    /// a page - and it would be wrong in the expensive direction, telling
    /// someone a page is free when scrubbing it will cost them.
    /// </summary>
    [HarmonyPatch(typeof(HintMenu), nameof(HintMenu.SwitchToHint))]
    [HarmonyPostfix]
    private static void AfterSwitchToHint() => RefreshNote();


    /// <summary>
    /// Say, on the page itself, what rubbing it out will cost.
    ///
    /// This is the notepad, not the pause menu, and it deliberately does not
    /// use Navigation.Annotate's trick of prefixing an existing label - there
    /// is no label here to prefix, and there is room under the page for one of
    /// our own. The menu's own scar (ConnectionPane.cs:713-726) is about
    /// right-aligned entries in an over-wide rect, which does not apply.
    /// </summary>
    private static void RefreshNote()
    {
        try
        {
            var label = NoteLabel();
            if (label == null) return;

            label.text = NoteText();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"hint: could not write the note: {e.Message}");
        }
    }


    /// <summary>What the note should say for the page now showing.</summary>
    private static string NoteText()
    {
        if (!Track.Active) return "";

        var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        var pages = li?.HintImages == null ? 0 : li.HintImages.Count;
        if (pages == 0) return "This puzzle has no hint";

        var key = _menu == null ? null : KeyFor(_menu.m_currentHintIndex);
        if (key == null) return "";

        // The state the player could not previously see at all: coming back to
        // a hint you have already bought, with no way to tell you will not be
        // charged for it twice.
        if (RunState.IsHintPageOpen(key))
            return "Already uncovered - reading this again is free";

        var held = Available;
        if (held <= 0) return "No Hint Pages - find one to uncover this hint";

        return $"Rubbing this out uses a Hint Page - you have {held}";
    }


    /// <summary>
    /// The label, made once and parented to the notepad so it moves with it.
    /// </summary>
    private static TMPro.TextMeshProUGUI? NoteLabel()
    {
        if (_note != null) return _note;
        if (_menu == null || _menu.notepad == null) return null;

        var go = new GameObject("ArchipelagoHintNote");
        go.transform.SetParent(_menu.notepad, false);

        var rect = go.AddComponent<RectTransform>();
        // Pinned under the page, spanning its width: anchored to the bottom
        // edge and pushed clear of it, so a page of any size keeps the note in
        // the same place relative to the paper.
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -12f);
        rect.sizeDelta = new Vector2(0f, 48f);

        var text = go.AddComponent<TMPro.TextMeshProUGUI>();
        text.alignment = TMPro.TextAlignmentOptions.Top;
        text.enableWordWrapping = true;
        text.fontSize = 22f;
        text.color = new Color(1f, 1f, 1f, 0.75f);
        text.raycastTarget = false;   // never steal a drag from the eraser

        _note = text;
        return _note;
    }

    private static TMPro.TextMeshProUGUI? _note;


    /// <summary>
    /// Say no, but only once per notepad opening.
    ///
    /// CanBeWiped is polled while the eraser is dragged, so a toast per call
    /// would be a wall of them. The latch clears whenever a level starts.
    /// </summary>
    private static void Refuse()
    {
        if (_refused) return;
        _refused = true;
        Plugin.Logger.LogInfo("hint: refused, none held");
        Toasts.Show("No Hint Page available - find one to uncover this hint",
                    Toasts.Notice);
    }

    private static bool _refused;

    /// <summary>
    /// Re-arm the refusal toast, and drop the cached menu. Called when a level
    /// starts.
    /// </summary>
    internal static void LevelStarted()
    {
        _refused = false;

        // The label belongs to the notepad, which is rebuilt with the level.
        // Dropping the reference lets it be made again against the new one -
        // Unity's == null is true for a destroyed object, so a stale wrapper
        // would otherwise be handed back forever and the note would silently
        // stop updating.
        _note = null;
    }
}
