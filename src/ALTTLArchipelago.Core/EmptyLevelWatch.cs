namespace ALTTLArchipelago.Core;

/// <summary>
/// What the "LEVEL LOADED EMPTY" watch treats as a puzzle that came up with
/// nothing in it. A chapter card and the credits never have objects or
/// controllers, so they never count: the base gate of 2026-09-25 flagged
/// 01__Chapter_HomeSweetHome at the title after a Skip's fallback went back
/// to the track, and the watch also shows the player a "failed to load" toast.
/// </summary>
public static class EmptyLevelWatch
{
    public static bool LoadedEmpty(bool isChapter, bool isCredits, int controllers, int objects)
        => !isChapter && !isCredits && controllers == 0 && objects == 0;
}
