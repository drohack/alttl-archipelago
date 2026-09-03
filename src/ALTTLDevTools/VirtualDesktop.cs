using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ALTTLDevTools;

/// <summary>
/// Moves the game's windows to a chosen Windows virtual desktop at startup.
///
/// Windows places a new window on whichever virtual desktop happens to be
/// active when it is created, so launching the game from a script drops it
/// wherever the user was standing at that moment. That is fine for a person
/// double-clicking an icon and useless for an automated test run.
///
/// It cannot be fixed from outside: IVirtualDesktopManager.MoveWindowToDesktop
/// returns E_ACCESSDENIED (0x80070005) for a window the calling process does
/// not own - measured, not assumed. But this plugin runs INSIDE the game, so
/// it owns both the Unity window and the BepInEx console and is allowed to
/// move them.
///
/// Only the documented, public COM interface is used. The undocumented
/// IVirtualDesktopManagerInternal would allow enumerating and switching
/// desktops too, but its GUIDs change between Windows builds and it is not
/// worth the breakage for this.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class VirtualDesktop
{
    private const string DesktopsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>
    /// Moves every visible top-level window of this process to the 1-based
    /// virtual desktop index. A value below 1 does nothing.
    /// </summary>
    internal static void MoveGameTo(int oneBasedIndex, Action<string> log)
    {
        if (oneBasedIndex < 1) return;

        try
        {
            var desktops = ReadDesktopIds();
            if (desktops.Count == 0)
            {
                log("virtual desktop: could not read the desktop list from the registry");
                return;
            }
            if (oneBasedIndex > desktops.Count)
            {
                log($"virtual desktop: asked for #{oneBasedIndex} but only "
                    + $"{desktops.Count} exist - leaving the window alone");
                return;
            }

            var target = desktops[oneBasedIndex - 1];
            var mgr = (IVirtualDesktopManager)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"))!)!;

            int moved = 0;
            foreach (var h in OwnWindows())
            {
                var g = target;
                int hr = mgr.MoveWindowToDesktop(h, ref g);
                if (hr == 0) moved++;
                else log($"virtual desktop: move failed for hwnd {h} (hr 0x{hr:X8})");
            }
            log($"virtual desktop: moved {moved} window(s) to #{oneBasedIndex} ({target})");
        }
        catch (Exception e)
        {
            // Never let a cosmetic convenience stop the plugin loading.
            log($"virtual desktop: {e.GetType().Name} - {e.Message}");
        }
    }

    /// <summary>
    /// The desktop GUIDs in Task View order. This registry value is not
    /// formally documented, but it is where the shell keeps the ordering and
    /// the public API offers no enumeration at all.
    /// </summary>
    private static List<Guid> ReadDesktopIds()
    {
        var result = new List<Guid>();
        var blob = RegBinary(DesktopsKey, "VirtualDesktopIDs");
        if (blob == null) return result;
        for (int i = 0; i + 16 <= blob.Length; i += 16)
        {
            var one = new byte[16];
            Array.Copy(blob, i, one, 0, 16);
            result.Add(new Guid(one));
        }
        return result;
    }

    /// <summary>
    /// Raise the GAME window above the BepInEx console.
    ///
    /// The console is created first and keeps the foreground, so the game
    /// starts hidden behind it and has to be clicked before it can be played -
    /// every launch, which during a testing session is every couple of minutes.
    ///
    /// Both windows belong to this process, so no focus-stealing rules apply
    /// and no AttachThreadInput dance is needed. The console is deliberately
    /// left open and merely behind: it is the whole point of a dev build.
    /// </summary>
    internal static void FocusGameWindow(Action<string> log)
    {
        try
        {
            var game = GameWindow();
            if (game == IntPtr.Zero)
            {
                log("could not find the game window to raise");
                return;
            }

            // Restore first in case it came up minimised, then raise and focus.
            ShowWindow(game, SW_SHOW);
            BringWindowToTop(game);
            SetForegroundWindow(game);
            log("raised the game window above the console");
        }
        catch (Exception e)
        {
            log($"could not raise the game window: {e.Message}");
        }
    }

    /// <summary>
    /// The Unity window, told apart from the BepInEx console by class name.
    /// The console is a real console window (ConsoleWindowClass); Unity's is
    /// UnityWndClass. Matching on the title would be fragile - both contain
    /// the game's name.
    /// </summary>
    private static IntPtr GameWindow()
    {
        IntPtr found = IntPtr.Zero;
        uint self = GetCurrentProcessId();
        var className = new StringBuilder(256);

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid != self) return true;

            className.Clear();
            GetClassNameW(h, className, className.Capacity);
            if (className.ToString().IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int count);

    private static IEnumerable<IntPtr> OwnWindows()
    {
        var mine = new List<IntPtr>();
        uint self = GetCurrentProcessId();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) mine.Add(h);
            return true;
        }, IntPtr.Zero);
        return mine;
    }

    // Read a REG_BINARY from HKCU without taking a dependency on
    // Microsoft.Win32.Registry, which is not in the net6.0 reference set.
    private static byte[]? RegBinary(string subKey, string value)
    {
        const int HKEY_CURRENT_USER = unchecked((int)0x80000001);
        const int RRF_RT_REG_BINARY = 0x00000008;
        int size = 0;
        int rc = RegGetValueW((IntPtr)HKEY_CURRENT_USER, subKey, value,
            RRF_RT_REG_BINARY, IntPtr.Zero, null, ref size);
        if (rc != 0 || size <= 0) return null;
        var buffer = new byte[size];
        rc = RegGetValueW((IntPtr)HKEY_CURRENT_USER, subKey, value,
            RRF_RT_REG_BINARY, IntPtr.Zero, buffer, ref size);
        return rc == 0 ? buffer : null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValueW(IntPtr hkey, string subKey, string value,
        int flags, IntPtr type, byte[]? data, ref int dataSize);

    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }
}
