using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// Blanking the game's "Chapter N" line during a run.
///
/// A run has packs, not chapters, so the vanilla subtitle is actively wrong
/// while one is up. Ticked from LateUpdate rather than Update - see the note
/// on the call in Plugin.cs, which records the one-frame flash that caused.
/// </summary>
internal static partial class Badges
{
    private static float _sinceSubtitle;

    /// <summary>
    /// Blank the level select's "Chapter N" line while a run is on.
    ///
    /// The game writes that subtitle from the section index, and it only
    /// has names for the five chapters it shipped with. A run has as many
    /// sections as it has packs - fifteen at the default - so past the
    /// fifth the line has nothing to say and the heading reads differently
    /// from every one before it. droha: "the chapters at 5 don't have a
    /// name... just remove the chapters x, and just have the - for all of
    /// them."
    ///
    /// Blanked rather than renumbered, because the run's own name for the
    /// section is already on screen directly underneath - "Opening",
    /// "Pack 3", "The End" - and a chapter number above a pack name is two
    /// different countings of the same thing. What is left is the dash and
    /// the star count, which is the same on every section.
    ///
    /// Polled, and only while the run's track is up: the game rewrites this
    /// label whenever the section changes, and the archive and daily menus
    /// use the same header with chapter names that are theirs to keep.
    /// </summary>
    internal static void TickChapterSubtitle()
    {
        if (!Track.Active) return;

        try
        {
            // EVERY FRAME, not on the half-second poll this started on. The
            // game rewrites the subtitle as each section scrolls under the
            // header, so a poll left the old chapter name on screen until
            // its next tick - droha: "I see chapter 1/2/3/4/5 show up when I
            // scroll over the chapter markers". There is nothing to poll
            // FOR here; the label either has text or it does not.
            //
            // The search is what costs, so only that is throttled: the label
            // is remembered, and looked for again only when the reference
            // has gone - which happens when the menu is rebuilt.
            if (_subtitle == null)
            {
                _sinceSubtitle += Time.unscaledDeltaTime;
                if (_sinceSubtitle < 0.5f) return;
                _sinceSubtitle = 0f;

                var select = Track.CampaignSelect();
                if (select == null || !select.gameObject.activeInHierarchy) return;

                var found = FindDeep(select.transform, "Subtitle");
                _subtitle = found == null
                    ? null
                    : found.GetComponent<TextMeshProUGUI>();
                if (_subtitle == null) return;
            }

            // Text AND the renderer. Clearing the text alone still leaves a
            // live label for the game to write into; turning the renderer off
            // means that even if something sets the text between this and the
            // draw, there is nothing on screen to see. Both, because the two
            // failures are different: the text is what the game keeps putting
            // back, the renderer is what would show it.
            if (!string.IsNullOrEmpty(Str(() => _subtitle.text))) _subtitle.text = "";
            if (_subtitle.enabled) _subtitle.enabled = false;
        }
        catch (Exception e)
        {
            _subtitle = null;
            Plugin.Logger.LogWarning($"badges: could not blank the subtitle: {e.Message}");
        }
    }

    private static TextMeshProUGUI? _subtitle;

    /// <summary>First descendant with this name, breadth first.</summary>
    private static Transform? FindDeep(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null) continue;
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null) continue;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
