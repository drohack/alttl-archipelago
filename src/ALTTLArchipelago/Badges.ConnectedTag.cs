using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The connection state, written into the main menu's own Archipelago label.
/// </summary>
internal static partial class Badges
{
    private static float _sinceTag;

    private static TextMeshProUGUI? _tag;

    /// <summary>
    /// Say whether the run is connected, on the level select.
    ///
    /// The main menu has had this since the pane was built
    /// (ConnectionPane.RefreshMenuIndicator), but the level select is where a
    /// player actually spends the run, and it was the one screen that never
    /// said. Losing the server mid-session is otherwise invisible until a check
    /// fails to land.
    ///
    /// Parented to the toast overlay rather than to the track. The track
    /// scrolls and is rebuilt whenever a pack arrives, and the main menu's own
    /// version of this needed a rich-text prefix on an existing label
    /// specifically because a free-standing object lost to that screen's
    /// layout. The overlay is the mod's own screen-space canvas, so it has no
    /// layout to lose to and nothing rebuilds it.
    ///
    /// Four states, matching the menu exactly - including "offline run", which
    /// is playing from the local cache and is NOT the same as having no run.
    /// </summary>
    internal static void TickConnectedTag()
    {
        _sinceTag += Time.unscaledDeltaTime;
        if (_sinceTag < 0.5f) return;
        _sinceTag = 0f;

        // Only while the run's own track is the thing on screen.
        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;

        if (!visible)
        {
            if (_tag != null) _tag.gameObject.SetActive(false);
            return;
        }

        if (_tag == null)
        {
            var root = Toasts.OverlayRoot;
            if (root == null) return;                // overlay not built yet

            _tag = MakeCorner("ApConnectedTag", root, new Vector2(-24f, -18f));
            if (_tag == null) return;
        }

        _tag.gameObject.SetActive(true);

        string state;
        if (Plugin.IsConnected) state = "<color=#6BC77A>connected</color>";
        else if (Plugin.IsConnecting) state = "<color=#E6C759>connecting</color>";
        else if (Plugin.IsOffline) state = "<color=#E6C759>offline run</color>";
        else state = "<color=#FFFFFF80>offline</color>";

        _tag.text = $"{state}<color=#FFFFFF80>  Archipelago</color>";
    }
}
