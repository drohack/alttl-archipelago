"""Snapshot the player's environment before a harness runs, and put it back.

A harness needs settings the player did not choose - a local server, a fixed
slot name, auto-connect on - and it writes them into the same BepInEx config
the player uses. It also drives the game through real saves, so it leaves run
files and completion data behind. All of that belongs to whoever plays this
install next.

CW4 paid for the missing half of this on 2026-09-05: a test battery left
Host = localhost and a dead port behind, the player launched a real session,
and the mod auto-connected to nothing while the message box filled with
timeouts. The mod was working perfectly; the rig had repointed it. Their
docs/in-game-testing.md now has a section titled "Put the player's environment
back when the harness exits".

It bit here too, smaller: TargetVirtualDesktop was changed in the DevTools
config to stop the game opening on another desktop, and the player's .run.json
was rewritten repeatedly to reset hint pages. Both were announced rather than
restored, and only because they happened to get mentioned.

Usage - the whole harness body goes inside the `with`:

    from harness_env import Environment

    with Environment("playthrough") as env:
        env.configure(Host="localhost", Port=38281, AutoConnect="true")
        ...

Restore runs on a normal exit, on an exception, and on Ctrl-C, because all
three unwind the `with`. It does NOT run if the process is killed outright,
which is why every snapshot stays on disk and

    py -3.13 tools/harness_env.py --restore-latest

puts back the most recent one. Check `--list` after any run that ended badly.

WHY RESTORE CLOSES THE GAME FIRST: BepInEx writes its config files on
shutdown, from the values held in memory. Restoring the file under a running
game means the game overwrites the restore seconds later, and the harness
prints "restored" having done nothing at all.
"""
import atexit
import glob
import os
import re
import shutil
import signal
import subprocess
import sys
import time

GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
CONFIG_DIR = os.path.join(GAME, "BepInEx", "config")

#: Steam's App ID for this game, from steamapps/appmanifest_1629520.acf.
STEAM_APP_ID = "1629520"


def ensure_no_steam_relaunch():
    """Stop the game restarting itself through Steam on every launch.

    Running the exe directly makes the Steam DRM stub call
    SteamAPI_RestartAppIfNecessary, which relaunches the game through Steam and
    exits the process you started. What you SEE is the window opening, closing
    and opening again, which looks like a crash on startup; what a harness sees
    is a process it can no longer track and a log written by a different one.

    Measured: launching once produced PID 70840, replaced four seconds later by
    PID 76964. With this file present, one PID and no relaunch.

    steam_appid.txt is the documented way to say "this app is already running
    as the right app" - Valve's own answer for developers, and the same thing
    every modding guide reaches for. It changes nothing else: the Steam API
    still initialises, so the overlay and cloud behave as before.

    Written rather than snapshotted-and-restored on purpose. It fixes a real
    annoyance for whoever plays this install next, and it is one line that
    Steam itself ignores.
    """
    path = os.path.join(GAME, "steam_appid.txt")
    try:
        if os.path.isfile(path):
            with open(path, encoding="utf-8") as f:
                if f.read().strip() == STEAM_APP_ID:
                    return False
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(STEAM_APP_ID)
        return True
    except OSError as e:
        print(f"WARNING: could not write steam_appid.txt ({e}); the game will "
              f"restart itself through Steam on launch", flush=True)
        return False


def _find_save_dir():
    """Unity's save folder, found rather than spelled out.

    It is <LocalLow>/<company>/<product>, and both halves come from the Unity
    project settings, not from anything visible. The first version of this
    hardcoded "Max Inferno/A Little to the Left" - the company as it is written
    everywhere a human reads it - and the real folder is "maxinferno/A Little
    To The Left". Nothing failed: the glob matched no files, the snapshot
    contained the two configs, and the harness printed that it had protected
    the environment.
    """
    root = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow")
    hits = [d for d in glob.glob(os.path.join(root, "*", "*Little*"))
            if os.path.isdir(d)]
    return hits[0] if hits else ""


SAVE_DIR = _find_save_dir()

#: Where snapshots live. Outside the repo would be tidier, but a snapshot that
#: is hard to find is a snapshot nobody restores from after a hard kill.
SNAPSHOT_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             ".harness-snapshots")

#: The two plugin configs. BepInEx.cfg is deliberately NOT here: no harness
#: writes it, and snapshotting a file nothing touches only creates a way for a
#: buggy restore to damage it.
CONFIG_FILES = ("droha.alttl.archipelago.cfg", "droha.alttl.devtools.cfg")

#: Everything the game and the mod persist. A glob rather than a list because
#: the set grows - save1.json (the campaign, which the mod must never touch),
#: save_ap_<slot>_<seed>.json, the matching .run.json, and whatever the slot
#: cache turns out to be called. A harness that creates a NEW run file is
#: covered by the same mechanism: it did not exist at snapshot time, so restore
#: deletes it.
SAVE_GLOB = "*.json*"

