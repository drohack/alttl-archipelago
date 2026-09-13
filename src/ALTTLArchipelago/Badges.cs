using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// A small badge in the corner of each card saying whether it is worth opening.
///
///   green            everything still to do here can be done now
///   green over red   some of it can, some of it cannot
///   red              none of it can, yet
///   star             nothing left to do
///
/// Split corner to corner rather than into two rectangles, which is what a card
/// tilted on the track needs in order to still read at a glance.
///
/// The card's own text is left completely alone. A level's name is its name - a
/// tracker that rewrote it to "[LOCKED] Books 3" was rejected, and rightly: the
/// badge carries the state and the name carries the name.
///
/// The drawing below is ported from the marker probe that was checked by eye in
/// game. What is new is where the state comes from - SlotProgress, reading the
/// generator's own requirements table.
/// </summary>
internal static class Badges
{
    private const string BadgeName = "ApTrackerBadge";
    private const string NameLabel = "ApCardName";
    private const string DimName = "ApLockedDim";

    /// <summary>Size of the badge, square, in the icon's own units.</summary>
    private const float BadgeSize = 30f;

    private static readonly Color Green = new Color(0.25f, 0.70f, 0.30f);
    private static readonly Color Red = new Color(0.85f, 0.20f, 0.20f);

    /// <summary>Fallback only - the game's own star colour is preferred.</summary>
    private static readonly Color FallbackStar = new Color(0.98f, 0.80f, 0.25f);

    /// <summary>
    /// The veil over a card the player cannot play yet.
    ///
    /// The GAME cannot show this. Its "locked" look - line art instead of full
    /// colour - is driven by LevelInterface.IsUnlocked, which is simply "this
    /// level has a LevelCompletionData row", and that save is keyed by levelId
    /// with no per-slot scoping. A run holding the same level in three slots is
    /// three cards pointing at ONE LevelInterface, so the moment the first is
    /// unlocked all three turn to full colour. That was reported as "when an
    /// Envelope level is in the pack, ALL Envelope levels look playable".
    ///
    /// Nothing in the save can express the difference, so it is drawn instead.
    /// </summary>
    private static readonly Color LockedVeil = new Color(0.05f, 0.05f, 0.08f, 0.55f);

    private static float _sinceRefresh;


    /// <summary>
    /// What each card last showed, so an unchanged track is not redrawn.
    /// Rebuilding every badge every second churns objects the game is
    /// animating, and the card tilt would fight it.
    /// </summary>
    /// <summary>
    /// The badge each SLOT is currently wearing, so an unchanged card is not
    /// repainted every tick.
    ///
    /// Keyed by slot rather than by track position on purpose. Position is not
    /// stable: a pack arriving inserts a divider, and every card after it
    /// shifts by one, so a position-keyed entry silently starts describing its
    /// neighbour. The badge survives that today only because the entry is also
    /// checked against the badge still being on screen and a rebuild destroys
    /// it - which is a second mechanism covering for the first. Slot is a
    /// bijection with the non-divider cards and needs no covering.
    /// </summary>
    private static readonly Dictionary<int, SlotStatus> _shown = new();

    /// <summary>
    /// Level ids the run contains more than once, so their cards can say which
    /// instance they are. Rebuilt when the cache is reset.
    /// </summary>
    private static readonly HashSet<string> _repeated = new(StringComparer.Ordinal);
    private static bool _repeatedBuilt;

    internal static void Reset()
    {
        _shown.Clear();
        _repeated.Clear();
        _repeatedBuilt = false;

        _dotsReported = false;

        if (_tag != null)
        {
            try { UnityEngine.Object.Destroy(_tag.gameObject); } catch { }
            _tag = null;
        }
        if (_counter != null)
        {
            try { UnityEngine.Object.Destroy(_counter.gameObject); } catch { }
            _counter = null;
            _counterStar = null;      // a child of the counter, already gone
        }
        if (_pills != null)
        {
            try { UnityEngine.Object.Destroy(_pills.gameObject); } catch { }
            _pills = null;
            _pillOrder.Clear();
            _pillTiles.Clear();
        }
        _sinceRefresh = 0f;
    }

    /// <summary>
    /// Refresh the badges while the track is on screen.
    ///
    /// Polled for the same reason as the rest: the game rebuilds the icons
    /// whenever the menu opens, taking our badges with them, and no event
    /// reliably fires after that has finished.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (!Track.Active) return;

        _sinceRefresh += dt;
        if (_sinceRefresh < 1f) return;
        _sinceRefresh = 0f;

