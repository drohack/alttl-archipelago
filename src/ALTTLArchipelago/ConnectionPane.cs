using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ALTTLModKit;

namespace ALTTLArchipelago;

/// <summary>
/// The Archipelago entry on the main menu, and the connection dialog behind it.
///
/// Built on the game's OWN modal rather than a bespoke canvas. UIModalContent
/// takes an arbitrary `contentGameobject` for its body plus confirm and decline
/// events, so the chrome, styling, buttons and animation all come for free and
/// the only thing we build is three text fields.
///
/// TWO THINGS HERE ARE UNPROVEN, and both are called out because they are the
/// likely failure points:
///
/// 1. The game contains no text input of its own - nothing in its UI uses
///    TMP_InputField. So there is no in-game example to copy and no guarantee
///    the game's setup cooperates with one.
/// 2. Input goes through Rewired, not Unity's input manager. TMP_InputField
///    reads the IMGUI event queue in OnUpdateSelected, which should be
///    independent of that, but Rewired may also act on the same keystrokes.
///    UIModal.SetCurrentMenuActive(false) is expected to deal with it by
///    disabling the menu behind the modal.
///
/// If typed entry cannot be made to work, the fallback is this same pane
/// showing the settings read-only with Connect/Disconnect buttons and leaving
/// editing to the config file.
/// </summary>
internal static class ConnectionPane
{
    private const string ButtonName = "ApConnectButton";

    /// <summary>The modal's panel is cream, so its text is near-black.</summary>
    private static readonly Color Ink = new Color(0.16f, 0.20f, 0.26f);

    private static TMP_InputField? _address;
    private static TMP_InputField? _slot;
    private static TMP_InputField? _password;
    private static TextMeshProUGUI? _status;
    private static GameObject? _confirm;
    private static GameObject? _pillOff;
    private static GameObject? _pillOn;

    /// <summary>Shown beside the main-menu entry so the state is visible
    /// without opening anything.</summary>
    private static TextMeshProUGUI? _menuIndicator;

    /// <summary>
    /// Three states, because there are three situations.
    ///
    /// This was `IsConnected ? "Disconnect" : "Connect"`, which named only two
    /// of them: while an attempt was in flight or a retry was counting down it
    /// read "Connect", and pressing it did nothing. There was no way to stop a
    /// pending retry from the pane at all - the only escape was quitting the
    /// game.
    /// </summary>
    private static string ConfirmLabel()
    {
        if (Plugin.IsConnected) return "Disconnect";
        return Plugin.IsBusy ? "Cancel" : "Connect";
    }