#: How many old snapshots to keep. Enough to recover from a hard kill that went
#: unnoticed for a few runs, few enough that the folder stays readable.
KEEP_SNAPSHOTS = 8


def close_game(quiet=True):
    """Close the game and wait for Windows to release its file handles.

    Same approach as tools/deploy.sh, and for a related reason: there the open
    process blocks a DLL copy, here it would overwrite a restored config on its
    way out.
    """
    subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "Get-Process | Where-Object {$_.ProcessName -like '*Little*'} "
         "| Stop-Process -Force"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    for _ in range(10):
        out = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "@(Get-Process | Where-Object {$_.ProcessName -like '*Little*'})"
             ".Count"],
            capture_output=True, text=True, check=False)
        if out.stdout.strip().startswith("0"):
            return True
        time.sleep(1)
    if not quiet:
        print("WARNING: the game is still running; a restore may be "
              "overwritten when it exits", flush=True)
    return False


def _require_save_dir():
    """Refuse to run rather than sweep the wrong folder.

    restore_snapshot deletes save-folder files that are not in the manifest.
    With SAVE_DIR empty that glob is relative, so it would resolve against the
    working directory - the repo - and delete json files from it. A harness
    that cannot find the save folder must stop, not carry on protecting half
    the environment and reporting success.
    """
    if not SAVE_DIR or not os.path.isdir(SAVE_DIR):
        raise SystemExit(
            "harness_env: could not find the game's save folder under "
            f"{os.path.join(os.path.expanduser('~'), 'AppData', 'LocalLow')}. "
            "Launch the game once so Unity creates it, or fix _find_save_dir.")


def _files_to_snapshot():
    """(source path, name inside the snapshot) for everything we protect."""
    _require_save_dir()
    out = []
    for name in CONFIG_FILES:
        path = os.path.join(CONFIG_DIR, name)
        if os.path.isfile(path):
            out.append((path, os.path.join("config", name)))
    for path in sorted(glob.glob(os.path.join(SAVE_DIR, SAVE_GLOB))):
        if os.path.isfile(path):
            out.append((path, os.path.join("save", os.path.basename(path))))
    return out


def _destination(rel):
    """Map a path inside a snapshot back to where it came from."""
    kind, name = rel.replace("\\", "/").split("/", 1)
    return os.path.join(CONFIG_DIR if kind == "config" else SAVE_DIR, name)


def take_snapshot(label):
    stamp = time.strftime("%Y%m%d-%H%M%S")
    safe = re.sub(r"[^A-Za-z0-9_.-]", "-", label)
    root = os.path.join(SNAPSHOT_ROOT, f"{stamp}-{safe}")
    os.makedirs(os.path.join(root, "config"), exist_ok=True)
    os.makedirs(os.path.join(root, "save"), exist_ok=True)

    names = []
    for src, rel in _files_to_snapshot():
        shutil.copy2(src, os.path.join(root, rel))
        names.append(rel)

    # The manifest is what lets restore DELETE files created during the run.
    # Without it a harness that starts a new run leaves its save behind, and
    # the next offline launch comes up on a slot nobody chose.
    with open(os.path.join(root, "MANIFEST"), "w", encoding="utf-8") as f:
        f.write("\n".join(names) + "\n")

    _prune()
    return root


def restore_snapshot(root, quiet=False):
    """Put every snapshotted file back and delete anything the run created."""
    _require_save_dir()
    manifest = os.path.join(root, "MANIFEST")
    if not os.path.isfile(manifest):
        print(f"WARNING: {root} has no MANIFEST; not restoring", flush=True)
        return 0, 0

    with open(manifest, encoding="utf-8") as f:
        known = [line.strip() for line in f if line.strip()]

    put_back = 0
    for rel in known:
        src = os.path.join(root, rel)
        dst = _destination(rel)
        if not os.path.isfile(src):
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
        put_back += 1

    # Anything matching the same patterns that is NOT in the manifest appeared
    # during the run. Only the save folder is swept: a new plugin config means
    # a plugin was installed, which is not the harness's doing and not its
    # business to undo.
    kept = {os.path.basename(r.replace("\\", "/")) for r in known
            if r.replace("\\", "/").startswith("save/")}
    removed = 0
    for path in glob.glob(os.path.join(SAVE_DIR, SAVE_GLOB)):
        if os.path.basename(path) not in kept and os.path.isfile(path):
            os.remove(path)
            removed += 1

    if not quiet:
        print(f"environment restored: {put_back} file(s) put back, "
              f"{removed} created by the run removed", flush=True)
    return put_back, removed


def _prune():
    try:
        dirs = sorted(d for d in os.listdir(SNAPSHOT_ROOT)
                      if os.path.isdir(os.path.join(SNAPSHOT_ROOT, d)))
    except OSError:
        return
    for old in dirs[:-KEEP_SNAPSHOTS]:
        shutil.rmtree(os.path.join(SNAPSHOT_ROOT, old), ignore_errors=True)


