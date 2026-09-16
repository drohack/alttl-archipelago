using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ALTTLModKit;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Driving the game's own UI - menus, buttons, real pointer clicks, the pause
/// screen, the hint notepad and the eraser.
///
/// press: and clickbutton: are NOT the same thing and the difference has bitten:
/// clickbutton invokes Button.onClick, which the level-select tutorial's confirm
/// does not use, so four "successful" clicks left the modal where it was.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// Whether the game is already in this state, so the transition is a no-op.
    ///
    /// Forcing a state the game is already in is not harmless. SetGameState
    /// closes the active menu on the way, so "menu:levels" while already in
    /// Levels_GameState leaves NOTHING open - and the next menu: command then
    /// calls CloseActiveMenu with no active menu, where the game's own
    /// TransitionMenuOut dereferences null. Measured: 7 failures in 7 attempts,
    /// every one preceded by the game logging
    /// "SetGameState: Levels_GameState already active".
    /// </summary>
    private static bool AlreadyIn(GameManager gm, string stateTypeName)
    {
        try
        {
            var current = gm.GameState == null
                ? null : gm.GameState.GetIl2CppType().Name;
            if (current != stateTypeName) return false;

            DevToolsPlugin.Log.LogInfo($"menu: already in {stateTypeName}, nothing to do");
            return true;
        }
        catch
        {
            return false;      // on doubt, do what was asked
        }
    }

    private static void GoToMenu(string arg)
    {
        var gm = GameManager.Instance;
        var parts = arg.Split(':');
        switch (parts[0].ToLowerInvariant())
        {
            case "title":
                if (AlreadyIn(gm, "Title_GameState")) return;
                gm.SetGameState<Title_GameState>(null, false);
                break;
            case "levels":
                if (AlreadyIn(gm, "Levels_GameState")) return;
                // Levels_GameState builds its own LevelsTrack_MenuData; the
                // state data slot only carries a transition delay.
                gm.SetGameState<Levels_GameState>(null, false);
                break;
            case "archive":
                if (AlreadyIn(gm, "Archive_GameState")) return;
                gm.SetGameState<Archive_GameState>(null, false);
                break;
            case "daily":
                if (AlreadyIn(gm, "DailyTidy_GameState")) return;
                gm.SetGameState<DailyTidy_GameState>(null, false);
                break;
            default:
                DevToolsPlugin.Log.LogWarning($"unknown menu: {arg}");
                return;
        }
        DevToolsPlugin.Log.LogInfo($"menu: {arg}");
    }

    /// <summary>
    /// "clickbutton:Name" invokes the onClick of the first Button whose
    /// GameObject is called Name. There is no synthetic mouse input here, so
    /// UI added by another plugin can be exercised from a script - which is
    /// the only way to test the randomizer's own connection pane.
    /// </summary>
    private static void ClickButton(string name)
    {
        foreach (var button in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = button == null ? null : button.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            // Own name OR the parent's: a composite control such as the
            // modal's confirm keeps its Button on a child, so matching only
            // the Button's own GameObject missed it.
            var parentName = b.transform.parent?.gameObject.name ?? "";
            if (!string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parentName, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (Button on {b.gameObject.name})");
            b.onClick.Invoke();
            return;
        }

        // Also the game's own long-press control, which is a UIBehaviour and
        // NOT a Button - the modal's confirm is one, so a Button-only search
        // reported "no active button" for a control plainly on screen.
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UILongPressButton>()))
        {
            var lp = obj == null ? null : obj.TryCast<UILongPressButton>();
            if (lp == null || lp.gameObject == null) continue;
            if (!string.Equals(lp.gameObject.name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!lp.gameObject.activeInHierarchy) continue;

            // The inner Button first - that is where a listener is normally
            // attached - and only then the long-press event.
            var inner = lp.Button;
            if (inner != null)
            {
                DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (inner Button)");
                inner.onClick.Invoke();
                return;
            }
            DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (long press)");
            lp.m_btnAction?.Invoke();
            return;
        }

        DevToolsPlugin.Log.LogWarning($"clickbutton: no active control named {name}");
    }

    /// <summary>
    /// "contextual" asks the running gameplay state where it would return to.
    ///
    /// The screen you land on after a puzzle is whatever ContextualState says,
    /// and it cannot be observed by completing a level from a script - a player
    /// clicks through the completion screen to get there. Asking the question
    /// directly is the only way to check the answer without a mouse.
    /// </summary>
    private static void ReportContextualState()
    {
        var state = GameManager.Instance.GameState;
        if (state == null)
        {
            DevToolsPlugin.Log.LogWarning("contextual: no game state");
            return;
        }

        var gameplay = state.TryCast<Gameplay_GameState>();
        if (gameplay == null)
        {
            DevToolsPlugin.Log.LogInfo(
                $"contextual: not in a level (state is {Str(() => state.GetIl2CppType().Name)})");
            return;
        }

        var back = gameplay.ContextualState();
        DevToolsPlugin.Log.LogInfo(
            "contextual: finishing here would return to "
            + Str(() => back == null ? "<null>" : back.GetIl2CppType().Name));
    }

    /// <summary>
    /// "menus" lists every menu the game knows about and its state.
    ///
    /// The question this answers is whether each level type gets a different
    /// pause menu, or the same one behaving differently - which decides whether
    /// a mod has to take over one menu or several.
    /// </summary>
    private static void DumpMenus()
    {
        var gm = GameManager.Instance;
        var mm = gm == null ? null : gm.menuManager;
        if (mm == null)
        {
            DevToolsPlugin.Log.LogWarning("menus: no MenuManager");
            return;
        }

        var li = gm!.levelManager == null ? null : gm.levelManager.ActiveLevelInterface;
        DevToolsPlugin.Log.LogInfo(
            "menus: in " + Str(() => li == null ? "<no level>" : li.LevelId)
            + " archived=" + Str(() => li == null ? "?" : li.IsArchived.ToString())
            + " type=" + Str(() => li == null ? "?" : li.LevelType.ToString())
            + " state=" + Str(() => gm.GameState == null
                ? "?" : gm.GameState.GetIl2CppType().Name));

        var menus = mm.Menus;
        DevToolsPlugin.Log.LogInfo($"menus: {(menus == null ? 0 : menus.Count)} registered");
        for (int i = 0; i < (menus == null ? 0 : menus.Count); i++)
        {
            var menu = menus![i];
            if (menu == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"  [{i}] {Str(() => menu.GetIl2CppType().Name)}"
                + $" object={Str(() => menu.gameObject.name)}"
                + $" active={Str(() => menu.gameObject.activeInHierarchy.ToString())}"
                + $" alpha={Str(() => menu.canvas == null ? "?" : menu.canvas.alpha.ToString("0.0"))}");
        }
    }

    /// <summary>
    /// The title screen's whole tree, so nothing on it is decided by guesswork.
    /// Depth-limited: the interesting things are entries and their badges, not
    /// the text objects inside them.
    /// </summary>
    private static void DumpTitle(Transform t, int depth)
    {
        if (depth > 3) return;

        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;

            // Component type names rather than typed lookups: the dev tools
            // deliberately reference as little of Unity's UI as possible.
            var kinds = "";
            foreach (var component in child.GetComponents<Component>())
            {
                if (component == null) continue;
                var name = Str(() => component.GetIl2CppType().Name);
                if (name == "Button" || name == "TextMeshProUGUI") kinds += " " + name;
            }

            DevToolsPlugin.Log.LogInfo(
                new string(' ', (depth + 1) * 2)
                + Str(() => child.name)
                + " active=" + Str(() => child.gameObject.activeSelf.ToString())
                + kinds);

            DumpTitle(child, depth + 1);
        }
    }

    /// <summary>
    /// Click a control the way a pointer would, not by invoking onClick.
    ///
    /// clickbutton invokes Button.onClick and returns as soon as it finds a
    /// Button. That is not the same as clicking: the level-select tutorial's
    /// confirm reads "Okay", IS a Button, and has nothing attached to onClick -
    /// invoking it four times left the modal on page 1 of 3 while the log
    /// cheerfully reported four successful clicks. The behaviour lives on a
    /// pointer handler instead.
    ///
    /// So this dispatches a real pointer-click through the EventSystem, which
    /// is what every IPointerClickHandler in the game is actually listening
    /// for, and reports how many handlers received it - zero being the answer
    /// that matters, since that is the case clickbutton reported as success.
    /// </summary>
    private static void PressControl(string name)
    {
        name = name.Trim();
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = obj == null ? null : obj.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            var parent = "";
            try { parent = b.transform.parent == null ? "" : b.transform.parent.gameObject.name; }
            catch { }
            if (!string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parent, name, StringComparison.OrdinalIgnoreCase)) continue;

            var data = new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current);
            data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;

            UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            var got = UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

            DevToolsPlugin.Log.LogInfo(
                $"press: {name} - pointer click {(got ? "handled" : "NOT handled")}");
            return;
        }
        DevToolsPlugin.Log.LogWarning($"press: no active control named {name}");
    }

    /// <summary>
    /// Open the in-level pause menu, the way the game does.
    ///
    /// Needed because a scripted run cannot press Escape, and every cheaper
    /// route was wrong: the menu has no Show/Open/Toggle, FindObjectOfType
    /// cannot see it because it is inactive while closed, and calling
    /// ShowHideMenuItems directly throws - the game dereferences the
    /// GameEventData a caller has no way to construct. PostOpenMenuEvent is
    /// what the game itself posts.
    ///
    /// This is what unblocks testing anything WITH the pause menu open, which
    /// until now could only be described rather than checked.
    /// </summary>
    private static void OpenPauseMenu()
    {
        var gm = GameManager.Instance;
        var mm = gm == null ? null : gm.menuManager;
        if (mm == null)
        {
            DevToolsPlugin.Log.LogWarning("pause: no menu manager");
            return;
        }

        // Inactive objects included: closed is exactly the state it is in.
        MainMenu? menu = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
        {
            menu = obj == null ? null : obj.TryCast<MainMenu>();
            if (menu != null) break;
        }

        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning(
                "pause: no MainMenu - it exists only while a level is running");
            return;
        }

        // Raise the game's own MenuOpen event, the same way solve: raises
        // ObjectControllerSolved.
        //
        // PostOpenMenuEvent was tried first, with null MenuData and then with a
        // real one. Both were accepted silently and opened nothing - the same
        // "reported success, did nothing" shape as everything else in this
        // harness, which is why the buttons dump is checked afterwards rather
        // than the call's return.
        var data = new GameEventManager.GameEventData
        {
            Menu = menu,
            LevelInterface = GameManager.Instance.levelManager.ActiveLevelInterface,
        };
        DevToolsPlugin.Log.LogInfo("pause: raising MenuOpen");
        GameEventManager.AddGameEvent<GameEventManager.GameEvent_MenuOpen>(data);
    }

    /// <summary>
    /// Does the running level actually have a hint?
    ///
    /// The question is whether a Hint item is worth minting: hints are authored
    /// per level as IMAGES, so a procedurally generated layout may have none,
    /// or may have one drawn for a different arrangement. Reading the values
    /// beats reasoning about it.
    /// </summary>
    private static void ReportHints()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("hints: no level running");
            return;
        }

        var images = -1;
        try { images = li.HintImages == null ? 0 : li.HintImages.Count; } catch { }

        // The OTHER source of hints, and the reason a level can report zero
        // images and still show a scribble: LevelRandomizer carries its own
        // List<Sprite> RandomizerHints plus a virtual GetRandomizerHints(),
        // which Books and Pencils override. LevelInterface.HintImages is what
        // the level sweep reads, so anything living only here is invisible to
        // the generator's page count.
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelRandomizer>()))
        {
            var rnd = obj == null ? null : obj.TryCast<LevelRandomizer>();
            if (rnd == null || rnd.gameObject == null) continue;
            if (!rnd.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo(
                $"hints: randomizer {rnd.GetIl2CppType().Name}"
                + $" RandomizerHints={Str(() => rnd.RandomizerHints == null ? "null" : rnd.RandomizerHints.Count.ToString())}"
                + $" GetRandomizerHints={Str(() => rnd.GetRandomizerHints() == null ? "null" : rnd.GetRandomizerHints().Count.ToString())}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"hints: {Str(() => li.LevelId)}"
            + $" randomizable={Str(() => li.IsRandomizable.ToString())}"
            + $" daily={Str(() => li.IsDailyTidy.ToString())}"
            + $" available={Str(() => li.HintAvailable.ToString())}"
            + $" used={Str(() => li.HintUsed.ToString())}"
            + $" images={images}");

        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<HintMenu>()))
        {
            var menu = obj == null ? null : obj.TryCast<HintMenu>();
            if (menu == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"hints: menu NumActiveHints={Str(() => menu.NumActiveHints.ToString())}"
                + $" maxIndex={Str(() => menu.m_maxHintIndex.ToString())}"
                + $" pages={Str(() => menu.HintPages == null ? "null" : menu.HintPages.Length.ToString())}"
                + $" isDaily={Str(() => menu.m_isDailyTidyHint.ToString())}");

            var mgr = menu.m_activeHintManager;
            DevToolsPlugin.Log.LogInfo(
                $"hints: manager={(mgr == null ? "null" : "yes")}"
                + (mgr == null ? "" :
                   $" usedAt={Str(() => mgr.hintUsedAtNormal.ToString())}"
                   + $" fullAt={Str(() => mgr.hintFullyCleanedAtNormal.ToString())}"
                   + $" taken={Str(() => mgr.m_hintsTakenIndexes.Count.ToString())}"));

            // Read CanBeWiped per page.
            //
            // This is not just reporting - it is the only way to exercise the
            // randomizer's hint gate from a probe, because the gate is a
            // prefix on this exact property getter. Reading it here goes
            // through the same call the eraser makes, so a page that reports
            // true has genuinely been paid for and a page that reports false
            // has genuinely been refused. Reading it can therefore SPEND a
            // Hint Page, which is intended: that is what makes it a test of
            // the gate rather than a description of it.
            ReportHintPages(menu);
            break;
        }
    }

    /// <summary>
    /// Each hint page, and whether the game would currently let it be wiped.
    /// </summary>
    private static void ReportHintPages(HintMenu menu)
    {
        var pages = menu.HintPages;
        if (pages == null)
        {
            DevToolsPlugin.Log.LogInfo("hints: no page array");
            return;
        }

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (page == null)
            {
                DevToolsPlugin.Log.LogInfo($"hints:   page {i}: null");
                continue;
            }

            var surface = page.CleanableSurface;
            DevToolsPlugin.Log.LogInfo(
                $"hints:   page {i}"
                + $" active={Str(() => page.gameObject.activeSelf.ToString())}"
                + $" surface={(surface == null ? "null" : "yes")}"
                + $" canBeWiped={(surface == null ? "-" : Str(() => surface.CanBeWiped.ToString()))}"
                + $" isCleaned={(surface == null ? "-" : Str(() => surface.IsCleaned.ToString()))}");
        }
    }

    /// <summary>
    /// Drag the notepad's eraser across the current hint page, for real.
    ///
    /// Why this exists rather than a cheaper probe: the randomizer's hint gate
    /// hangs off CleanableSurface.CanBeWiped, and the ONE question that cannot
    /// be answered by reading state is whether the game ever consults that
    /// property while a wipe is actually in progress. Reading CanBeWiped from
    /// a probe answers a different question - it exercises the getter with no
    /// wipe underway, which is exactly the case the gate is meant to ignore.
    ///
    /// So this drives the eraser through the UI drag handlers the player's
    /// mouse would, and lets the game do the rest.
    /// </summary>
    private static void DragTheEraser()
    {
        HintMenu? menu = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<HintMenu>()))
        {
            menu = obj == null ? null : obj.TryCast<HintMenu>();
            if (menu != null) break;
        }
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("erase: no HintMenu");
            return;
        }

        var eraser = menu.Eraser;
        if (eraser == null || eraser.gameObject == null)
        {
            DevToolsPlugin.Log.LogWarning("erase: no Eraser on the menu");
            return;
        }

        var index = menu.m_currentHintIndex;
        var pages = menu.HintPages;
        if (pages == null || index < 0 || index >= pages.Length)
        {
            DevToolsPlugin.Log.LogWarning($"erase: no page at index {index}");
            return;
        }

        var page = pages[index];
        if (page == null)
        {
            DevToolsPlugin.Log.LogWarning($"erase: page {index} is null");
            return;
        }

        // Sweep across the page in screen space. The camera is null for an
        // overlay canvas, which WorldToScreenPoint handles.
        var cam = Camera.main;
        var centre = RectTransformUtility.WorldToScreenPoint(
            cam, page.transform.position);

        DevToolsPlugin.Log.LogInfo(
            $"erase: dragging over page {index} at ({centre.x:F0},{centre.y:F0})");

        var data = new UnityEngine.EventSystems.PointerEventData(
            UnityEngine.EventSystems.EventSystem.current);
        data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        data.position = centre;
        data.pressPosition = centre;

        var go = eraser.gameObject;
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);

        for (int step = -6; step <= 6; step++)
        {
            var at = new Vector2(centre.x + step * 40f, centre.y + step * 12f);
            data.delta = new Vector2(40f, 12f);
            data.position = at;
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                go, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
        }

        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);

        var surface = page.CleanableSurface;
        DevToolsPlugin.Log.LogInfo(
            $"erase: done, page {index}"
            + $" isCleaned={(surface == null ? "-" : Str(() => surface.IsCleaned.ToString()))}"
            + $" beingWiped={(surface == null ? "-" : Str(() => surface.IsBeingWiped().ToString()))}");
    }

    /// <summary>
    /// "clickat" or "clickat:X,Y" - click wherever the player would click.
    ///
    /// THE LAST STEP OF droha's REPORT, and the one nothing here could do.
    /// "It reset ... and when I clicked anywhere the game fully froze." The
    /// harness reproduces the state before that - a trap resetting inside a
    /// navigation leaves two levels alive at once, which droha saw on screen
    /// as "oh god 2 levels loaded at once" - but it had no way to take the
    /// final action, so every run ended with the game wounded and still
    /// ticking.
    ///
    /// Deliberately NOT aimed at a named control, unlike press:. The report
    /// says ANYWHERE, and with two levels stacked the interesting part is
    /// precisely which of the two the raycast finds and what is still
    /// listening on the one that should have been torn down.
    ///
    /// Screen coordinates, origin bottom-left, defaulting to the middle of
    /// the window.
    /// </summary>
    private static void ClickAt(string arg)
    {
        var at = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var parts = arg.Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var y))
        {
            at = new Vector2(x, y);
        }

        var system = UnityEngine.EventSystems.EventSystem.current;
        if (system == null)
        {
            DevToolsPlugin.Log.LogWarning("clickat: no EventSystem");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(system);
        data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        data.position = at;
        data.pressPosition = at;

        var hits = new Il2CppSystem.Collections.Generic.List<
            UnityEngine.EventSystems.RaycastResult>();
        system.RaycastAll(data, hits);

        if (hits.Count == 0)
        {
            DevToolsPlugin.Log.LogInfo(
                $"clickat: ({at.x:F0},{at.y:F0}) hit nothing at all");
            return;
        }

        var target = hits[0].gameObject;
        data.pointerCurrentRaycast = hits[0];
        data.pointerPressRaycast = hits[0];

        DevToolsPlugin.Log.LogInfo(
            $"clickat: ({at.x:F0},{at.y:F0}) over {hits.Count} object(s), "
            + $"topmost '{(target == null ? "null" : target.name)}'");

        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

        DevToolsPlugin.Log.LogInfo("clickat: dispatched");
    }

    /// <summary>
    /// Pick pieces up and drop them, for real, one after another.
    ///
    /// EXISTS BECAUSE A BUG NEEDED QUARTER-SECOND TIMING TO REPRODUCE.
    /// Dropping a piece starts a LeanTween settle animation, and a cat trap
    /// landing while one is running used to leave a dead callback throwing
    /// every frame. Asking a human to spring a trap inside that window is not
    /// a test; droha, reasonably: "how do I time that? It needs to be timed
    /// to like the quarter second."
    ///
    /// So this drops piece after piece with a short gap, which keeps SOMETHING
    /// settling for as long as it runs. A trap sent any time during that lands
    /// mid-animation without anyone having to aim.
    ///
    /// A REAL POINTER DRAG, not a flag flip. docs/release-testing.md records
    /// that every short reproducer written for this project used DevTools'
    /// `complete` instead of solving, and all of them came back clean while
    /// the bug reproduced in the full run. The settle tween only exists if a
    /// piece is actually dragged and dropped, so this dispatches the same
    /// pointer sequence the game gets from a mouse.
    ///
    ///     jiggle          every piece in the level, once
    ///     jiggle:5        the first five
    /// </summary>
    private static void JigglePieces(string arg)
    {
        var want = int.MaxValue;
        if (!string.IsNullOrEmpty(arg)
            && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var parsed))
        {
            want = parsed;
        }

        var pieces = UnityEngine.Object.FindObjectsOfType<DragObject>();
        if (pieces == null || pieces.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("jiggle: no DragObject in the scene");
            return;
        }

        // ObjectPlaced is what starts the settle animation, and it is reached
        // by REFLECTION rather than a synthetic drag.
        //
        // The first version dispatched pointerDown/beginDrag/drag/endDrag
        // through the EventSystem and started no tween at all - 24 drags,
        // zero detached tweens - because DragObject has no OnDrag at all: the
        // interop shows OnPointerDown, OnBeginDrag and OnEndDrag but no drag
        // handler, so the sequence never amounted to a placement. Calling the
        // method the crash names is both simpler and exactly on target.
        // Snap(), not ObjectPlaced(GameEventData). The crash lives in a
        // closure inside ObjectPlaced, but that overload wants a game event
        // we have no honest way to synthesise - and Snap is what actually
        // runs the settle: the type carries snapMoveTween, snapEase and
        // m_snapTweenID right beside it.
        System.Reflection.MethodInfo? placed = null;
        foreach (var m in typeof(DragObject).GetMethods(
                     System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Instance))
        {
            if (m.Name != "Snap") continue;
            if (m.GetParameters().Length != 0) continue;
            placed = m;
            break;
        }

        if (placed == null)
        {
            // Say what IS there. "No zero-argument ObjectPlaced" is true and
            // useless; the overload list is what picks the next move.
            DevToolsPlugin.Log.LogWarning(
                "jiggle: no zero-argument ObjectPlaced on DragObject - "
                + "candidates follow");
            foreach (var m in typeof(DragObject).GetMethods(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.Instance))
            {
                var n = m.Name;
                if (n.IndexOf("Place", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Snap", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drag", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                var ps = m.GetParameters();
                var sig = new System.Text.StringBuilder(n).Append('(');
                for (int j = 0; j < ps.Length; j++)
                {
                    if (j > 0) sig.Append(", ");
                    sig.Append(ps[j].ParameterType.Name);
                }
                DevToolsPlugin.Log.LogInfo($"jiggle:   {sig.Append(')')}");
            }
            return;
        }

        var moved = 0;
        var failed = 0;
        for (int i = 0; i < pieces.Length && moved < want; i++)
        {
            var piece = pieces[i];
            if (piece == null || piece.gameObject == null) continue;
            if (!piece.gameObject.activeInHierarchy) continue;

            try
            {
                placed.Invoke(piece, null);
                moved++;
            }
            catch (Exception e)
            {
                if (failed++ == 0)
                {
                    DevToolsPlugin.Log.LogWarning(
                        $"jiggle: ObjectPlaced threw: {e.Message}");
                }
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"jiggle: placed {moved} of {pieces.Length} piece(s), {failed} "
            + "threw; anything settling now is what a trap has to survive");
    }

    /// <summary>
    /// Force the level-select skip prompt on screen, and say what it reads.
    ///
    /// Written to settle a question that a hierarchy scan could not: a label
    /// at Menus/Level Select/Levels Track/Skip Tooltip reads "Skipppable" in
    /// the object tree, but it has a localiser and had never been activated,
    /// so that string may be nothing more than the placeholder baked into the
    /// prefab. What a player actually sees is only knowable by showing it.
    /// </summary>
    private static void ShowSkipTooltip()
    {
        var found = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var track = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (track == null || track.gameObject == null) continue;
            if (!track.gameObject.activeInHierarchy) continue;

            var tip = track.skipTooltip;
            if (tip == null)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"skiptip: {PathOf(track.transform)} has no skipTooltip");
                continue;
            }

            found++;
            DevToolsPlugin.Log.LogInfo(
                $"skiptip: {PathOf(tip.transform)}"
                + $" showing={Str(() => tip.Showing.ToString())}"
                + $" expire={Str(() => tip.skipExpireTime.ToString())}"
                + $" before={Str(() => tip.skipText.text)}");

            tip.Show(true);
            tip.StartSkipTooltip();

            DevToolsPlugin.Log.LogInfo(
                $"skiptip: shown, now reads {Str(() => tip.skipText.text)}"
                + $" live={tip.gameObject.activeInHierarchy}");
        }

        if (found == 0)
        {
            DevToolsPlugin.Log.LogWarning(
                "skiptip: no active LevelsTrack - open the level select first");
        }
    }

    /// <summary>
    /// Fire the game's hint-taken path directly.
    ///
    /// The randomizer charges a Hint Page in a postfix on
    /// LevelInterface.HintTaken, and that postfix has never been observed
    /// firing: a synthetic pointer drag reaches the eraser's drag handlers but
    /// never puts the surface into a wiping state, so the game never gets as
    /// far as raising the event. Calling the method exercises the postfix end
    /// to end, which leaves only "does the game call it when you scrub" - and
    /// HintsTakenCounter.CheckHintTaken subscribes to the same event to keep a
    /// Steam stat, so it demonstrably does.
    /// </summary>
    private static void RaiseHintTaken()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("hinttaken: no level running");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"hinttaken: calling HintTaken on {Str(() => li.LevelId)}");
        li.HintTaken(new GameEventManager.Level_GameEvent.EventData(li, ""));
        DevToolsPlugin.Log.LogInfo("hinttaken: returned");
    }

    /// <summary>
    /// Every clickable control currently on screen, with its parent.
    ///
    /// Added after guessing control names twice and being wrong twice - the
    /// tutorial modal's confirm reads "Okay" on screen and is not named Okay,
    /// and the pause menu could not be found at all because it is inactive
    /// while closed. A scripted run has to dismiss whatever a player would
    /// dismiss, and it cannot do that by guessing what the artist called it.
    ///
    /// Parent as well as name because clickbutton matches either, and the
    /// confirm on a modal keeps its Button on a child.
    /// </summary>
    private static void ListButtons()
    {
        int n = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = obj == null ? null : obj.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            var parent = "";
            try { parent = b.transform.parent == null ? "(root)" : b.transform.parent.gameObject.name; }
            catch { }

            string label = "";
            try
            {
                var t = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (t != null) label = t.text;
            }
            catch { }

            DevToolsPlugin.Log.LogInfo(
                $"  button '{Str(() => b.gameObject.name)}' parent='{parent}' text='{label}'");
            n++;
        }
        DevToolsPlugin.Log.LogInfo($"buttons: {n} active");
    }
}