    /// <summary>
    /// Add our button to the title screen. Postfix on SetupTitleScreen because
    /// that is where the game builds the menu; adding earlier would be undone.
    /// </summary>
    [HarmonyPatch(typeof(TitleMenu), nameof(TitleMenu.SetupTitleScreen))]
    [HarmonyPostfix]
    private static void AddMenuButton(TitleMenu __instance)
    {
        try
        {
            var container = __instance.MainMenuContainer;
            if (container == null) return;
            if (container.Find(ButtonName) != null) return;   // already added

            // Clone an existing entry rather than building one: it inherits
            // the font, the hover behaviour, the layout element and the exact
            // look of every other button, none of which we want to reproduce.
            var template = FindTemplateButton(container);
            if (template == null)
            {
                Plugin.Logger.LogWarning("no menu button to clone; pane unavailable");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, container);
            clone.name = ButtonName;
            // Directly after Settings, so it sits with the other options and
            // above Quit. The first attempt cloned "the last button", which is
            // Quit, and landed the new entry on top of it.
            clone.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);

            SetLabel(clone, "Archipelago");
            AddMenuIndicator(clone);

            var button = clone.GetComponent<Button>();
            if (button != null)
            {
                // REPLACING the event object, not calling RemoveAllListeners().
                // RemoveAllListeners only drops listeners added at runtime; the
                // PERSISTENT ones serialised in the scene survive it. The first
                // attempt did exactly that and the Archipelago button opened
                // Settings as well as the pane.
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(new Action(Open));
            }

            Plugin.Logger.LogInfo("added the Archipelago button to the main menu");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"could not add the menu button: {e}");
        }
    }

    /// <summary>
    /// The Settings button, cloned because our entry belongs beside it rather
    /// than beside Quit. Measured menu order is Play, Levels, Shuffle
    /// (inactive), Daily Tidy, Archive, Settings, Quit.
    ///
    /// Matched by name, with the last ACTIVE button as a fallback - Shuffle is
    /// present but disabled, so cloning it would produce an invisible entry.
    /// </summary>
    private static Transform? FindTemplateButton(Transform container)
    {
        Transform? fallback = null;
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            if (child.name == ButtonName) continue;
            if (child.GetComponent<Button>() == null) continue;
            if (!child.gameObject.activeSelf) continue;

            if (child.name.StartsWith("Settings", StringComparison.Ordinal))
            {
                return child;
            }
            if (!child.name.StartsWith("Quit", StringComparison.Ordinal))
            {
                fallback = child;
            }
        }
        return fallback;
    }

    /// <summary>
    /// A small state tag to the LEFT of the menu entry, so the connection is
    /// visible from the title screen without opening the dialog.
    ///
    /// Right-aligned and hung off the button's left edge, because the menu
    /// itself is right-aligned - anchoring it inside the button would put it
    /// on top of the word.
    /// </summary>
    /// <summary>
    /// Remember the menu entry's own label so the state can be written into
    /// it.
    ///
    /// A SEPARATE text object beside the button was tried twice and fought the
    /// menu's layout both times - the entries are right-aligned inside a rect
    /// wider than the word, so anchoring either overlapped "Archipelago" or
    /// flew off to the far left. Writing a rich-text prefix into the existing
    /// label puts the tag exactly where it belongs with no layout maths at
    /// all, because the game is already positioning that label correctly.
    /// </summary>
    private static void AddMenuIndicator(GameObject button)
    {
        foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null) continue;
            _menuIndicator = label;
            _menuIndicator.richText = true;
            break;
        }
        RefreshMenuIndicator();
    }

    private static void SetLabel(GameObject root, string text)
    {
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null) continue;
            // A localised string would be overwritten on the next refresh.
            var localiser = label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
            if (localiser != null) UnityEngine.Object.Destroy(localiser);
            label.text = text;
            // "Archipelago" is longer than any stock entry and wrapped into a
            // narrow column of letters on the first attempt.
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }
    }

    /// <summary>Show the dialog.</summary>
    internal static void Open()
    {
        try
        {
            // Stepwise, and with explicit == null rather than ?., because an
            // IL2CPP object can be a live managed wrapper around a destroyed
            // native object: ?. sees non-null and the next call throws.
            var gm = GameManager.Instance;
            if (gm == null) { Plugin.Logger.LogWarning("pane: no GameManager"); return; }

            var menus = gm.menuManager;
            if (menus == null) { Plugin.Logger.LogWarning("pane: no menuManager"); return; }

            var modal = menus.modalWindow;
            if (modal == null) { Plugin.Logger.LogWarning("pane: no modal window"); return; }

            // UIModalContent is a MonoBehaviour, not a ScriptableObject, so it
            // needs a GameObject to live on rather than CreateInstance.
            var holder = new GameObject("ApModalContent");
            var content = holder.AddComponent<UIModalContent>();
            if (content == null) { Plugin.Logger.LogWarning("pane: no content"); return; }
            content.title = "Archipelago";
            content.message = "";
            content.confirmText = Plugin.IsConnected ? "Disconnect" : "Connect";
            content.declineText = "Close";
            content.allowDismissal = true;

            // Layout.GameObject is the mode built for a caller-supplied body -
            // the other two expect the modal's own image and text slots.
            content.layout = UIModal.Layout.GameObject;

            // The modal's own style. Leaving it null threw a
            // NullReferenceException inside ShowModal, because the game reads
            // colours and fonts off it while building.
            content.style = modal.defaultStyle;
            content.modalPreferredWidth = 620;

            content.contentGameobject = BuildFields();

            // ALL THREE events, not just the one we use. The game invokes
            // whichever the player triggers, and a null UnityEvent throws.
            content.onConfirmEvent = new UnityEngine.Events.UnityEvent();
            content.onConfirmEvent.AddListener(new Action(OnConfirm));
            content.onDeclineEvent = new UnityEngine.Events.UnityEvent();
            content.onDeclineEvent.AddListener(new Action(Closed));
            content.onDismissEvent = new UnityEngine.Events.UnityEvent();
            content.onDismissEvent.AddListener(new Action(Closed));

            modal.ShowModal(content);

            // The modal leaves confirm and decline HIDDEN for us. It decides
            // that while building, and a runtime-added UnityEvent listener does
            // not look like a real one to it - GetPersistentEventCount is 0 for
            // anything not serialised in the scene. So they are switched on and
            // wired by hand here, after the build, rather than reproducing the
            // game's buttons ourselves.
            // Only the confirm button. A separate Close never worked - the
            // modal's decline path is wired for its own content - and the X in
            // the corner already closes this the same way it closes every other
            // dialog in the game, so a second control was redundant anyway.
            _confirm = modal.confirmButton?.gameObject;
            ShowAndWire(_confirm, OnConfirm, ConfirmLabel());


            // REBIND to the modal's own copies.
            //
            // ShowModal INSTANTIATES contentGameobject rather than reparenting
            // it, so the fields the player types into are clones and the
            // originals we built are discarded. Measured: the live fields had
            // different instance ids from ours. Holding the originals meant
            // isFocused was never true - so the typing guard never engaged and
            // the mouse drifted - and every status update was written to a
            // label nobody could see.
            RebindToLiveCopies(modal);

            // Resolve Rewired now the game is fully up. It reports whether the
            // mouse-drift fix can work, before the player types rather than
            // after they hit the bug.
            TypingGuard.Probe();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"could not open the connection pane: {e}");
        }
    }

    /// <summary>
    /// Force one of the modal's own buttons visible and point it at our code.
    /// Its parents are activated too - the footer that holds them is hidden as
    /// well, so activating the button alone leaves it invisible.
    /// </summary>
    private static void ShowAndWire(GameObject? go, Action action, string label)
    {
        if (go == null) return;

        // ONLY this object and its immediate parent (the footer that holds
        // it). An earlier version walked the whole ancestor chain activating
        // anything inactive, and the modal's ancestors include other menus the
        // game had deliberately switched off - clicking Archipelago switched
        // the Daily Tidy screen on underneath it.
        var parent = go.transform.parent;
        if (parent != null && parent.gameObject != null)
        {
            parent.gameObject.SetActive(true);
        }
        go.SetActive(true);

        foreach (var text in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null) continue;
            var localiser = text.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
            if (localiser != null) UnityEngine.Object.Destroy(localiser);
            text.text = label;
            text.enableWordWrapping = false;
        }

        // EXACTLY ONE hook, whichever the control actually uses.
        //
        // A previous version wired both the inner Button and the
        // UILongPressButton's own UnityEvent "to be safe". Both fired on a
        // single press, so Disconnect ran twice: the first call dropped the
        // session and the second, now seeing no session, reconnected it. On
        // screen that looked like the button flashing and staying on
        // Disconnect. Belt and braces is a bug when the action is a toggle.
        var button = go.GetComponent<Button>()
                     ?? go.GetComponent<UILongPressButton>()?.Button;

        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            button.interactable = true;
            Plugin.Logger.LogInfo($"pane: '{go.name}' wired");
            return;
        }

        // No Button anywhere: fall back to the long-press event, which is then
        // the only path there is.
        var longPress = go.GetComponent<UILongPressButton>();
        if (longPress != null)
        {
            longPress.m_btnAction = new UnityEngine.Events.UnityEvent();
            longPress.m_btnAction.AddListener(action);
            Plugin.Logger.LogInfo($"pane: '{go.name}' wired via long press");
            return;
        }

        Plugin.Logger.LogWarning(
            $"pane: '{go.name}' has no clickable component; it will do nothing");
    }

    /// <summary>
    /// Point our references at the instances actually on screen.
    ///
    /// Matched by order and by name rather than by reference, because the
    /// clone has fresh objects throughout. Order is the order they were added:
    /// server, slot, password.
    /// </summary>
    private static void RebindToLiveCopies(UIModal modal)
    {
        var fields = modal.GetComponentsInChildren<TMP_InputField>(true);
        if (fields.Length >= 3)
        {
            _address = fields[0];
            _slot = fields[1];
            _password = fields[2];
        }
        else
        {
            Plugin.Logger.LogWarning(
                $"pane: expected 3 input fields in the modal, found {fields.Length}");
        }

        _status = null;
        foreach (var text in modal.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && text.gameObject.name == "Status") { _status = text; break; }
        }
        if (_status == null) Plugin.Logger.LogWarning("pane: no live status label");

        // The pills, rewired onto the copy. Unity does not carry runtime
        // AddListener calls across Instantiate, so the handlers built a moment
        // ago belong to the discarded original - the row would look right and
        // do nothing, which is exactly how the first version behaved.
        _pillOff = null;
        _pillOn = null;
        foreach (var candidate in modal.GetComponentsInChildren<Button>(true))
        {
            if (candidate == null) continue;
            var owner = candidate.gameObject.name;
            bool value;
            if (owner == "AutoConnectOn") value = true;
            else if (owner == "AutoConnectOff") value = false;
            else continue;

            if (value) _pillOn = candidate.gameObject; else _pillOff = candidate.gameObject;

            var setting = value;
            candidate.onClick = new Button.ButtonClickedEvent();
            candidate.onClick.AddListener(new Action(() =>
            {
                Plugin.SetAutoConnect(setting);
                RefreshStatus();
            }));
        }
        if (_pillOn == null || _pillOff == null)
        {
            Plugin.Logger.LogWarning("pane: auto-connect pills not rebound");
        }

        Plugin.Logger.LogInfo(
            $"pane: bound to the modal's copies ({fields.Length} fields, "
            + $"status={(_status == null ? "missing" : "ok")})");
        RefreshStatus();
    }

    private static string Describe(GameObject? go)
        => go == null ? "missing" : (go.activeInHierarchy ? "shown" : "hidden");

    private static GameObject BuildFields()
    {
        var root = new GameObject("ApConnectionFields");
        // INACTIVE while it is assembled. A TMP_InputField built active never
        // initialises its caret and silently refuses to accept typing.
        root.SetActive(false);

        var rect = root.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(560f, 280f);

        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        // Property-by-property: the interop RectOffset has no 4-argument
        // constructor, only the default one.
        var padding = new RectOffset();
        padding.left = 16; padding.right = 16; padding.top = 8; padding.bottom = 8;
        layout.padding = padding;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        // The modal sizes itself from the content, and without a preferred
        // size it collapsed the rows to a narrow, unreadable column.
        var fitter = root.AddComponent<LayoutElement>();
        fitter.preferredWidth = 560f;
        fitter.preferredHeight = 280f;

        var font = FindFont();

        _address = AddField(root, font, "Server", CurrentAddress(), false);
        _slot = AddField(root, font, "Slot name", Plugin.SlotNameSetting, false);
        _password = AddField(root, font, "Password", Plugin.PasswordSetting, true);
        AddAutoToggle(root, font);
        _status = AddStatus(root, font);

        root.SetActive(true);   // now, and only now
        return root;
    }

    private static TMP_InputField AddField(GameObject parent, TMP_FontAsset? font,
                                           string label, string value, bool secret)
    {
        var row = new GameObject(label);
        row.transform.SetParent(parent.transform, false);
        var rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(520f, 56f);
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 56f;
        rowLayout.minHeight = 56f;

        var caption = new GameObject("Label");
        caption.transform.SetParent(row.transform, false);
        var captionRect = caption.AddComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(0.32f, 1f);
        captionRect.offsetMin = Vector2.zero;
        captionRect.offsetMax = Vector2.zero;
        var captionText = caption.AddComponent<TextMeshProUGUI>();
        if (font != null) captionText.font = font;
        captionText.text = label;
        captionText.fontSize = 22f;
        captionText.alignment = TextAlignmentOptions.MidlineLeft;
        // Dark on the modal's cream panel. The default white was legible on
        // the level select and nearly invisible here.
        captionText.color = Ink;
        captionText.fontStyle = FontStyles.Normal;

        var box = new GameObject("Input");
        box.transform.SetParent(row.transform, false);
        var boxRect = box.AddComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.34f, 0f);
        boxRect.anchorMax = new Vector2(1f, 1f);
        boxRect.offsetMin = Vector2.zero;
        boxRect.offsetMax = Vector2.zero;

        var background = box.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.08f);

        // The text child must exist before the field is wired to it.
        var textArea = new GameObject("Text");
        textArea.transform.SetParent(box.transform, false);
        var textRect = textArea.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 4f);
        textRect.offsetMax = new Vector2(-10f, -4f);
        var text = textArea.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.color = Ink;
        text.fontStyle = FontStyles.Normal;

        var field = box.AddComponent<TMP_InputField>();
        field.textViewport = textRect;
        field.textComponent = text;
        field.fontAsset = font;
        field.pointSize = 22f;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = secret
            ? TMP_InputField.ContentType.Password
            : TMP_InputField.ContentType.Standard;
        field.text = value ?? "";
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = Ink;

        return field;
    }

    /// <summary>
    /// An OFF / ON pair for auto-connect, in the game's own settings idiom.
    ///
    /// A single line reading "Auto-connect: on" was accurate and read as a
    /// statement rather than a control - nothing about it suggested it could
    /// be clicked. The Settings screen already solves this with two pills
    /// where the active one is highlighted, so this copies that.
    /// </summary>
    private static void AddAutoToggle(GameObject parent, TMP_FontAsset? font)
    {
        var row = new GameObject("AutoConnectRow");
        row.transform.SetParent(parent.transform, false);
        var rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520f, 48f);
        var layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = 48f;
        layout.minHeight = 48f;

        var caption = new GameObject("Caption");
        caption.transform.SetParent(row.transform, false);
        var captionRect = caption.AddComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(0.46f, 1f);
        captionRect.offsetMin = Vector2.zero;
        captionRect.offsetMax = Vector2.zero;
        var captionText = caption.AddComponent<TextMeshProUGUI>();
        if (font != null) captionText.font = font;
        captionText.text = "Auto-connect";
        captionText.fontSize = 22f;
        captionText.alignment = TextAlignmentOptions.MidlineLeft;
        captionText.color = Ink;
        captionText.fontStyle = FontStyles.Normal;
        captionText.raycastTarget = false;

        AddPill(row, font, "AutoConnectOff", "OFF", 0.50f, 0.72f, false);
        AddPill(row, font, "AutoConnectOn", "ON", 0.74f, 0.96f, true);
    }

    private static void AddPill(GameObject row, TMP_FontAsset? font, string name,
                                string label, float from, float to, bool value)
    {
        var pill = new GameObject(name);
        pill.transform.SetParent(row.transform, false);

        var rect = pill.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(from, 0.12f);
        rect.anchorMax = new Vector2(to, 0.88f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        pill.AddComponent<Image>();

        var textObject = new GameObject("Text");
        textObject.transform.SetParent(pill.transform, false);
        var textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = label;
        text.fontSize = 19f;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Normal;
        text.raycastTarget = false;

        var button = pill.AddComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(new Action(() =>
        {
            Plugin.SetAutoConnect(value);
            RefreshStatus();
        }));
    }

    /// <summary>Highlight whichever pill is the current setting.</summary>
    private static void PaintPills(bool autoConnect)
    {
        Paint(_pillOff, !autoConnect);
        Paint(_pillOn, autoConnect);

        static void Paint(GameObject? pill, bool active)
        {
            if (pill == null) return;
            var image = pill.GetComponent<Image>();
            if (image != null)
            {
                image.color = active
                    ? new Color(0.94f, 0.85f, 0.55f)      // the game's own yellow
                    : new Color(0f, 0f, 0f, 0.08f);
            }
            foreach (var text in pill.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null) continue;
                text.color = active ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.45f);
            }
        }
    }

    private static TextMeshProUGUI AddStatus(GameObject parent, TMP_FontAsset? font)
    {
        var go = new GameObject("Status");
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 40f);

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = Plugin.StatusLine();
        text.fontSize = 20f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = new Color(Ink.r, Ink.g, Ink.b, 0.75f);
        text.fontStyle = FontStyles.Normal;
        return text;
    }

    /// <summary>
    /// Borrow the game's own font atlas rather than shipping one. Prefer a
    /// live in-use component: a font from FindObjectsOfTypeAll can be one the
    /// game never actually renders with.
    /// </summary>
    private static TMP_FontAsset? FindFont()
    {
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t != null && t.font != null) return t.font;
        }
        return null;
    }

    private static string CurrentAddress()
        => $"{Plugin.HostSetting}:{Plugin.PortSetting}";

    private static void OnConfirm()
    {
        // Logged so a single press is provably a single action. The previous
        // build wired two hooks to this and both fired, which disconnected and
        // then immediately reconnected - two of these lines per press would
        // mean that has come back.
        Plugin.Logger.LogInfo(
            $"pane: action pressed (connected={Plugin.IsConnected}, "
            + $"busy={Plugin.IsBusy})");

        if (Plugin.IsConnected)
        {
            Plugin.DisconnectNow();
            return;
        }

        // Busy means connecting or waiting out a backoff. The press is a
        // refusal, not a request: stop, and do not dial again.
        if (Plugin.IsBusy)
        {
            Plugin.CancelConnect();
            return;
        }

        // Address is one field split on the last colon, so IPv6 and a bare
        // hostname both behave.
        var address = _address?.text?.Trim() ?? "";
        var host = address;
        var port = Plugin.PortSetting;
        var colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address.Substring(colon + 1), out var parsed))
        {
            host = address.Substring(0, colon);
            port = parsed;
        }

        Plugin.ApplySettings(host, port, _slot?.text?.Trim() ?? "",
                             _password?.text ?? "");
        Plugin.ConnectNow();
    }

    /// <summary>
    /// The field the player is typing in, or null. Drives input suppression.
    /// </summary>
    internal static TMP_InputField? FocusedField()
    {
        // Written out rather than looped over a temporary array: this is called
        // every frame by the typing guard, so the array was sixty allocations a
        // second for the whole session, pane open or not.
        if (_address != null && _address.isFocused) return _address;
        if (_slot != null && _slot.isFocused) return _slot;
        if (_password != null && _password.isFocused) return _password;
        return null;
    }

    /// <summary>Move focus to the next box, wrapping. Bound to Tab.</summary>
    internal static void FocusNext()
    {
        var order = new[] { _address, _slot, _password };
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == null || !order[i]!.isFocused) continue;

            for (int step = 1; step <= order.Length; step++)
            {
                var next = order[(i + step) % order.Length];
                if (next == null) continue;

                order[i]!.DeactivateInputField();
                next.ActivateInputField();
                next.Select();
                return;
            }
            return;
        }
    }

    /// <summary>Called when the pane closes, so nothing stays suppressed.</summary>
    internal static void Closed()
    {
        _address = null;
        _slot = null;
        _password = null;
        _status = null;
        _confirm = null;
        _pillOff = null;
        _pillOn = null;
        // _menuIndicator deliberately kept: it lives on the main menu, not in
        // the dialog, and must keep reporting after this closes.
        TypingGuard.Restore();
        RefreshMenuIndicator();
    }

    /// <summary>
    /// Bring every piece of the UI in line with the connection state.
    ///
    /// One place rather than several, because these all have to agree: a
    /// "Connect" button next to "Connected as droha" would be nonsense.
    /// </summary>
    internal static void RefreshStatus()
    {
        if (_status != null) _status.text = Plugin.StatusLine();

        // The button says what it will DO, so it flips to Disconnect once a
        // session is up.
        if (_confirm != null)
        {
            foreach (var text in _confirm.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text != null) text.text = ConfirmLabel();
            }
        }

        // Locked mid-attempt: editing the address while it is being dialled
        // just means the failure reported belongs to the old value.
        var editable = !Plugin.IsConnecting;
        foreach (var field in new[] { _address, _slot, _password })
        {
            if (field != null) field.interactable = editable;
        }

        PaintPills(Plugin.AutoConnectEnabled);

        RefreshMenuIndicator();
    }

    /// <summary>
    /// The little state tag beside the main-menu entry. Survives the pane
    /// being closed, so it is looked up rather than cached.
    /// </summary>
    private static void RefreshMenuIndicator()
    {
        if (_menuIndicator == null) return;
        try
        {
            string tag;
            if (Plugin.IsConnected) tag = "<color=#6BC77A>connected</color>";
            else if (Plugin.IsConnecting) tag = "<color=#E6C759>connecting</color>";
            // A run from the cache is not the same as no run, and the entry
            // used to read the same for both. "offline" alone next to a full
            // track invites the player to think their checks are being lost.
            else if (Plugin.IsOffline) tag = "<color=#E6C759>offline run</color>";
            else tag = "<color=#FFFFFF80>offline</color>";

            // Smaller and dimmer than the entry itself, so it reads as a note
            // about the item rather than part of its name.
            _menuIndicator.text = $"<size=55%>{tag}</size>  Archipelago";
        }
        catch
        {
            // The menu was torn down; it is rebuilt with the screen.
            _menuIndicator = null;
        }
    }
}
