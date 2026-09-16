"""A run finished offline must still report its goal when the server returns.

THE BUG THIS EXISTS FOR, and it shipped in 0.3.2. Reporting the goal requires
the credits to have been PLAYED - the change that stopped a run ending the
instant the Credits item arrived. That flag lived only in GoalLatch, and
Credits.Reset() builds a new latch on every reconnect and every offline start.
So:

    finish the run offline -> play the credits -> reconnect
    -> the flag is gone, the goal is never sent, and the multiworld waits
       forever on a slot that genuinely finished.

Nothing logged it. The player sees the credits, the run looks over, and the
seed never completes for anyone else.

tools/offline-test.py cannot catch this: none of its five phases plays the
credits. The Core tests cover the latch in isolation. This is the only thing
that exercises the whole path through a real game, a real server going away,
and a real relaunch.

THREE PHASES, each one an assertion:

  1. WON, NOT PLAYED   online, beat the goal and hold the Credits item, but do
                       NOT play the card. The credits must unlock and the goal
                       must NOT be reported - that is the 0.3.2 behaviour
                       droha asked for and it must survive this fix.
  2. PLAYED OFFLINE    server gone. Relaunch, play the credits card. The mod
                       must record it in the run's sidecar file, because that
                       is the only place it can outlive the process.
  3. REPORTED LATE     server back. Relaunch. The goal must reach it.

Phase 3 is the regression. Before the fix it fails: the mod reconnects, builds
a fresh latch, and never reports.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-offline-goal.py 2>/dev/null

Runs inside harness_env, so the player's config and save folder are restored
afterwards. Closes the game on the way out.
"""
import importlib.util
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, SAVE_DIR, close_game, ensure_no_steam_relaunch

_spec = importlib.util.spec_from_file_location(
    "offline_test", os.path.join(TOOLS, "offline-test.py"))
off = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(off)

REPO = off.REPO
PORT = off.PORT
SLOT = off.SLOT
OUT = os.path.join(REPO, "testserver", "out-goalprobe")
YAML = os.path.join(REPO, "testserver", "yaml-goalprobe")
CMDS = os.path.join(REPO, "testserver", "goalprobe-commands.txt")

#: The game's credits "level". Same constant the release gate uses; it is only
#: needed because clickcard: takes an index.
CREDITS_LEVEL_INDEX = 84

TOTAL = 3
# The borrowed launch/dev helpers print through offline-test's say(), which
# carries ITS phase count. Line it up so a reader is not told "phase 2/5" by
# a three-phase probe.
off.TOTAL = TOTAL
results = []


def say(phase, msg):
    print(f"[phase {phase}/{TOTAL}] {msg}", flush=True)


def check(name, ok):
    results.append((name, ok))
    print(f"  {'PASS' if ok else 'FAIL'}  {name}", flush=True)


def write_yaml():
    """One puzzle to beat, everything open, nothing that can interfere.

    ability_locks off so a forced completion is not fighting dimmed objects,
    skip_count 0 so nothing else can finish a puzzle, and no traps so a cat
    cannot reset one mid-probe.
    """
    os.makedirs(YAML, exist_ok=True)
    with open(os.path.join(YAML, "goalprobe.yaml"), "w",
              encoding="utf-8", newline="\n") as f:
        f.write(
            f"name: {SLOT}\n"
            "game: A Little to the Left\n"
            "description: offline goal probe\n"
            "requires:\n  version: 0.6.7\n"
            "A Little to the Left:\n"
            "  goal: beat_levels\n"
            "  levels_to_beat: 1\n"
            "  puzzle_count: 8\n"
            "  pack_size: 8\n"
            "  ability_locks: false\n"
            "  skip_count: 0\n"
            "  hint_coverage: 0\n"
            "  cat_trap_chance: 0\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n")


def generate():
    os.makedirs(OUT, exist_ok=True)
    for stale in os.listdir(OUT):
        os.remove(os.path.join(OUT, stale))
    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML,
         "--outputpath", OUT, "--seed", "48151623"],
        cwd=off.AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stdout[-1500:], flush=True)
        sys.exit("generation failed")