        try
        {
            Refresh();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: refresh failed: {e.Message}");
        }
    }

    private static float _sinceTag;

    /// <summary>
    /// Paint the run's decorations on the NEXT frame rather than up to a
    /// second from now.
    ///
    /// The connected tag polls every 0.5s and the overview dots every 1s, on
    /// free-running timers that know nothing about the menu opening. Open the
    /// level select at the wrong moment in that cycle and you watch the
    /// Archipelago furniture arrive after the screen has already settled -
    /// reported as "the level select takes a moment to pop in all of the
    /// archipelago stuff. it's kind of jarring."
    ///
    /// Polling is still the right shape - there is no single reliable event
    /// for every route into that menu, which is why the track rebuild is on a
    /// timer too. What was wrong was letting a poll that exists to CATCH
    /// changes also decide when the FIRST paint happens.
    /// </summary>
    internal static void RepaintSoon()
    {
        _sinceTag = float.MaxValue;
        _sinceDots = float.MaxValue;
        _sinceCounter = float.MaxValue;
        _sincePills = float.MaxValue;
        _sinceSubtitle = float.MaxValue;
        _subtitle = null;
    }
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

    private static float _sinceCounter;
    private static TextMeshProUGUI? _counter;

    /// <summary>
    /// How far along the goal is, on the level select.
    ///
    /// droha asked for a "levels beaten / needed" counter: the number only
    /// appeared in a toast that scrolls away, so the one screen where you
    /// decide what to play next never said how close you were.
    ///
    /// READS THE GOAL, not the beaten count. A star seed wants starred
    /// puzzles, and a counter that always said "beaten" would be quietly
    /// measuring the wrong thing on half the seeds - Checks.GoalProgress
    /// owns that choice so this does not have to.
    ///
    /// Everything structural here is copied from TickConnectedTag above and
    /// for the same reasons: the toast overlay as parent because the track
    /// scrolls and is rebuilt, the same visibility gate so it cannot float
    /// over a puzzle, and a place in RepaintSoon so it does not pop in a
    /// second after the screen has settled.
    /// </summary>
    internal static void TickGoalCounter()
    {
        _sinceCounter += Time.unscaledDeltaTime;
        if (_sinceCounter < 0.5f) return;
        _sinceCounter = 0f;

        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;

        var (done, needed, unit) = Checks.GoalProgress(Plugin.Seed);
        // A run with no goal to report is a run with nothing to say. Better
        // blank than "0 / 0".
        if (!visible || needed <= 0)
        {
            if (_counter != null) _counter.gameObject.SetActive(false);
            return;
        }

        if (_counter == null)
        {
            var root = Toasts.OverlayRoot;
            if (root == null) return;                // overlay not built yet

            // Under the connected tag, which owns the top-right corner and
            // is 28 high at y -18.
            _counter = MakeCorner("ApGoalCounter", root, new Vector2(-24f, -48f));
            if (_counter == null) return;
            BuildCounterStar();
        }

        _counter.gameObject.SetActive(true);

        // THE STAR GOAL GETS THE STAR, not the word. droha: "for star goal we
        // should have 0/50 [star icon]s instead of it saying stars or beaten."
        // It is the game's own level-select star, the same one the card wears
        // when a puzzle has nothing left on it, so the counter and the card
        // are plainly talking about the same thing.
        //
        // The beaten goal keeps its word. There is no icon in the game for
        // "finished any one way" - the Icon- sprites are per-LEVEL card art,
        // not per-anything-else - and inventing a glyph for it would be less
        // clear than the word, not more.
        var starred = Plugin.Seed?.GoalIsStars == true;
        if (_counterStar != null) _counterStar.gameObject.SetActive(starred);

        var colour = done >= needed ? "#6BC77A" : "#FFFFFFB0";
        _counter.text = starred
            ? $"<color={colour}>{done} / {needed}</color>"
            : $"<color={colour}>{done} / {needed}</color>"
              + $"<color=#FFFFFF80>  {unit}</color>";

        // The text shifts left to make room, rather than the star being laid
        // out around it - there is no layout group on the overlay and adding
        // one to position a single image would be more machinery than this
        // needs.
        var rt = _counter.rectTransform;
        rt.sizeDelta = new Vector2(starred ? 230f : 260f, 28f);
        rt.anchoredPosition = new Vector2(starred ? -50f : -24f, -48f);
    }

    private static Image? _counterStar;

    /// <summary>
    /// Borrow the level select's own star for the counter.
    ///
    /// By name from the loaded sprites, which is how FindStar below reaches
    /// the same art on a card - except that one scans a LevelIcon's children
    /// and there is no card here to scan. The names were read out of the
    /// running game with the DevTools `sprites` command rather than guessed:
    /// that command exists because this file's history contains two wrong
    /// guesses at a sprite name.
    ///
    /// Several candidates, in preference order. The level-select star first
    /// because matching the card is the whole point; the others are there so
    /// a renamed asset costs a plainer star rather than no counter.
    /// </summary>
    private static void BuildCounterStar()
    {
        if (_counter == null) return;

        var sprite = FindSpriteNamed(
            "LTL-LevelSelect-Star-solved", "Star-full", "Icon-Star-unlocked");
        if (sprite == null)
        {
            Plugin.Logger.LogInfo(
                "badges: no star sprite found, the counter will use its word");
            return;
        }

        var go = new GameObject("ApGoalStar");
        go.transform.SetParent(_counter.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(6f, 0f);
        rt.sizeDelta = new Vector2(20f, 20f);

        _counterStar = go.AddComponent<Image>();
        _counterStar.sprite = sprite;
        _counterStar.color = FallbackStar;
        _counterStar.raycastTarget = false;
        _counterStar.preserveAspect = true;
    }

    /// <summary>
    /// The first loaded sprite matching any of these names, or null.
    ///
    /// FindObjectsOfTypeAll because a sprite on an inactive object is still
    /// a perfectly good sprite, and the level select is inactive at the
    /// moment this runs.
    /// </summary>
    private static Sprite? FindSpriteNamed(params string[] names)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
            if (all == null) return null;

            // By preference order, not by scan order: a later name must not
            // win just because it sorts earlier in the asset table.
            foreach (var wanted in names)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var sprite = all[i]?.TryCast<Sprite>();
                    if (sprite == null) continue;
                    if (string.Equals(Str(() => sprite.name), wanted,
                                      StringComparison.Ordinal))
                    {
                        return sprite;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: sprite search failed: {e.Message}");
        }
        return null;
    }

    /// <summary>
    /// A right-aligned label in the overlay's top-right corner.
    ///
    /// Factored out when the counter arrived: the tag and the counter are
    /// the same object bar a position, and a third one is coming for the
    /// ability pills. Six sites in this file open-code the same
    /// GameObject / RectTransform / graphic dance, and this is the first
    /// two of them collapsed.
    /// </summary>
    private static TextMeshProUGUI? MakeCorner(string name, Transform root,
                                               Vector2 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(260f, 28f);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Right;
        text.fontSize = 18f;
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

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

    private static float _sincePills;
    private static RectTransform? _pills;
    private static readonly List<string> _pillOrder = new();

    /// <summary>
    /// The tiles, in the same order as _pillOrder.
    ///
    /// HELD, NOT LOOKED UP BY INDEX. The repaint used to walk the strip's
    /// children by position, which was true right up until a backing panel
    /// was added as the first child - and then every icon drew under its
    /// neighbour's label. Books appeared over SWP's slot reading STK, and
    /// the twelfth tile had nothing in it at all. An index into a child
    /// list is a fact about the hierarchy; this is a fact about the strip.
    /// </summary>
    private static readonly List<RectTransform> _pillTiles = new();

    /// <summary>Held, and locked. Deliberately far apart so a glance reads.</summary>
    private static readonly Color PillHeld = new(0.25f, 0.70f, 0.30f, 0.85f);
    private static readonly Color PillLocked = new(0.16f, 0.16f, 0.19f, 0.75f);

    /// <summary>
    /// Locked art: dark and half transparent, so a locked mechanic reads as
    /// absent at a glance while its shape is still there to be recognised
    /// once you hold it.
    /// </summary>
    private static readonly Color IconLocked = new(0.42f, 0.42f, 0.46f, 0.55f);


    /// <summary>
    /// Three letters per mechanic. Short enough to fit, long enough to guess.
    ///
    /// LETTERS RATHER THAN ICONS, for now. The mod ships no art at all - the
    /// only graphic it has ever generated is the diagonal below - and the
    /// game's own sprites are borrowed by name, which has twice been guessed
    /// wrong in this file. droha's call: build the pills with text first,
    /// then go looking for in-game icons that would work. The DevTools
    /// `sprites` command exists to do that looking with real names rather
    /// than another guess.
    /// </summary>
    private static readonly Dictionary<string, string> PillText =
        new(StringComparer.Ordinal)
        {
            ["Swapping"] = "SWP",
            ["Stacking"] = "STK",
            ["Ordering"] = "ORD",
            ["Gadgets"] = "GAD",
            ["Rotating"] = "ROT",
            ["Grids"] = "GRD",
            ["Tidying"] = "TDY",
            ["Containers"] = "CON",
            ["Drawer"] = "DRW",
            ["Sticking"] = "STI",
            ["Symmetry"] = "SYM",
            ["Jigsaw"] = "JIG",
        };

    /// <summary>
    /// A borrowed item sprite per mechanic, and how it was chosen.
    ///
    /// THE GAME HAS NO ABILITY ART. Abilities are the mod's invention, so
    /// there is nothing to look up - the Icon- family is per-LEVEL card art,
    /// not per-mechanic. What the game does have is a large set of small
    /// single-object badge sprites, and those are what these are.
    ///
    /// CHOSEN BY LOOKING, not by reading names. droha: "look for ones easy
    /// to distinguish at a glance." A name says nothing about silhouette or
    /// how something reads at twenty pixels, so the DevTools `spritegrid`
    /// command drew batches of candidates on screen at full size and at
    /// icon size, and three rounds of that threw out everything thin or
    /// low-contrast - a hammer, nails, callipers, a key, dice and scissors
    /// all vanish when small - and everything that collided with a
    /// neighbour on colour.
    ///
    /// The mapping is a metaphor, not a fact, which is why the letters stay
    /// underneath. Two coins changing hands for Swapping, a record for
    /// Rotating because it spins, a floppy disk for Gadgets, a sponge for
    /// Tidying, a box for Drawer, gem shards for Jigsaw because they fit
    /// together. Nobody would guess all twelve; with the label under each
    /// one nobody has to.
    /// </summary>
    private static readonly Dictionary<string, string> PillSprite =
        new(StringComparer.Ordinal)
        {
            ["Swapping"] = "Books",
            ["Stacking"] = "Cartridges",
            ["Ordering"] = "Pencils",
            ["Gadgets"] = "Lightbulb",
            ["Rotating"] = "Record",
            ["Grids"] = "GridTile",
            ["Tidying"] = "Breadtag",
            ["Containers"] = "EggCarton",
            ["Drawer"] = "Drawer",
            ["Sticking"] = "Stickers",
            ["Symmetry"] = "Wreath",
            ["Jigsaw"] = "Gingerbread",
        };

    /// <summary>
    /// Decoded icons, keyed as above. Built once on first use.
    /// </summary>
    private static readonly Dictionary<string, Sprite> _pillArt =
        new(StringComparer.Ordinal);
    private static bool _pillArtLoaded;

    /// <summary>
    /// Load the ability icons from PNGs shipped inside this DLL.
    ///
    /// SHIPPED RATHER THAN FOUND, and that is the whole reason this is
    /// simple. Six of the twelve are objects out of the puzzles that use
    /// each mechanic, and those are Addressable assets: the game loads a
    /// level's art when the level opens and releases it when it closes. So
    /// they are not in memory on the level select, which is the one screen
    /// that needs them - measured, six of twelve resolved there and the
    /// other six drew as lettered plates.
    ///
    /// The alternative was to take them as they passed and hold a reference,
    /// which meant the strip filled in gradually as a player happened to
    /// visit the right puzzles. droha: "can we just save those as png and
    /// use them in game? That way we don't have to do all this run around."
    ///
    /// WHERE EACH ONE CAME FROM. Six are badge elements, which are the small
    /// item pictures that sit on a badge rather than an assembled badge, so
    /// the badges themselves stay free for whatever they might become. Six
    /// are objects lifted out of a puzzle that uses that mechanic and
    /// nothing else, so the picture and the lock mean the same thing:
    ///
    ///   Books        Badge1-Books2        badge element
    ///   Cartridges   Badge2-NES           badge element
    ///   Pencils      Badge1-Pencils       badge element
    ///   Lightbulb    badge3-lightbulb     badge element
    ///   Record       Badge1-Record        badge element
    ///   Stickers     Badge1-Stickers      badge element
    ///   GridTile     1x1-1                Procedural Grid Puzzle
    ///   Breadtag     Breadtag-red         Breadtags
    ///   EggCarton    Carton-front copy    Fridge (Something Eggstra)
    ///   Drawer       Drawer-Top+Bottom    Tool Drawer (Drawer Chores)
    ///   Wreath       Wreath               Wreath (Good Tidings)
    ///   Gingerbread  GingerbreadMan       Cookies Jigsaw (Good Tidings)
    /// </summary>
    private static void LoadPillArt()
    {
        if (_pillArtLoaded) return;
        _pillArtLoaded = true;

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;

                using var buffer = new System.IO.MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (bytes.Length == 0) continue;

                // The size is replaced by LoadImage; 2x2 is just a placeholder
                // cheap enough not to matter.
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes)) continue;
                // Icons are drawn small and never rotated, so clamping and
                // bilinear keeps the edges clean without a mip chain.
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));

                // "ALTTLArchipelago.Icons.Books.png" -> "Books"
                var key = resource;
                var dot = key.LastIndexOf('.', key.Length - 5);
                if (dot >= 0) key = key.Substring(dot + 1);
                key = key.Substring(0, key.Length - 4);

                _pillArt[key] = sprite;
            }

            Plugin.Logger.LogInfo($"badges: loaded {_pillArt.Count} ability icon(s)");
        }
        catch (Exception e)
        {
            // The strip falls back to lettered plates, which is what it
            // looked like before there was any art at all.
            Plugin.Logger.LogWarning($"badges: could not load the icons: {e.Message}");
        }
    }

    // Sized against a 1280x720 window, which is where this was judged by
    // eye. The overlay scales a 1920 reference down by two thirds, so 54
    // here is about 36 real pixels. At the 34 it was first built with, the
    // art landed at 23 pixels and most of the point of having art was lost.
    private const float PillWidth = 58f;
    private const float PillHeight = 72f;
    private const float PillIcon = 54f;
    private const float PillGap = 4f;

    /// <summary>
    /// Extra air between the two rows. Without it the second row's art
    /// crowds the first row's letters and the block reads as one smear.
    /// </summary>
    private const float PillRowGap = 10f;

    /// <summary>
    /// Tiles per row. Twelve in a line is a long thin ribbon across the top
    /// of the screen; six by two is a block the eye takes in at once, and it
    /// leaves the corner it sits in looking like one thing rather than a
    /// banner. droha's call.
    /// </summary>
    private const int PillsPerRow = 6;

    /// <summary>Room reserved under the art for the three-letter label.</summary>
    private const float LabelHeight = 15f;

    /// <summary>
    /// The size to draw a mechanic's icon at, keeping its own proportions.
    ///
    /// The longest side gets PillIcon and the other follows, so a tall
    /// picture and a wide one occupy the same visual weight rather than the
    /// same square. Falls back to a square when the art has not loaded, so a
    /// missing icon still leaves a tile the right shape.
    /// </summary>
    private static Vector2 FitIcon(string ability)
    {
        if (PillSprite.TryGetValue(ability, out var key)
            && _pillArt.TryGetValue(key, out var sprite)
            && sprite != null)
        {
            var w = sprite.rect.width;
            var h = sprite.rect.height;
            if (w > 0f && h > 0f)
            {
                return w >= h
                    ? new Vector2(PillIcon, PillIcon * h / w)
                    : new Vector2(PillIcon * w / h, PillIcon);
            }
        }
        return new Vector2(PillIcon, PillIcon);
    }

    /// <summary>
    /// Which mechanics you hold, as a row of pills on the level select.
    ///
    /// droha: "show ability locks on the level select - icons for the twelve
    /// mechanics, so you can see at a glance which you hold." Until now the
    /// only way to know was to open a puzzle and see what was greyed out.
    ///
    /// ALL OF THEM, ALWAYS, with the locked ones dimmed rather than absent.
    /// The strip never changes width, so a pill does not move under your eye
    /// as things unlock, and you can see what is still to come rather than
    /// only what you have.
    ///
    /// Except the ones this seed does not have. SlotData.Abilities is the
    /// seed's own catalogue and a seed can carry fewer than twelve; drawing
    /// a pill for a mechanic no item will ever grant would be a lie in the
    /// other direction. Nothing is drawn at all when ability locks are off,
    /// for the same reason - twelve dim pills would describe a restriction
    /// that is not in force.
    ///
    /// Top LEFT: the connected tag and the goal counter own the top right,
    /// and the toasts come up from the bottom left.
    /// </summary>
    internal static void TickAbilityPills()
    {
        _sincePills += Time.unscaledDeltaTime;
        if (_sincePills < 0.5f) return;
        _sincePills = 0f;

        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;
        var state = Inventory.Abilities;

        if (!visible || state == null || !state.LocksEnabled)
        {
            if (_pills != null) _pills.gameObject.SetActive(false);
            return;
        }

        LoadPillArt();

        if (_pills == null && !BuildPills()) return;
        _pills!.gameObject.SetActive(true);

        for (int i = 0; i < _pillOrder.Count && i < _pillTiles.Count; i++)
        {
            var child = _pillTiles[i];
            if (child == null) continue;

            var held = state.Has(_pillOrder[i]);

            // The icon is tinted rather than swapped: Image.color multiplies,
            // so full white is the art as drawn and a dark grey is the same
            // art dimmed. One sprite, two states, no second asset to find.
            var icon = child.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                if (icon.sprite == null
                    && PillSprite.TryGetValue(_pillOrder[i], out var wanted)
                    && _pillArt.TryGetValue(wanted, out var art))
                {
                    icon.sprite = art;
                    icon.enabled = true;
                }
                icon.color = held ? Color.white : IconLocked;
            }

            // The plate shows only where there is no art yet, so a tile
            // without its picture still reads as held or locked.
            var plate = child.GetComponent<Image>();
            if (plate != null)
            {
                plate.enabled = icon == null || icon.sprite == null;
                if (plate.enabled) plate.color = held ? PillHeld : PillLocked;
            }

            var label = child.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = held
                    ? new Color(1f, 1f, 1f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.30f);
            }
        }
    }

    /// <summary>
    /// Build the strip once, from the seed's own ability catalogue.
    ///
    /// Ordered by Core.Abilities.All rather than by the dictionary, so the
    /// pills sit in the same places on every seed and a player learns where
    /// to look. Returns false if the pieces are not ready yet, and the poll
    /// tries again.
    /// </summary>
    private static bool BuildPills()
    {
        var root = Toasts.OverlayRoot;
        var seed = Plugin.Seed;
        if (root == null || seed == null) return false;

        _pillOrder.Clear();
        _pillTiles.Clear();
        foreach (var ability in ALTTLArchipelago.Core.Abilities.All)
        {
            if (seed.Abilities.Count > 0 && !seed.Abilities.ContainsKey(ability))
            {
                continue;                 // not in this seed's catalogue
            }
            _pillOrder.Add(ability);
        }
        if (_pillOrder.Count == 0) return false;

        var strip = new GameObject("ApAbilityStrip");
        strip.transform.SetParent(root, false);
        _pills = strip.AddComponent<RectTransform>();
        _pills.anchorMin = new Vector2(0f, 1f);
        _pills.anchorMax = new Vector2(0f, 1f);
        _pills.pivot = new Vector2(0f, 1f);
        // CENTRED IN THE GAP, not just clear of it. At the left edge the
        // strip sat on top of the level select's own X - droha: "the icons
        // cover up the X in the level select, if they can fit between that
        // and the chapter headers that would be nice... can they be centered
        // between the X and chapter 1 title?"
        //
        // Measured off the screen at 1280x720 and converted to these units,
        // which are a 1920 reference: the X ends near 187 and the chapter
        // heading starts near 690, so the gap runs 187 to 690 and its middle
        // is 438. Half the block sits either side of that.
        var rows = (_pillOrder.Count + PillsPerRow - 1) / PillsPerRow;
        var columns = Math.Min(_pillOrder.Count, PillsPerRow);
        const float gapStart = 187f;
        const float gapEnd = 690f;
        var width = columns * (PillWidth + PillGap);
        var x = (gapStart + gapEnd) * 0.5f - width * 0.5f;
        _pills.anchoredPosition = new Vector2(Mathf.Max(gapStart, x), -18f);

        _pills.sizeDelta = new Vector2(width, rows * (PillHeight + PillRowGap));

        for (int i = 0; i < _pillOrder.Count; i++)
        {
            var pill = new GameObject("ApPill_" + _pillOrder[i]);
            pill.transform.SetParent(_pills, false);

            var rt = pill.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(
                (i % PillsPerRow) * (PillWidth + PillGap),
                -(i / PillsPerRow) * (PillHeight + PillRowGap));
            rt.sizeDelta = new Vector2(PillWidth, PillHeight);

            // A backing plate, used ONLY when there is no sprite for this
            // mechanic. Disabled otherwise so the art sits on the menu
            // rather than in a box.
            var plate = pill.AddComponent<Image>();
            plate.raycastTarget = false;
            plate.color = PillLocked;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(rt, false);
            var irt = iconGo.AddComponent<RectTransform>();

            // BOTTOM OF THE BOX, NOT THE TOP, and sized to the art rather
            // than to a square. These pictures are not square - the egg
            // carton is five times wider than it is tall - and a square box
            // with preserveAspect centres the drawing inside it, so a flat
            // object floated in the middle of its tile with a gap under it.
            // droha: "the egg carton should be bottom aligned, not top."
            //
            // Fitting the rect to the art and pinning it to the bottom puts
            // every icon on one baseline, which is also how the objects sit
            // in the puzzles they came from.
            irt.anchorMin = new Vector2(0.5f, 0f);
            irt.anchorMax = new Vector2(0.5f, 0f);
            irt.pivot = new Vector2(0.5f, 0f);
            irt.anchoredPosition = new Vector2(0f, LabelHeight + 2f);
            irt.sizeDelta = FitIcon(_pillOrder[i]);

            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.color = IconLocked;
            icon.enabled = false;

            var labelGo = new GameObject("Text");
            labelGo.transform.SetParent(rt, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, LabelHeight);

            _pillTiles.Add(rt);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = PillText.TryGetValue(_pillOrder[i], out var abbrev)
                ? abbrev
                : _pillOrder[i].Substring(0, Math.Min(3, _pillOrder[i].Length))
                    .ToUpperInvariant();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 12f;
            label.raycastTarget = false;
        }

        Plugin.Logger.LogInfo(
            $"badges: ability strip built with {_pillOrder.Count} pill(s)");
        return true;
    }

    private static float _sinceDots;
    private static bool _dotsReported;
    private const string DotName = "ApDotState";

    /// <summary>
    /// Colour the overview strip along the bottom of the level select, so the
    /// shape of the run is readable without scrolling it.
    ///
    /// Reached through Transform ONLY, deliberately. The typed route is
    /// LevelsOverviewScrollbar.levelOverviewItems, which is index-parallel to
    /// LevelSelect.Levels and therefore to Track._plan - but the element type
    /// of that list could not be confirmed from the interop assembly, and a
    /// guess that fails to compile costs more than this feature is worth. Every
    /// API used here is UnityEngine.Transform, GameObject and Image, so the
    /// worst case is that the search finds nothing and the strip stays as it
    /// was.
    ///
    /// A tinted CHILD rather than a tint on the dot itself: the game repaints
    /// these from UpdateOverviewAppearance whenever anything unlocks, and
    /// recolouring its own Image would be undone at a moment we do not control
    /// - the lesson already recorded in docs/verification-log.md about
    /// recolouring the game's graphics. The overlay survives that, and the poll
    /// puts back anything a rebuild removed.
    /// </summary>
    internal static void TickOverviewDots()
    {
        _sinceDots += Time.unscaledDeltaTime;
        if (_sinceDots < 1f) return;
        _sinceDots = 0f;

        if (!Track.Active) return;

        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (progress == null || abilities == null || state == null) return;

        var track = Track.CampaignTrack();
        if (track == null || !track.gameObject.activeInHierarchy) return;

        var strip = FindOverviewStrip(track.transform);
        if (strip == null) return;

        if (!_dotsReported)
        {
            _dotsReported = true;
            Plugin.Logger.LogInfo(
                $"badges: overview strip '{strip.name}' has {strip.childCount} dot(s)");
        }

        FitStrip(strip);

        for (int i = 0; i < strip.childCount; i++)
        {
            var dot = strip.GetChild(i);
            if (dot == null) continue;

            var slot = Track.SlotAt(i);
            if (slot < 0)
            {
                RemoveChild(dot, DotName);
                continue;
            }

            var status = progress.StatusOf(
                slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

            // Mixed gets the SAME corner-to-corner split the card badge
            // uses, not a blended orange. The strip is a map of the badges
            // above it, and a third colour that appears nowhere on the cards
            // makes the reader learn two legends instead of one.
            var split = status == SlotStatus.Mixed;

            Color colour;
            switch (status)
            {
                case SlotStatus.Doable:   colour = Green; break;
                case SlotStatus.Locked:   colour = Red; break;
                case SlotStatus.Mixed:    colour = Color.white; break;   // the sprite carries it
                case SlotStatus.Complete: colour = FallbackStar; break;
                default: continue;
            }

            var existing = dot.Find(DotName);
            if (existing != null)
            {
                var img = existing.GetComponent<Image>();
                if (img != null)
                {
                    // The sprite decides which look this is, so it has to be
                    // set as well as the tint - a dot that went from Mixed to
                    // Doable would otherwise keep the diagonal and just recolour
                    // it, which reads as a solid green triangle.
                    var wanted = split ? DiagonalSprite() : null;
                    if (img.sprite != wanted) img.sprite = wanted;
                    if (img.color != colour) img.color = colour;
                }
                continue;
            }

            var go = new GameObject(DotName);
            go.transform.SetParent(dot, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            if (split) image.sprite = DiagonalSprite();
            image.color = colour;
            image.raycastTarget = false;

            go.transform.SetAsLastSibling();
        }
    }

    /// <summary>
    /// The strip's own scale before we touched it, so the fit is computed from
    /// a fixed baseline rather than by multiplying what is already there.
    ///
    /// Keyed by nothing - there is one strip - but reset whenever a different
    /// object turns up, because the level select is rebuilt and the Transform
    /// we measured may be gone.
    /// </summary>
    private static Transform? _fittedStrip;
    private static Vector3 _fittedBase = Vector3.one;

    /// <summary>
    /// Shrink the overview strip until it fits the screen.
    ///
    /// The game sizes this for its own widest campaign, about 85 cards. A run
    /// inserts a pack divider between blocks, so 79 puzzles builds 92 cards
    /// and the strip runs off the edge - reported from a full-length run. The
    /// default puzzle count now lands at 84 so this does nothing there, but a
    /// player is free to ask for 79 and should not be punished with a strip
    /// they cannot see the end of.
    ///
    /// SCALE, NOT SPACING. The mod has no handle on the layout - there is no
    /// layout component here, the game positions each dot itself - so the only
    /// lever that cannot fight it is the parent's scale.
    ///
    /// IDEMPOTENT, FROM A CACHED BASELINE. The game repaints these from
    /// UpdateOverviewAppearance at moments we do not control, and this runs on
    /// a one-second poll, so anything that multiplied the current value would
    /// shrink the strip to nothing over a minute of looking at the menu.
    /// </summary>
    private static void FitStrip(Transform strip)
    {
        try
        {
            if (!ReferenceEquals(_fittedStrip, strip))
            {
                _fittedStrip = strip;
                _fittedBase = strip.localScale;
            }

            var rect = strip.TryCast<RectTransform>();
            var parent = strip.parent == null
                ? null : strip.parent.TryCast<RectTransform>();
            if (rect == null || parent == null)
            {
                ReportFit($"no RectTransform (self={rect != null}, "
                          + $"parent={parent != null})");
                return;
            }

            // Measured from the dots, not from the strip's own rect: the rect
            // is whatever the game authored and need not bound its children.
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < strip.childCount; i++)
            {
                var dot = strip.GetChild(i);
                if (dot == null) continue;
                var dr = dot.TryCast<RectTransform>();
                if (dr == null) continue;
                var x = dr.anchoredPosition.x;
                if (x < min) min = x;
                if (x > max) max = x;
            }
            if (min > max)
            {
                ReportFit($"no dot positions to measure from {strip.childCount} child(ren)");
                return;
            }

            var span = (max - min) + DotAllowance;
            var room = RoomFor(strip);

            // Said out loud ONCE, whatever the verdict. A fit pass that only
            // logs when it acts is indistinguishable from one that never ran,
            // and that is exactly how the first attempt at this looked.
            ReportFit($"{strip.childCount} dots span {span:0} in {room:0}");

            if (span <= 0f || room <= 0f) return;

            // Only ever shrink. Widening a strip the game already fits would
            // be the mod inventing a layout rather than rescuing one.
            var wanted = Mathf.Clamp(room / span, MinStripScale, 1f);
            var target = _fittedBase * wanted;

            if ((strip.localScale - target).sqrMagnitude < 0.000001f) return;

            strip.localScale = target;
            Plugin.Logger.LogInfo(
                $"badges: overview strip scaled to {wanted:0.00} - "
                + $"{strip.childCount} dots span {span:0} in {room:0}");
        }
        catch (Exception e)
        {
            // Cosmetic, on a poll. A strip that stays too wide is far better
            // than an exception every second.
            Plugin.Logger.LogWarning($"badges: could not fit the strip: {e.Message}");
        }
    }

    /// <summary>
    /// Room for the last dot itself, which the leftmost-to-rightmost span of
    /// CENTRES leaves out. Approximate on purpose - being a few pixels
    /// conservative costs nothing and stops the final dot touching the edge.
    /// </summary>
    private const float DotAllowance = 24f;

    /// <summary>
    /// How small the strip may get. At the maximum 79 puzzles it needs about
    /// 0.9, so this floor is only reached by something unforeseen - and a
    /// strip too small to read is no more useful than one that overflows.
    /// </summary>
    private const float MinStripScale = 0.55f;

    /// <summary>
    /// How much width the strip actually has, in its own units.
    ///
    /// THE IMMEDIATE PARENT IS THE WRONG ANSWER and measuring it is why the
    /// first version of this never fired. The strip sits inside
    /// 'Levels Overview Scrollbar', whose width TRACKS ITS CONTENT: with 92
    /// dots it measured 2025 against a span of 2026, so the ratio was 0.9995
    /// and nothing ever looked like it overflowed. The real viewport is two
    /// levels up - 'Level Select' at 1920, the screen width.
    ///
    /// So take the NARROWEST ancestor. A content-sized box is by definition
    /// at least as wide as its content, so it can never be the smallest; the
    /// first fixed one above it wins. That holds without naming any of the
    /// game's objects, which matters because the names here are not ours.
    /// </summary>
    private static float RoomFor(Transform strip)
    {
        var room = float.MaxValue;
        var walk = strip.parent;
        for (int up = 0; up < 6 && walk != null; up++, walk = walk.parent)
        {
            var wr = walk.TryCast<RectTransform>();
            var width = wr == null ? 0f : wr.rect.width;
            if (width > 1f && width < room) room = width;
        }
        return room == float.MaxValue ? 0f : room;
    }

    private static string? _fitReported;

    /// <summary>One line per distinct measurement, so a 1s poll cannot spam.</summary>
    private static void ReportFit(string what)
    {
        if (_fitReported == what) return;
        _fitReported = what;
        Plugin.Logger.LogInfo($"badges: strip fit - {what}");
    }

    /// <summary>
    /// The container holding one dot per card, found by name because its type
    /// could not be pinned offline. Searched breadth-first from the track and
    /// then from the scene root above it, since the strip is a sibling of the
    /// track rather than a child.
    /// </summary>
    private static Transform? FindOverviewStrip(Transform from)
    {
        var root = from;
        while (root.parent != null) root = root.parent;

        return SearchForOverview(root, 0);
    }

    private static Transform? SearchForOverview(Transform node, int depth)
    {
        if (depth > 8) return null;

        for (int i = 0; i < node.childCount; i++)
        {
            var child = node.GetChild(i);
            if (child == null) continue;

            var name = child.name;
            if (name != null
                && name.IndexOf("overview", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0
                && child.childCount > 0)
            {
                return child;
            }

            var found = SearchForOverview(child, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static void RemoveChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) UnityEngine.Object.Destroy(child.gameObject);
    }

    private static float _sinceWhy;

    /// <summary>
    /// Answer a "why is this badge that colour" question left in a file.
    ///
    /// Diagnostic only. It exists because a badge that reads wrong is otherwise
    /// unarguable: the state is derived from the seed's requirements, what has
    /// been collected, packs held and abilities held, and only listing all four
    /// per location says which one is responsible.
    /// </summary>
    internal static void TickWhy(float dt)
    {
        // Off unless someone asked for it.
        //
        // This was a Path.Combine and a File.Exists every second, forever, in
        // the shipping mod - the DevTools command-file pattern copied into
        // player-facing code, for a diagnostic no shipping code reads. It is
        // genuinely useful when a badge looks wrong, so it is a switch rather
        // than a deletion.
        if (!Plugin.WhyProbeEnabled) return;

        _sinceWhy += dt;
        if (_sinceWhy < 1f) return;
        _sinceWhy = 0f;

        try
        {
            var path = System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath, "alttl-why.txt");
            if (!System.IO.File.Exists(path)) return;

            var text = System.IO.File.ReadAllText(path).Trim();
            System.IO.File.Delete(path);
            if (!int.TryParse(text, out var position)) return;

            Explain(position);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"why: {e.Message}");
        }
    }

    private static void Explain(int position)
    {
        var seed = Plugin.Seed;
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (seed == null || progress == null || abilities == null || state == null) return;

        var slot = Track.SlotAt(position);
        if (slot < 0)
        {
            Plugin.Logger.LogInfo($"why: position {position} is not a puzzle");
            return;
        }

        var entry = seed.Slots[slot];
        var status = progress.StatusOf(
            slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

        Plugin.Logger.LogInfo(
            $"why: position {position} = slot {slot} {entry.LevelId} "
            + $"#{entry.Instance}, badge {status}, {state.PacksHeld} packs held");

        foreach (var name in Checks.Router!.ForSlot(slot))
        {
            var collected = Checks.Ledger.IsCollected(name);
            var reachable = progress.IsReachable(name, state.PacksHeld, abilities);
            seed.Requirements.TryGetValue(name, out var need);

            Plugin.Logger.LogInfo(
                $"why:   {(collected ? "done" : "TODO")} "
                + $"{(reachable ? "reachable" : "BLOCKED  ")} {name}"
                + (need == null ? "" : $"  [needs {need.Packs} packs"
                    + (need.Abilities.Count > 0
                        ? ", " + string.Join(" + ", need.Abilities) : "")
                    + "]"));
        }
    }

    private static void Refresh()
    {
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (progress == null || abilities == null || state == null) return;

        // The CAMPAIGN track specifically. Archive and DLC menus have a
        // LevelsTrack too, and a plain search returns whichever exists - so
        // while the Archive page was open, badges were painted onto ITS cards
        // using our slot indices. That is what made a badge look stale until
        // you entered a level and came back: it was never our track.
        var track = Track.CampaignTrack();
        var items = track == null ? null : track.trackItems;
        if (items == null) return;

        // Nothing to repaint when no card is on screen.
        //
        // The level select is built once and cached, so trackItems stays
        // populated for the whole session - which meant this full recompute ran
        // every second while the player was inside a puzzle, looking at none of
        // it. Track.TickTrackIntegrity has always had this check; the badge
        // path never did.
        //
        // Safe to skip: the badges are repainted from scratch on the next tick
        // once the track is up again, which is the same thing that already
        // happens after the game rebuilds the icons.
        if (!track!.gameObject.activeInHierarchy) return;

        for (int i = 0; i < items.Count; i++)
        {
            var icon = items[i];
            if (icon == null) continue;

            // Track POSITION is not slot index. Pack dividers and the credits
            // card are cards too, so from the first divider onwards every badge
            // would report a different puzzle's state - and the dividers
            // themselves wore one.
            var slot = Track.SlotAt(i);
            if (slot < 0)
            {
                Remove(icon, BadgeName);
                Remove(icon, NameLabel);
                Remove(icon, DimName);
                RenumberDivider(icon, Track.PackNumberAt(i));
                continue;
            }

            var status = progress.StatusOf(
                slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

            // Skip only when it is unchanged AND still on screen: the game
            // destroys our badge when it rebuilds the track, and a cached
            // "already green" would leave that card bare forever.
            if (_shown.TryGetValue(slot, out var was) && was == status
                && icon.transform.Find(BadgeName) != null)
            {
                continue;
            }

            SetDim(icon, status == SlotStatus.Locked);
            SetBadge(icon, status);
            SetName(icon, slot);
            _shown[slot] = status;
        }
    }

    /// <summary>
    /// Put the right number on a pack divider.
    ///
    /// The dividers are the game's own five chapter cards, reused - a run can
    /// have far more packs than the game has chapters, and reusing one is much
    /// better than leaving fifty-eight cards in an unbroken block. What reuse
    /// costs is the number printed on the card: pack 6 borrows chapter 1's
    /// card and announces itself as "1".
    ///
    /// Found by VALUE rather than by name, because the chapter card's
    /// hierarchy is not documented and guessing an object name has already
    /// cost this project a day. Any label on the card whose text is just an
    /// integer is the number; nothing else on a chapter card is.
    ///
    /// Reapplied every refresh rather than once: the game rebuilds these when
    /// the track changes, and a number written once would revert the first
    /// time a pack arrives.
    /// </summary>
    private static void RenumberDivider(LevelIcon icon, int pack)
    {
        if (pack <= 0) return;

        try
        {
            var wanted = pack.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var text in icon.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null) continue;

                var current = text.text;
                if (string.IsNullOrWhiteSpace(current)) continue;
                if (current == wanted) continue;

                // Only a bare number. A chapter card also carries its title,
                // and rewriting that would replace "Lost Recipe" with "6".
                var trimmed = current.Trim();
                if (trimmed.Length > 2) continue;
                if (!int.TryParse(trimmed,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) continue;

                text.text = wanted;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: could not renumber a divider: {e.Message}");
        }
    }

    /// <summary>
    /// Grey a card out while its slot is still locked. See LockedVeil for why
    /// this is drawn rather than left to the game.
    /// </summary>
    private static void SetDim(LevelIcon icon, bool locked)
    {
        var existing = icon.transform.Find(DimName);

        if (!locked)
        {
            if (existing != null) Remove(icon, DimName);
            return;
        }

        if (existing != null) return;                // already veiled

        var go = new GameObject(DimName);
        go.transform.SetParent(icon.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = LockedVeil;
        // The click still has to reach the card: refusing it is the track's
        // job, and it now explains itself with a toast. Swallowing it here
        // would give the player silence instead.
        image.raycastTarget = false;

        go.transform.SetAsLastSibling();
    }

    internal static void SetBadge(LevelIcon icon, SlotStatus state)
    {
        Remove(icon, BadgeName);

        var root = new GameObject(BadgeName);
        root.transform.SetParent(icon.transform, false);

        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-6f, -6f);
        rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);

        switch (state)
        {
            case SlotStatus.Doable:
                AddFill(root, Green, 0f, 1f);
                break;
            case SlotStatus.Locked:
                AddFill(root, Red, 0f, 1f);
                break;
            case SlotStatus.Mixed:
                // Split corner to corner, top-right down to bottom-left: green
                // in the upper-left triangle, red in the lower-right.
                AddDiagonal(root);
                break;
            case SlotStatus.Complete:
                AddStar(root, icon);
                break;
        }

        // uGUI paints in hierarchy order, so last means on top of the artwork.
        // This is the whole reason the badge works where borderImage does not.
        root.transform.SetAsLastSibling();
    }

    /// <summary>
    /// The split badge, drawn from a generated sprite.
    ///
    /// uGUI has no triangle: Image draws a rect, and its Filled modes cut
    /// radially or along an axis, never corner to corner. So the diagonal is
    /// painted into a small texture once and reused. Rooted in a static
    /// because a Texture2D with no managed reference is collectable, and a
    /// collected texture leaves a blank badge rather than an error.
    /// </summary>
    private static Sprite? _diagonal;
    private static Texture2D? _diagonalTexture;

    private static void AddDiagonal(GameObject parent)
    {
        var go = new GameObject("Diagonal");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = DiagonalSprite();
        image.color = Color.white;      // the sprite carries the colours
        image.raycastTarget = false;
    }

    private static Sprite DiagonalSprite()
    {
        if (_diagonal != null) return _diagonal;

        // 64 for a 30-point badge, so the diagonal is sampled down rather than
        // up and does not come out as a staircase.
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Texture coordinates put y at the BOTTOM, so the line through the
        // top-right and bottom-left corners is y = x. Above it (y > x) is the
        // upper-left triangle.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Blend across a pixel and a half rather than stepping, so the
                // edge does not alias at badge size.
                float t = Mathf.Clamp01(((y - x) / 1.5f) + 0.5f);
                texture.SetPixel(x, y, Color.Lerp(Red, Green, t));
            }
        }
        texture.Apply();

        _diagonalTexture = texture;
        _diagonal = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f));
        return _diagonal;
    }

    /// <summary>One solid slab, spanning [from, to] horizontally.</summary>
    private static void AddFill(GameObject parent, Color colour, float from, float to)
    {
        var go = new GameObject("Fill");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(from, 0f);
        rt.anchorMax = new Vector2(to, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = colour;
        // Never eat a click meant for the card underneath.
        image.raycastTarget = false;
    }

    /// <summary>
    /// The game's own star, so "done" looks like the game's idea of done rather
    /// than a fifth invented colour.
    /// </summary>
    private static void AddStar(GameObject parent, LevelIcon icon)
    {
        var go = new GameObject("Star");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.raycastTarget = false;

        var (sprite, colour) = FindStar(icon);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = colour;
        }
        else
        {
            // No star art found - a plain gold square still reads as "done"
            // and is better than an invisible badge.
            image.color = colour;
        }
    }

    /// <summary>
    /// Borrow the star sprite and colour from the icon itself rather than
    /// shipping art. Prefers the completion star actually used on cards, and
    /// falls back to the locked-star icon, which every card carries.
    /// </summary>
    private static (Sprite?, Color) FindStar(LevelIcon icon)
    {
        // Search by SPRITE NAME, and be exact about the suffix. Every card
        // carries three star sprites and picking the wrong one is silent:
        //
        //   LTL-LevelSelect-Star-solved     the earned star   <- this one
        //   LTL-LevelSelect-Star-unsolved   an empty outline
        //   LTL-LevelSelect-Star-locked     a drab grey star
        //
        // Both earlier attempts got this wrong. Walking a fixed path landed on
        // "-locked" and drew a grey smudge; then "any star that is not locked"
        // landed on "-unsolved" and drew a hollow outline. "Contains solved" is
        // no good either, because "unsolved" contains "solved".
        Sprite? fallback = null;

        foreach (var image in icon.GetComponentsInChildren<Image>(true))
        {
            var sprite = image == null ? null : image.sprite;
            if (sprite == null) continue;

            var name = Str(() => sprite.name);
            if (name.IndexOf("star", StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (name.EndsWith("-solved", StringComparison.OrdinalIgnoreCase))
            {
                // Tinted gold, NOT drawn as-is. The sprite is a white
                // silhouette that the game tints at runtime, so drawing it
                // untinted gives a white star that disappears against the
                // pale cards - tried, and it vanished on four of six.
                //
                // The game's own earned-star tint could not be read off a live
                // card: it only draws that star on a SOLVED level, and writing
                // save data with solve: sets the found count without setting
                // solved. So this gold is ours, pending a real solved card to
                // sample.
                return (sprite, FallbackStar);
            }
            fallback ??= sprite;
        }

        // No earned-star art on this card. A star-shaped outline in gold still
        // reads as "done"; a grey one would read as "locked", the opposite of
        // the truth.
        return (fallback, FallbackStar);
    }

    private static void Remove(LevelIcon icon, string name)
    {
        var existing = icon.transform.Find(name);
        if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
    }

    /// <summary>
    /// The puzzle's name, under the card.
    ///
    /// The card carries no name of its own - its whole hierarchy holds only a
    /// locked-star count and a chapter number - so in a randomized track, where
    /// the art is the only clue and the order is not the one anyone knows,
    /// there is no way to find a particular puzzle or to match a hint to a
    /// card.
    ///
    /// Just the name. No lock state, no check counts: the badge already says
    /// all of that, and a name with data bolted onto it stops reading as a
    /// name.
    /// </summary>
    /// <summary>
    /// Whether the run holds this level in more than one slot. Answered once
    /// per level and cached, because it is asked for every card on every
    /// rebuild.
    /// </summary>
    private static bool IsRepeated(SlotData seed, string levelId)
    {
        if (!_repeatedBuilt)
        {
            _repeatedBuilt = true;
            var once = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in seed.Slots)
            {
                if (!once.Add(s.LevelId)) _repeated.Add(s.LevelId);
            }
        }

        return _repeated.Contains(levelId);
    }

    private static void SetName(LevelIcon icon, int slot)
    {
        try
        {
            var seed = Plugin.Seed;
            if (seed == null || slot < 0 || slot >= seed.Slots.Count) return;
            if (icon.transform.Find(NameLabel) != null) return;   // already there

            var entry = seed.Slots[slot];
            var text = DisplayNames.For(entry.LevelId);

            // Number EVERY instance once a level is in the run more than once,
            // so a repeated pair reads "#1" and "#2" rather than a bare name
            // followed by "#2" - which looks like the same card twice at a
            // glance, and was reported as exactly that.
            //
            // Cosmetic only, and deliberately NOT matched in the location
            // table: locations.instance_tag leaves the first instance
            // unnumbered, and it must keep doing so. Location ids are
            // positional (see the headers on locations.py and items.py), so
            // emitting "#1" there would rename every location in the
            // datapackage and invalidate every seed in flight. The label and
            // the AP name therefore differ by this one suffix, on purpose.
            if (IsRepeated(seed, entry.LevelId)) text += $" #{entry.Instance}";

            var go = new GameObject(NameLabel);
            go.transform.SetParent(icon.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            // Well clear of the card. The card GROWS when hovered, so a label
            // tucked close underneath ends up sitting on top of the art at the
            // exact moment the player is looking at it.
            rect.anchoredPosition = new Vector2(0f, -28f);
            rect.sizeDelta = new Vector2(60f, 54f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = FindFont(icon);
            label.text = text;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Top;
            label.enableWordWrapping = true;
            label.raycastTarget = false;       // never eat a click meant for the card
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 180);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: could not name a card: {e.Message}");
        }
    }

    /// <summary>Borrow a font from the card itself, so it matches the menu.</summary>
    private static TMPro.TMP_FontAsset? FindFont(LevelIcon icon)
    {
        foreach (var t in icon.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t != null && t.font != null) return t.font;
        }
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t != null && t.font != null) return t.font;
        }
        return null;
    }

    /// <summary>
    /// Read a value from the IL2CPP side without letting a throw escape.
    ///
    /// Reaching into a destroyed or half-built object throws rather than
    /// returning null, and a badge refresh is not worth taking the frame down
    /// for.
    /// </summary>
    private static string Str(Func<string?> read)
    {
        try { return read() ?? ""; }
        catch { return ""; }
    }
}
