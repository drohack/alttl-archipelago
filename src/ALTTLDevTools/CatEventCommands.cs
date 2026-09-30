using System;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// `catevent[:info|grab|trigger|try|climb]` - the running level's own cat.
///
/// Reports every CatGrab and CatClimb under the level (their Interlude config
/// and state), then with an argument starts one out of sequence: `grab` calls
/// CatGrab.DoGrab(), `trigger` the Interlude's OnTrigger(), `try` its
/// TryDoInterlude(), `climb` CatClimb.StartCatClimb at the cat's own spot.
/// For the next 15 s every change of the cat's state is logged with the time
/// since the call (`catevent: +1.23s ...`), so one run says what the call did
/// and how long the cat takes: what the Cat Trap needs before it plays a
/// level's own cat.
/// </summary>
public partial class DevToolsBehaviour
{
    private static CatGrab? _catGrab;
    private static CatClimb? _catClimb;
    private static float _catSince;
    private static float _catUntil;
    private static string _catLast = "";

    private static void CatEvent(string arg)
    {
        var how = string.IsNullOrWhiteSpace(arg) ? "info" : arg.Trim().ToLowerInvariant();
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null)
        {
            DevToolsPlugin.Log.LogInfo("catevent: no level is running");
            return;
        }

        var grabs = level.GetComponentsInChildren<CatGrab>(true);
        var climbs = level.GetComponentsInChildren<CatClimb>(true);
        DevToolsPlugin.Log.LogInfo(
            $"catevent: level={Str(() => li!.LevelId)} grabs={grabs.Length} climbs={climbs.Length}");
        foreach (var g in grabs) DevToolsPlugin.Log.LogInfo("catevent:   grab  " + Config(g) + " " + GrabState(g));
        foreach (var c in climbs) DevToolsPlugin.Log.LogInfo("catevent:   climb " + Config(c) + " " + ClimbState(c));

        _catGrab = grabs.Length > 0 ? grabs[0] : null;
        _catClimb = climbs.Length > 0 ? climbs[0] : null;
        Interlude? target = _catGrab != null ? _catGrab : _catClimb;
        if (how == "info") return;
        if (target == null)
        {
            DevToolsPlugin.Log.LogInfo("catevent: nothing to start - the level has no CatGrab or CatClimb");
            return;
        }

        _catSince = Time.unscaledTime;
        _catUntil = _catSince + 15f;
        _catLast = "";
        try
        {
            switch (how)
            {
                case "grab" when _catGrab != null:
                    _catGrab.DoGrab();
                    break;
                case "climb" when _catClimb != null:
                    _catClimb.StartCatClimb(_catClimb.cat == null
                        ? Vector2.zero
                        : (Vector2)_catClimb.cat.position);
                    break;
                case "trigger":
                    target.OnTrigger();
                    break;
                case "try":
                    DevToolsPlugin.Log.LogInfo($"catevent: TryDoInterlude returned {B(target.TryDoInterlude())}");
                    break;
                default:
                    DevToolsPlugin.Log.LogInfo($"catevent: '{how}' does not apply here - info, grab, trigger, try, climb");
                    _catUntil = 0f;
                    return;
            }
            DevToolsPlugin.Log.LogInfo($"catevent: {how} called on '{Str(() => target.gameObject.name)}', watching 15s");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"catevent: {how} threw {e.GetType().Name}: {e.Message}");
            _catUntil = 0f;
        }
    }

    private static void TickCatEvent()
    {
        if (_catUntil <= 0f) return;
        var t = Time.unscaledTime - _catSince;
        if (Time.unscaledTime > _catUntil)
        {
            DevToolsPlugin.Log.LogInfo($"catevent: +{t:0.00}s done watching, last {_catLast}");
            _catUntil = 0f;
            return;
        }
        string now;
        try
        {
            now = _catGrab != null ? GrabState(_catGrab)
                : _catClimb != null ? ClimbState(_catClimb)
                : "gone";
            var li = GameManager.Instance.levelManager.ActiveLevelInterface;
            now += li == null ? " level=none"
                : $" loaded={B(li.LevelIsLoaded)} transitioning={B(li.IsTransitioning)}";
        }
        catch
        {
            now = "gone (the level was torn down)";
        }
        if (now == _catLast) return;
        _catLast = now;
        DevToolsPlugin.Log.LogInfo($"catevent: +{t:0.00}s {now}");
    }

    private static string Config(Interlude i) =>
        $"'{Str(() => i.gameObject.name)}' active={B(i.gameObject.activeInHierarchy)}"
        + $" trigger={Str(() => i.triggerEvent.ToString())} solution='{Str(() => i.requiredSolutionId)}'"
        + $" chance={i.triggerChance:0.##} startDelay={i.eventStartDelay:0.##} endDelay={i.eventEndDelay:0.##}"
        + $" deactivatesLevel={B(i.deactivateLevelOnInterlude)} completesLevel={B(i.doCompleteLevel)}"
        + $" hidesCursor={B(i.hideCursorDuringInterlude)} bypass={B(i.Bypass)}";

    private static string InterludeState(Interlude i) =>
        $"queued={B(i.eventQueued)} triggered={B(i.eventDidTrigger)} performed={B(i.eventDidPerform)}"
        + $" completed={B(i.eventDidComplete)} inProgress={B(i.InterludeInProgress)}";

    private static string GrabState(CatGrab g) =>
        InterludeState(g)
        + $" type={Str(() => g.grabType.ToString())} reaching={B(g.isReaching)} grabbing={B(g.isGrabbing)}"
        + $" attempts={g.numGrabAttempts}/{g.maxGrabAttempts:0} autoRetrigger={B(g.autoRetrigger)}"
        + $" clickToCancel={B(g.clickToCancel)} objects={(g.grabObjects == null ? 0 : g.grabObjects.Count)}"
        + $" holding='{Str(() => g.m_grabbedObject == null ? "" : g.m_grabbedObject.name)}'"
        + $" arm={Str(() => g.catArm == null ? "none" : g.catArm.enabled ? "shown" : "hidden")}";

    private static string ClimbState(CatClimb c) =>
        InterludeState(c)
        + $" climbing={B(c.catIsClimbing)} state='{Str(() => c.currentState)}'"
        + $" at={Str(() => c.cat == null ? "none" : c.cat.position.ToString())}"
        + $" shown={Str(() => c.catRenderer == null ? "none" : c.catRenderer.enabled.ToString())}";
}
