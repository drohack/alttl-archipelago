using System;

namespace ALTTLDevTools;

/// <summary>
/// The game's own cursor, as the game sees it.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// "cursor": GameCursor's state, mode and whether its sprite is drawn.
    ///
    /// Written for the 0.4.2 playtest's lost cursor (backlog: after Tupperware
    /// Tower the cursor was gone inside the next level, fine in the pause menu,
    /// back after Restart). The game draws its cursor itself - an Image that
    /// follows the mouse - so a screenshot only shows it when a real pointer is
    /// over the window, which a scripted test never has. This reads the
    /// answer instead of looking for it. Read only.
    /// </summary>
    private static void ReportCursor()
    {
        var log = DevToolsPlugin.Log;
        var cursor = GameCursor.Input;
        if (cursor == null)
        {
            log.LogInfo("cursor: no GameCursor");
            return;
        }

        var sprite = cursor.cursorSprite;
        var drawn = sprite == null ? "no sprite"
            : $"sprite enabled={sprite.enabled} active={sprite.gameObject.activeInHierarchy} "
              + $"alpha={sprite.color.a:0.##}";
        var state = GameManager.Instance?.GameState;
        log.LogInfo($"cursor: state={cursor.cursorState} mode={cursor.cursorMode} "
                    + $"active={cursor.cursorActive} prev={cursor.m_prevState}/{cursor.m_prevMode} "
                    + $"interaction={cursor.IsInteractionState} "
                    + $"game={(state == null ? "?" : state.GetIl2CppType().Name)} {drawn}");
    }
}