def set_config(name, settings):
    """Rewrite `key = value` lines in a BepInEx config, in place.

    Line-oriented on purpose. A config file is also documentation - every
    setting carries its type, default and a paragraph explaining it - and
    regenerating one from a dict throws all of that away. This only touches
    the assignment lines the caller names.
    """
    path = os.path.join(CONFIG_DIR, name)
    if not os.path.isfile(path):
        print(f"WARNING: no config at {path}; nothing configured", flush=True)
        return []

    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()

    wanted = dict(settings)
    changed = []
    for i, line in enumerate(lines):
        m = re.match(r"^(\w+)\s*=\s*(.*)$", line)
        if not m or m.group(1) not in wanted:
            continue
        key = m.group(1)
        value = str(wanted.pop(key))
        if m.group(2).strip() != value:
            lines[i] = f"{key} = {value}"
            changed.append(f"{key}={value}")

    if wanted:
        # Loud, because a silently ignored setting means the harness is not
        # testing what it says it is. A key can go missing when the plugin
        # renames it, and the run would otherwise look fine.
        print(f"WARNING: {name} has no line(s) for "
              f"{', '.join(sorted(wanted))}", flush=True)

    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    return changed


class Environment:
    """Snapshot on entry, restore on exit. See the module docstring."""

    def __init__(self, label, close_on_restore=True):
        self.label = label
        self.close_on_restore = close_on_restore
        self.root = None
        self._done = False

    def __enter__(self):
        # Deliberately does NOT close the game. Several harnesses drive a
        # session that is already up, and closing it here would break them for
        # no gain: BepInEx saves a config entry the moment it is set, so the
        # file on disk already matches what the player is running with,
        # in-game edits to Host and SlotName included.
        #
        # The close belongs on the RESTORE side, where it is load-bearing.
        self.root = take_snapshot(self.label)
        n = sum(1 for _ in _files_to_snapshot())
        print(f"-- environment snapshotted: {n} file(s) -> "
              f"{os.path.basename(self.root)} --", flush=True)

        # Belt and braces for the paths that do not unwind the `with`:
        # SIGTERM, and os._exit-style teardown. SIGINT already raises
        # KeyboardInterrupt, which does unwind.
        atexit.register(self._restore)
        try:
            signal.signal(signal.SIGTERM, self._on_signal)
        except (ValueError, OSError):
            pass                       # not the main thread; atexit still runs
        return self

    def __exit__(self, exc_type, exc, tb):
        self._restore()
        return False

    def _on_signal(self, signum, frame):
        raise SystemExit(f"terminated by signal {signum}")

    def _restore(self):
        if self._done or self.root is None:
            return
        self._done = True
        if self.close_on_restore:
            close_game()
        restore_snapshot(self.root)

    # -- convenience wrappers, so a harness never names a config file --------

    def configure(self, **settings):
        """Point the mod at a test server. Restored on exit like everything.

        Takes effect at the NEXT launch. The plugin reads its config once at
        startup, so a harness driving a session that is already running has to
        configure before it launches the game, not after.
        """
        changed = set_config("droha.alttl.archipelago.cfg", settings)
        if changed:
            print(f"-- mod config: {', '.join(changed)} --", flush=True)

    def configure_devtools(self, **settings):
        changed = set_config("droha.alttl.devtools.cfg", settings)
        if changed:
            print(f"-- devtools config: {', '.join(changed)} --", flush=True)


def _cli(argv):
    if "--list" in argv:
        try:
            dirs = sorted(os.listdir(SNAPSHOT_ROOT))
        except OSError:
            dirs = []
        if not dirs:
            print("no snapshots")
            return 0
        for d in dirs:
            manifest = os.path.join(SNAPSHOT_ROOT, d, "MANIFEST")
            n = 0
            if os.path.isfile(manifest):
                with open(manifest, encoding="utf-8") as f:
                    n = sum(1 for line in f if line.strip())
            print(f"{d}  ({n} file(s))")
        return 0

    if "--restore-latest" in argv:
        try:
            dirs = sorted(d for d in os.listdir(SNAPSHOT_ROOT)
                          if os.path.isdir(os.path.join(SNAPSHOT_ROOT, d)))
        except OSError:
            dirs = []
        if not dirs:
            print("no snapshots to restore from")
            return 1
        close_game()
        root = os.path.join(SNAPSHOT_ROOT, dirs[-1])
        print(f"restoring {dirs[-1]}", flush=True)
        restore_snapshot(root)
        return 0

    print(__doc__.strip())
    print("\nusage: harness_env.py [--list | --restore-latest]")
    return 0


if __name__ == "__main__":
    raise SystemExit(_cli(sys.argv[1:]))