class PipedServer(off.Server):
    """offline-test's Server, but reading console commands from a file.

    The base class gives MultiServer a DEVNULL stdin, which is right for a
    harness that never cheats. This probe has to hand itself the Credits item,
    because where the generator placed it is not the thing under test - so it
    borrows probe-skip-path's `tail -f` pipe.
    """

    def __enter__(self):
        open(CMDS, "w", encoding="utf-8").close()
        env = dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1")
        self.err = open(os.path.join(REPO, "testserver", "logs",
                                     "goalprobe-server.err"), "w")
        self.proc = subprocess.Popen(
            f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
            f'--port {PORT} "{os.path.join(self.folder, self.zip)}"',
            shell=True, cwd=off.AP, stdout=subprocess.DEVNULL,
            stderr=self.err, env=env)
        for _ in range(60):
            if off.port_open():
                return self
            time.sleep(1)
        raise SystemExit(f"MultiServer never opened port {PORT}")

    def __exit__(self, *exc):
        """Ask it to exit, THEN kill the tree if it will not.

        Two separate lessons, both paid for by a failed run of this probe.

        ONE: self.proc is `tail -f ... | MultiServer.py`, so it is the SHELL.
        Terminating it leaves MultiServer holding the port, and the base
        class's terminate() does exactly that. The consequence was subtle -
        "the run resumes with no server" failed while every other check
        passed, because phase 2 was never offline at all. It connected to a
        server that was supposed to be dead, and phase 3 then proved
        something weaker than it claimed.

        TWO: taskkill /F alone is not enough either. MultiServer persists what
        a slot has received to its .apsave, and a hard kill never gets there -
        so the NEXT phase's server came up having forgotten the Credits item
        this probe had granted, the goal condition could not be true, and
        phase 3 failed as if the mod were broken. Nothing about the symptom
        pointed at the teardown.

        So: /exit down the same pipe the probe cheats through, which lets it
        save, and the kill only as a fallback for a server that ignores it.
        """
        if self.proc and self.proc.poll() is None:
            try:
                send("/exit")
            except OSError:
                pass
            for _ in range(20):
                if self.proc.poll() is not None:
                    break
                time.sleep(0.5)
        if self.proc and self.proc.poll() is None:
            subprocess.run(["taskkill", "/PID", str(self.proc.pid), "/T", "/F"],
                           capture_output=True)
        return off.Server.__exit__(self, *exc)


def send(line):
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def run_state(seed):
    path = os.path.join(SAVE_DIR, f"save_ap_{SLOT}_{seed}.run.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def seed_of(text):
    for line in text.splitlines():
        if "save redirected to save_ap_" in line:
            return line.split("save_ap_")[1].strip().split("_", 1)[1]
    return None


def main():
    close_game()
    write_yaml()
    generate()

    with Environment("probe-offline-goal") as env:
        env.configure(Host="localhost", Port=PORT, SlotName=SLOT,
                      AutoConnect="true")
        env.configure_devtools(MuteAudio="true", RaiseWindowAtStartup="false")
        log = off.Log()
        seed = None

        # ---- 1. won, but the credits are not played ----------------------
        with PipedServer(OUT):
            ensure_no_steam_relaunch()
            off.launch(1, log)
            text = log.wait(["connected. "], 180, 1, "the connection")
            time.sleep(6.0)
            text += log.new()
            seed = seed_of(text)
            say(1, f"connected, seed {seed}")

            say(1, "granting the Credits item")
            send(f"/send {SLOT} Credits")
            text += log.wait(["received item: Credits"], 30, 1, "the item")

            say(1, "beating one puzzle to meet the goal")
            # menu:title BEFORE play. `play` presses Play on the TITLE menu,
            # so sending menu:levels first leaves no live TitleMenu, nothing
            # opens, and the `complete` that follows throws on a level that is
            # not there - which is exactly how the first run of this probe
            # failed, three phases deep and looking like a mod bug.
            off.dev("menu:title", 3.0)
            off.dev("play", 9.0)
            off.dev("complete", 6.0)
            text += log.wait(["credits: unlocked"], 40, 1, "the unlock")
            time.sleep(8.0)
            text += log.new()

            check("1 the credits unlock once the goal is met",
                  "credits: unlocked" in text)
            # The 0.3.2 behaviour, and it must survive this fix: winning is not
            # finishing. Nothing may report until the card is played.
            check("1 the goal is NOT reported before the card is played",
                  "goal: reported to the server" not in text)
            close_game()

        # ---- 2. the credits are played with no server ---------------------
        say(2, "server is down; relaunching offline")
        off.launch(2, log)
        text = log.wait(["staying offline", "connected. "], 180, 2,
                        "the offline start")
        time.sleep(6.0)
        text += log.new()
        check("2 the run resumes with no server", "offline: resumed" in text)

        say(2, "playing the credits card")
        off.dev("menu:levels", 3.0)
        off.dev(f"clickcard:{CREDITS_LEVEL_INDEX}", 8.0)
        text += log.wait(["credits: played to the end"], 60, 2, "the credits")
        time.sleep(4.0)
        text += log.new()
        check("2 the mod sees the credits played offline",
              "credits: played to the end" in text)

        state = run_state(seed) or {}
        # THE MECHANISM. Without this key the flag dies with the process and
        # phase 3 can never pass, however the rest behaves.
        check("2 credits-played is written to the run's save file",
              state.get("creditsPlayed") is True)
        close_game()

        # ---- 3. the server comes back -------------------------------------
        say(3, "server back up; relaunching")
        with PipedServer(OUT):
            off.launch(3, log)
            text = log.wait(["goal: reported to the server"], 180, 3,
                            "the late goal report")
            time.sleep(4.0)
            text += log.new()
            check("3 the goal is reported after reconnecting",
                  "goal: reported to the server" in text)
            close_game()

    passed = sum(1 for _, ok in results if ok)
    print(f"Done: {passed}/{len(results)} checks passed", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
