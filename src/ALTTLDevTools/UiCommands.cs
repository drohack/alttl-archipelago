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
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// Driving the game's own UI - menus, buttons, real pointer clicks, the pause
/// screen, the post-level arrow - and the hint notepad and its eraser.
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
    /// cannot see it because it is inactive while closed, calling
    /// ShowHideMenuItems directly throws - the game dereferences the
    /// GameEventData a caller has no way to construct - and PostOpenMenuEvent
    /// is accepted and opens nothing. So this raises the game's own MenuOpen
    /// event, the way solve: raises ObjectControllerSolved.
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
                + $" GetRandomizerHints={Str(() => rnd.GetRandomizerHints() == null ? "null" : rnd.GetRandomizerHints().Count.ToString())}"
                + $" sprites=[{Str(() => SpriteNames(rnd.GetRandomizerHints()))}]");
        }

        DevToolsPlugin.Log.LogInfo(
            $"hints: {Str(() => li.LevelId)}"
            + $" randomizable={Str(() => li.IsRandomizable.ToString())}"
            + $" daily={Str(() => li.IsDailyTidy.ToString())}"
            + $" available={Str(() => li.HintAvailable.ToString())}"
            + $" used={Str(() => li.HintUsed.ToString())}"
            + $" images={images}"
            + $" sprites=[{Str(() => SpriteNames(li.HintImages))}]");

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
                + $" isCleaned={(surface == null ? "-" : Str(() => surface.IsCleaned.ToString()))}"
                // WHICH PICTURE the page draws: the level's own HintImages
                // or its randomizer's generic hints (the Calendar hint bug,
                // 2026-09-25) - compare with the sprites= lists above.
                + $" shows={Str(() => page.HintImage == null || page.HintImage.sprite == null ? "none" : page.HintImage.sprite.name)}");
        }
    }

    /// <summary>The names of a sprite list, comma separated.</summary>
    private static string SpriteNames(Il2CppSystem.Collections.Generic.List<Sprite>? sprites)
    {
        if (sprites == null) return "null";
        var names = new List<string>();
        for (int i = 0; i < sprites.Count; i++) names.Add(sprites[i] == null ? "null" : sprites[i].name);
        return string.Join(", ", names);
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

    /// <summary>
    /// The first LIVE instance of a component, inactive ones included: the
    /// pause menu is inactive while closed, so an ordinary find cannot see
    /// it, and FindObjectsOfTypeAll also returns prefabs, whose buttons do
    /// nothing useful when pressed - it produced a blank screen that looked
    /// like a bug in the thing being tested.
    /// </summary>
    private static T? LiveInScene<T>() where T : Component
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<T>()))
        {
            var found = candidate == null ? null : candidate.TryCast<T>();
            if (found == null || !found.gameObject.scene.IsValid()) continue;
            return found;
        }
        return null;
    }

    /// <summary>
    /// `play`: press Play on the TITLE menu. There is no live TitleMenu
    /// anywhere else, so it needs menu:title first.
    /// </summary>
    private static void PressPlay()
    {
        var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
        if (title == null)
        {
            DevToolsPlugin.Log.LogWarning("play: no live TitleMenu");
            return;
        }
        DevToolsPlugin.Log.LogInfo("play: pressing Play");
        title.PlayGame();
    }

    /// <summary>`pausebuttons`: the pause menu's buttons and which are shown.</summary>
    private static void ListPauseButtons()
    {
        var menu = LiveInScene<MainMenu>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("pausebuttons: no live MainMenu");
            return;
        }
        DevToolsPlugin.Log.LogInfo(
            "pausebuttons: active=" + Str(() => menu.gameObject.activeInHierarchy.ToString()));
        var container = menu.ButtonsContainer;
        if (container == null) { DevToolsPlugin.Log.LogWarning("  no container"); return; }
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            DevToolsPlugin.Log.LogInfo(
                $"  {Str(() => child.name)} active="
                + Str(() => child.gameObject.activeSelf.ToString()));
        }
    }

    /// <summary>`titletree`: the title screen's object tree.</summary>
    private static void DumpTitleTree()
    {
        var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
        if (title == null)
        {
            DevToolsPlugin.Log.LogWarning("titletree: no title screen");
            return;
        }
        DevToolsPlugin.Log.LogInfo("titletree:");
        DumpTitle(title.transform, 0);
    }

    /// <summary>
    /// `uitree:&lt;object name&gt;[:&lt;depth&gt;]`: one UI object's subtree, each
    /// child with its components by type name and its RectTransform (size,
    /// anchored position, pivot, anchors, scale, screen x), then its parents up
    /// to the canvas. The object is found by exact name among scene objects,
    /// an active one first. At most six children per parent are listed.
    ///
    /// Built for the overview strip (2026-09-28): which object holds the
    /// Scrollbar the player drags, and how wide it is next to the dots the mod
    /// scales to fit.
    /// </summary>
    private static void DumpUiTree(string arg)
    {
        var parts = arg.Split(':');
        var name = parts[0].Trim();
        var depth = 3;
        if (parts.Length > 1
            && !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out depth))
        {
            depth = 3;
        }
        if (name.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("uitree: name an object, e.g. uitree:Levels Overview Scrollbar");
            return;
        }

        Transform? root = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<Transform>()))
        {
            var t = obj == null ? null : obj.TryCast<Transform>();
            if (t == null || t.gameObject == null || !t.gameObject.scene.IsValid()) continue;
            if (Str(() => t.name) != name) continue;
            if (root == null || (t.gameObject.activeInHierarchy && !root.gameObject.activeInHierarchy))
            {
                root = t;
            }
        }
        if (root == null)
        {
            DevToolsPlugin.Log.LogWarning($"uitree: no object named '{name}'");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"uitree: {DescribeUi(root)}");
        for (var up = root.parent; up != null; up = up.parent)
        {
            DevToolsPlugin.Log.LogInfo($"uitree:   parent {DescribeUi(up)}");
        }
        DumpUi(root, 0, depth);
    }

    private static void DumpUi(Transform t, int depth, int max)
    {
        if (depth >= max) return;
        var count = t.childCount;
        for (int i = 0; i < count; i++)
        {
            // The first four and the last two: a strip of 138 dots says what
            // it is in six lines.
            if (count > 6 && i == 4)
            {
                DevToolsPlugin.Log.LogInfo(
                    "uitree: " + new string(' ', (depth + 1) * 2) + $"... {count - 6} more");
                i = count - 3;
                continue;
            }
            var child = t.GetChild(i);
            if (child == null) continue;
            DevToolsPlugin.Log.LogInfo(
                "uitree: " + new string(' ', (depth + 1) * 2) + DescribeUi(child));
            DumpUi(child, depth + 1, max);
        }
    }

    private static string DescribeUi(Transform t)
    {
        var kinds = "";
        foreach (var component in t.GetComponents<Component>())
        {
            if (component == null) continue;
            var kind = Str(() => component.GetIl2CppType().Name);
            if (kind == "Transform" || kind == "RectTransform") continue;
            kinds += (kinds.Length == 0 ? " [" : ",") + kind;
        }
        if (kinds.Length > 0) kinds += "]";

        var geometry = "";
        var rect = t.TryCast<RectTransform>();
        if (rect != null)
        {
            var r = rect.rect;
            geometry = string.Format(CultureInfo.InvariantCulture,
                " size={0:0}x{1:0} pos={2:0},{3:0} pivot={4:0.##},{5:0.##} anchors={6:0.##}-{7:0.##} screenx={8:0}",
                r.width, r.height, rect.anchoredPosition.x, rect.anchoredPosition.y,
                rect.pivot.x, rect.pivot.y, rect.anchorMin.x, rect.anchorMax.x, rect.position.x);
        }
        return string.Format(CultureInfo.InvariantCulture, "'{0}' active={1}{2}{3} scale={4:0.###},{5:0.###}",
            Str(() => t.name), Str(() => t.gameObject.activeSelf.ToString()), kinds, geometry,
            t.localScale.x, t.localScale.y);
    }

    /// <summary>`titlebuttons`: the title menu's entries and which are shown.</summary>
    private static void ListTitleButtons()
    {
        var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
        var container = title == null ? null : title.MainMenuContainer;
        if (container == null)
        {
            DevToolsPlugin.Log.LogWarning("titlebuttons: no title screen");
            return;
        }
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            DevToolsPlugin.Log.LogInfo(
                $"  {Str(() => child.name)} active="
                + Str(() => child.gameObject.activeSelf.ToString()));
        }
    }

    /// <summary>`replayselect`: the post-level ReplayMenu's Level Select button.</summary>
    private static void ReplayLevelSelect()
    {
        var menu = LiveInScene<ReplayMenu>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("replayselect: no live ReplayMenu");
            return;
        }
        DevToolsPlugin.Log.LogInfo("replayselect: post-level Level Select");
        menu.LevelSelect();
    }

    /// <summary>`next`: the post-level Continue arrow.</summary>
    private static void PressNext()
    {
        // THE RETRY PANEL, WHEN IT IS THE ONE ON SCREEN. A level whose own
        // menu is the retry panel shows RetryMenu, and pressing the hidden
        // ReplayMenu's arrow moved on while the panel stayed up over the next
        // puzzle (droha, 2026-09-25). The mod's RetryPanel presses RetryMenu
        // for the same reason; a script must take the same route.
        var gm = GameManager.Instance;
        var state = gm == null || gm.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
        if (state == "RetryUI_GameState")
        {
            // A pointer click on its Continue Button, not RetryMenu.NextLevel:
            // that advances but leaves the panel up over the next puzzle
            // (measured 2026-09-25).
            DevToolsPlugin.Log.LogInfo("next: pressing the retry panel's Continue Button");
            PressControl("Continue Button");
            return;
        }

        var menu = LiveInScene<ReplayMenu>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("next: no live ReplayMenu");
            return;
        }
        DevToolsPlugin.Log.LogInfo("next: pressing the arrow");
        menu.NextLevel();
    }

    /// <summary>
    /// `leave`: the pause menu's own Level Select button - the thing a player
    /// actually presses to leave a puzzle. Asking the game where it WOULD go
    /// proved nothing; this takes the route.
    /// </summary>
    private static void LeavePuzzle()
    {
        var menu = LiveInScene<MainMenu>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("leave: no MainMenu in the scene");
            return;
        }
        DevToolsPlugin.Log.LogInfo("leave: pressing Level Select");
        menu.LevelSelect();
    }
}
