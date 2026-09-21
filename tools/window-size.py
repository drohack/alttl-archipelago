"""Make the game open in a small window for harness runs.

WHY THIS IS NOT ONE LINE. Nothing stores a width and height. The save
holds `playerPrefs.resolution`, an INDEX into the GAME's own resolution
list - the one SettingsMenu builds for its dropdown, not Unity's
Screen.resolutions, which had 135 entries here. The list is rebuilt from
whichever monitor the game opened on, so the same number means different
sizes on different displays. droha: "the game changes the resolution
list depending on what monitor opened it, so a number doesn't help me
here."

So the index is DISCOVERED from the running game, never guessed:

    py -3.13 tools/window-size.py            # discover and report
    py -3.13 tools/window-size.py --apply    # discover, then set it

AND WRITING THE REGISTRY DOES NOT WORK. That was tried: the game applies
its own playerPrefs after startup and overwrote the Screenmanager keys,
while the harness printed "windowed 1280x720" on the strength of having
written them. The save is what the game obeys.

The change is restored by harness_env's snapshot like everything else -
the display choice belongs to whoever plays next.
"""
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
import harness_env as env

#: What we would like. The nearest entry in the game's own list wins;
#: an exact match is not required and not expected on every display.
WANT = env.TEST_SIZE


def discover(log):
    """Ask the running game for its own resolution list."""
    log.new()
    e2e.dev("resolutions", 1.0)
    out = log.wait(["resolutions: "], 20, 6, "the resolution list")
    time.sleep(1.5)
    out += log.new()
    return out


def game_list(text):
    """[(index, width, height)] from the GAME's list, not Unity's.

    DumpGameList prints the SettingsMenu list, which is the one
    playerPrefs.resolution indexes. Unity's own list is printed too and
    is NOT what a saved choice means, so the two must not be mixed.
    """
    out = []
    in_game_list = False
    for line in text.splitlines():
        if "game list" in line.lower() or "settingsmenu" in line.lower():
            in_game_list = True
        m = re.search(r"\[(\d+)\]\s+(\d+)x(\d+)", line)
        if m and in_game_list:
            out.append((int(m.group(1)), int(m.group(2)), int(m.group(3))))
    return out


def _saved_index():
    """What playerPrefs currently asks for, or None."""
    for path in env._save_files():
        data = env._decode_save(path)
        if isinstance(data, dict):
            prefs = data.get("playerPrefs")
            if isinstance(prefs, dict) and "resolution" in prefs:
                return prefs["resolution"]
    return None


def nearest(entries, want):
    """The index whose size is closest to `want`, preferring not larger."""
    if not entries:
        return None
    w, h = want

    def cost(entry):
        _i, ew, eh = entry
        return (abs(ew - w) + abs(eh - h)) + (0 if ew <= w and eh <= h else 1)

    return min(entries, key=cost)


def main():
    apply = "--apply" in sys.argv
    with env.Environment("window-size") as _e:
        env.close_game()
        log = e2e.Log()
        log.before_launch()
        env.ensure_no_steam_relaunch()
        subprocess.Popen([e2e.EXE], cwd=e2e.GAME)

        up = log.wait(["A Little To The Left Archipelago loaded",
                       "Dev Tools"], 120, 2, "the game")
        if not up.strip():
            env.close_game()
            sys.exit("the game never started")
        time.sleep(3.0)

        text = discover(log)
        entries = game_list(text)
        if not entries:
            # Fall back to EVERY [i] WxH line, and say so - better a
            # noisy answer than a confident wrong one.
            entries = []
            for line in text.splitlines():
                m = re.search(r"\[(\d+)\]\s+(\d+)x(\d+)", line)
                if m:
                    entries.append((int(m.group(1)), int(m.group(2)),
                                    int(m.group(3))))
            print("WARNING: could not tell the game's list from Unity's; "
                  "showing every entry found", flush=True)

        # WHAT IT IS ACTUALLY RUNNING AT, which is the only number that
        # settles whether a write took effect. The saved index and the
        # live size DISAGREE on this machine - the save held index 6
        # (1920x1080) while the game ran at 3840x2160 - so the index is
        # evidence of intent, not of outcome.
        live = ""
        for line in text.splitlines():
            m = re.search(r"current (\d+)x(\d+)", line)
            if m:
                live = f"{m.group(1)}x{m.group(2)}"
                break
        saved = _saved_index()
        print(f"-- {len(entries)} resolution(s) the game offers --", flush=True)
        for i, w, h in entries:
            mark = ""
            if live and f"{w}x{h}" == live:
                mark += "  <- RUNNING AT THIS"
            if saved is not None and i == saved:
                mark += "  <- saved index"
            print(f"   [{i}] {w}x{h}{mark}", flush=True)
        print(f"\nlive size {live or 'unknown'}, saved index {saved}",
              flush=True)

        pick = nearest(entries, WANT)
        if pick is None:
            env.close_game()
            sys.exit("no resolutions found - is DevTools installed?")

        index, w, h = pick
        print(f"\nclosest to {WANT[0]}x{WANT[1]}: index {index} = {w}x{h}",
              flush=True)

        env.close_game()

    # OUTSIDE THE `with`, DELIBERATELY. Environment restores the saves on
    # exit, so a write inside it is put straight back - the first version
    # of this reported "set resolution index 19" and changed nothing,
    # which is the same self-deception as the registry attempt before it.
    #
    # The game also rewrites the save from its RUNTIME state when it
    # closes, so this has to land after the game is gone as well. The
    # saved index was 6 at the start of this session and 0 by the end,
    # for exactly that reason.
    if not apply:
        print("Done: nothing written. Re-run with --apply to set it.",
              flush=True)
        return 0

    changed = env.set_window(index)
    print(f"Done: set resolution index {index} ({w}x{h}) in "
          f"{len(changed)} save(s): {', '.join(changed) or 'none'}",
          flush=True)
    print("VERIFY by launching once more - the index is intent, not "
          "outcome, and this machine has already shown the two disagree.",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
