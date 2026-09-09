using System;
using System.IO;

namespace ALTTLArchipelago;

/// <summary>
/// Remember which one-off prompts the player has answered, across runs.
///
/// The game records "you have seen the colour assist prompt" and friends as
/// booleans in the SAVE. That is fine in vanilla, where there is one save. A
/// run gets its own file, so every new seed looked like a fresh install and
/// asked again - and this project generates a lot of seeds.
///
/// SaveRedirect already copies the flags forward from the campaign save, which
/// helps a player who answered them before installing the mod. droha's campaign
/// save has every flag false, so there was nothing to copy and the prompts came
/// back on every seed: "shouldn't that be gone once i select the option?"
///
/// So the mod keeps its own note instead. Answering a prompt in ANY run is
/// remembered here and applied to every run after it.
///
/// WHY NOT JUST WRITE THE CAMPAIGN SAVE. Because a run must never open the
/// player's own save for writing. That guarantee is the whole point of the save
/// redirect, and "these flags are harmless" is exactly the argument that would
/// erode it. A file the mod owns costs one small class and keeps the rule
/// intact.
/// </summary>
internal static class PromptMemory
{
    private static string Path => System.IO.Path.Combine(
        BepInEx.Paths.BepInExRootPath, "alttl-prompts.json");

    internal readonly struct Answered
    {
        internal Answered(bool colour, bool solutions, bool dtSelector,
                          bool dtStreak, bool dtBadges)
        {
            Colour = colour;
            Solutions = solutions;
            DtSelector = dtSelector;
            DtStreak = dtStreak;
            DtBadges = dtBadges;
        }

        internal bool Colour { get; }
        internal bool Solutions { get; }
        internal bool DtSelector { get; }
        internal bool DtStreak { get; }
        internal bool DtBadges { get; }

        internal bool Any => Colour || Solutions || DtSelector || DtStreak || DtBadges;

        internal Answered Or(Answered o) => new Answered(
            Colour || o.Colour,
            Solutions || o.Solutions,
            DtSelector || o.DtSelector,
            DtStreak || o.DtStreak,
            DtBadges || o.DtBadges);
    }

    /// <summary>
    /// Hand-rolled rather than a serializer, because five bools do not justify
    /// a dependency and the file is meant to be readable if anyone wonders why
    /// a prompt stopped appearing.
    /// </summary>
    internal static Answered Load()
    {
        try
        {
            if (!File.Exists(Path)) return default;
            var text = File.ReadAllText(Path);
            return new Answered(
                Has(text, "colourAssist"),
                Has(text, "multipleSolutions"),
                Has(text, "dailySelector"),
                Has(text, "dailyStreak"),
                Has(text, "dailyBadges"));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"prompts: could not read the memory: {e.Message}");
            return default;
        }
    }

    private static bool Has(string text, string key)
    {
        var at = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (at < 0) return false;
        var colon = text.IndexOf(':', at);
        if (colon < 0) return false;
        var tail = text.Substring(colon + 1).TrimStart();
        return tail.StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }

    internal static void Save(Answered a)
    {
        try
        {
            File.WriteAllText(Path,
                "{\n"
                + "  \"_comment\": \"One-off prompts the player has answered. The mod "
                + "keeps this so a new seed does not ask again; the campaign save is "
                + "never written to.\",\n"
                + $"  \"colourAssist\": {Low(a.Colour)},\n"
                + $"  \"multipleSolutions\": {Low(a.Solutions)},\n"
                + $"  \"dailySelector\": {Low(a.DtSelector)},\n"
                + $"  \"dailyStreak\": {Low(a.DtStreak)},\n"
                + $"  \"dailyBadges\": {Low(a.DtBadges)}\n"
                + "}\n");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"prompts: could not write the memory: {e.Message}");
        }
    }

    private static string Low(bool b) => b ? "true" : "false";

    /// <summary>What the save in memory currently says.</summary>
    internal static Answered FromSave()
    {
        try
        {
            var d = SaveSystem.data;
            if (d == null) return default;
            return new Answered(
                d.seenColourAssistPrompt,
                d.seenMultipleSolutionTutorial,
                d.seenDTSelectorPrompt,
                d.seenDTStreakTutorial,
                d.seenDTAllBadgesEarnedPrompt);
        }
        catch
        {
            return default;
        }
    }

    private static float _since;

    /// <summary>
    /// Notice a prompt being answered and remember it.
    ///
    /// A slow poll rather than a hook: the game answers these in several
    /// places, and the flag landing in SaveSystem.data is the one event they
    /// all share. Five booleans every couple of seconds costs nothing.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (!Track.Active) return;

        _since += dt;
        if (_since < 2f) return;
        _since = 0f;

        var now = FromSave();
        if (!now.Any) return;

        var known = Load();
        var merged = known.Or(now);

        if (merged.Colour == known.Colour && merged.Solutions == known.Solutions
            && merged.DtSelector == known.DtSelector
            && merged.DtStreak == known.DtStreak
            && merged.DtBadges == known.DtBadges)
        {
            return;                       // nothing new
        }

        Save(merged);
        Plugin.Logger.LogInfo(
            "prompts: remembered an answered prompt; later runs will not ask again");
    }
}
