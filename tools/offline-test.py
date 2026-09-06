"""Prove a run survives the server going away, and comes back when it returns.

Five phases, each one a claim that was made when offline play was designed:

  1. ONLINE      a real connection writes a cache of the run
  2. OFFLINE     with no server at all, the same run comes back - same seed,
                 same puzzles, same packs held
  3. EARNED      a check solved offline is recorded and owed
  4. REJOINED    the next connection sends it
  5. NOT STALE   a seed regenerated under the same slot name does NOT come up
                 on the cached plan

Phase 5 is the one worth having. A cache is a copy of something the server
owns, and the failure that matters is not "the cache is missing" - it is the
cache being confidently wrong about a run that has since been regenerated.

Runs inside harness_env, so the player's BepInEx config and save folder are
restored afterwards, including the cache file this creates.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/offline-test.py 2>/dev/null
"""
import json
import os
import socket
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment, SAVE_DIR, close_game

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
CACHE = os.path.join(SAVE_DIR, "alttl-last-session.json")

AP = os.path.join(REPO, "Archipelago")
SEED = os.path.join(REPO, "testserver", "out-hint")
PORT = 38281
SLOT = "droha"

TOTAL = 5
_step = [0]


def say(phase, msg):
    """One self-contained line. The status bar shows only the last one."""
    print(f"[phase {phase}/{TOTAL}] {msg}", flush=True)


def port_open(seconds=0.5):
    with socket.socket() as s:
        s.settimeout(seconds)
        return s.connect_ex(("127.0.0.1", PORT)) == 0


class Server:
    """MultiServer for one seed zip. A context manager so a failing phase
    still stops it - a stale server holding 38281 has cost a session before.

    SKIP_REQUIREMENTS_UPDATE is not optional. Archipelago runs
    ModuleUpdate.update() at the top of MultiServer.py, and when a requirement
    is unsatisfied it does not fail, it PROMPTS: "press enter to install it".
    With no console attached that read is an EOFError and the server dies
    before it binds. The first run of this harness lost a phase to exactly
    that, showing only "No response from localhost:38281" - the same trap that
    turned CI red, and just as unrecognisable from the symptom.

    Readiness is the PORT accepting, not a sleep. The fixed six-second sleep
    it replaced was both too short here and a guess everywhere else.
    """

    def __init__(self, folder):
        self.zip = next(f for f in sorted(os.listdir(folder))
                        if f.endswith(".zip"))
        self.folder = folder
        self.proc = None

    def __enter__(self):
        env = dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1")
        # stderr to its own file rather than merged or discarded: merged, one
        # library warning becomes the last line and hides every progress line;
        # discarded, a server that will not start has nothing to explain it.
        self.err = open(os.path.join(REPO, "testserver", "logs",
                                     "offline-test-server.err"), "w")
        self.proc = subprocess.Popen(
            [sys.executable, "MultiServer.py", "--port", str(PORT),
             os.path.join(self.folder, self.zip)],
            cwd=AP, stdout=subprocess.DEVNULL, stderr=self.err,
            stdin=subprocess.DEVNULL, env=env)

        for _ in range(60):
            if port_open():
                return self
            if self.proc.poll() is not None:
                self.err.flush()
                raise SystemExit(
                    f"MultiServer exited with {self.proc.returncode} before "
                    "binding; see testserver/logs/offline-test-server.err")
            time.sleep(1)
        raise SystemExit(f"MultiServer never opened port {PORT}")

    def __exit__(self, *exc):
        if self.proc:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        self.err.close()
        # The port must be genuinely closed before the next phase launches,
        # or an "offline" phase quietly connects to a dying server.
        for _ in range(20):
            if not port_open(0.3):
                break
            time.sleep(1)
        return False


class Log:
    def __init__(self):
        self.pos = self._size()

    def _size(self):
        try:
            return os.path.getsize(LOG)
        except OSError:
            return 0

    def rewind(self):
        self.pos = self._size()

    def new(self):
        size = self._size()
        if size < self.pos:
            self.pos = 0
        if size == self.pos:
            return ""
        with open(LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(self.pos)
            text = f.read()
        self.pos = size
        return text

    def wait(self, needles, timeout, phase, what):
        """Accumulate log text until one of `needles` appears."""
        got = ""
        end = time.time() + timeout
        last = 0
        while time.time() < end:
            got += self.new()
            for n in needles:
                if n in got:
                    return got
            if time.time() - last > 5:
                last = time.time()
                say(phase, f"waiting for {what} "
                           f"({int(end - time.time())}s left)")
            time.sleep(0.5)
        return got


def launch(phase, log):
    """Start the game. No arguments, ever: Steam's rungameid fails with a
    'no license' dialog on this family-shared copy, and Unity pops a dialog
    for an argument it does not recognise. Both look like a hang."""
    log.rewind()
    subprocess.Popen([EXE], cwd=GAME)
    say(phase, "launched, waiting for the plugin")


def dev(cmd, settle=0.0):
    with open(CMD, "w", encoding="utf-8") as f:
        f.write(cmd)
    for _ in range(240):
        try:
            if os.path.getsize(CMD) == 0:
                break
        except OSError:
            pass
        time.sleep(0.25)
    if settle:
        time.sleep(settle)


def line_after(text, marker):
    for line in text.splitlines():
        if marker in line:
            return line
    return ""


def cache():
    if not os.path.exists(CACHE):
        return None
    with open(CACHE, encoding="utf-8") as f:
        return json.load(f)


def run_state_owed(seed):
    path = os.path.join(SAVE_DIR, f"save_ap_{SLOT}_{seed}.run.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f).get("owed", [])


def regenerate(phase):
    """A different seed under the SAME slot name - the stale-cache hazard."""
    yaml_dir = os.path.join(REPO, "testserver", "yaml-offline")
    out = os.path.join(REPO, "testserver", "out-offline")
    os.makedirs(yaml_dir, exist_ok=True)
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))

    # Deliberately a different SIZE, so "did it come up on the cached plan"
    # is answerable from the puzzle count alone.
    with open(os.path.join(yaml_dir, "offline.yaml"), "w", encoding="utf-8") as f:
        f.write(
            "name: droha\n"
            "game: A Little to the Left\n"
            "requires:\n"
            "  version: 0.6.0\n"
            "A Little to the Left:\n"
            "  puzzle_count: 18\n"
            "  levels_to_beat: 6\n"
            "  pack_size: 3\n"
            "  ability_locks: true\n"
            "  starting_abilities: 2\n"
            "  cat_trap_chance: 10\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n")

    say(phase, "generating a different seed under the same slot name")
    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", yaml_dir,
         "--outputpath", out, "--seed", "20260906"],
        cwd=AP, capture_output=True, text=True,
        # Same prompt as MultiServer's - Generate.py calls ModuleUpdate too.
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        say(phase, f"FAIL generation: {r.stderr.strip().splitlines()[-1:]}")
        return False
    return True


def main():
    results = []
    log = Log()

    with Environment("offline-test") as env:
        env.configure(Host="localhost", Port=PORT, SlotName=SLOT,
                      AutoConnect="true")

        # ---- 1. online: a real connection writes the cache ----------------
        with Server(SEED):
            launch(1, log)
            out = log.wait(["connected. "], 90, 1, "the connection")
            connected = line_after(out, "connected. ")
            if not connected:
                say(1, "FAIL never connected")
                return 1
            say(1, connected.split("] ")[-1])
            time.sleep(8)                      # let the cache debounce fire
            close_game()
            time.sleep(2)

            first = cache()
            ok = first is not None and len(first.get("items", [])) >= 0
            say(1, f"cache written: {ok}"
                   + (f", seed {first['seed']}, {len(first['slot_data']['slots'])}"
                      f" puzzles, {len(first['items'])} item(s)" if first else ""))
            results.append(("1 ONLINE  cache written", ok))
            if not ok:
                return 1

        online_seed = first["seed"]
        online_puzzles = len(first["slot_data"]["slots"])
        online_items = len(first["items"])

        # ---- 2. offline: the same run comes back --------------------------
        launch(2, log)
        out = log.wait(["offline: resumed", "connected. "], 120, 2,
                       "the offline start")
        resumed = line_after(out, "offline: resumed")
        say(2, resumed.split("] ")[-1] if resumed else "NOTHING RESUMED")
        ok = (bool(resumed)
              and f"seed {online_seed}" in resumed
              and f"{online_puzzles} puzzles" in resumed
              and f"{online_items} item(s)" in resumed)
        results.append(("2 OFFLINE same seed, puzzles and items", ok))

        # The track really going up is a separate claim from the log line.
        track = line_after(out, "track: ")
        say(2, track.split("] ")[-1] if track else "NO TRACK")
        results.append(("2 OFFLINE track up", "puzzles" in track))

        # ---- 3. earned: a check solved with no server is owed -------------
        dev("press:Confirm Button", 1.5)
        dev("menu:title", 2.5)
        dev("play", 9.0)
        log.new()
        for i in range(4):
            dev(f"solve:{i}", 0.8)
            got = log.wait(["LevelComplete ", "no level running"], 4, 3,
                           "the puzzle to finish")
            if "LevelComplete " in got:
                break
        log.wait(["check:", "beaten:"], 8, 3, "the check")
        time.sleep(3)
        close_game()
        time.sleep(2)

        owed = run_state_owed(online_seed)
        say(3, f"owed after playing offline: "
               f"{len(owed) if owed is not None else 'no run file'}")
        results.append(("3 EARNED  check queued offline",
                        bool(owed)))

        # ---- 4. rejoined: the next connection sends it --------------------
        with Server(SEED):
            launch(4, log)
            out = log.wait(["checks: sent "], 120, 4, "the queued check to go")
            sent = line_after(out, "checks: sent ")
            say(4, sent.split("] ")[-1] if sent else "NOTHING SENT")
            results.append(("4 REJOINED offline check sent", bool(sent)))
            time.sleep(5)
            close_game()
            time.sleep(2)

        # ---- 5. not stale: a regenerated seed wins ------------------------
        if not regenerate(5):
            results.append(("5 NOT STALE regenerated seed wins", False))
        else:
            with Server(os.path.join(REPO, "testserver", "out-offline")):
                launch(5, log)
                out = log.wait(["connected. "], 120, 5, "the new seed")
                line = line_after(out, "connected. ")
                say(5, line.split("] ")[-1] if line else "NEVER CONNECTED")
                after = cache()
                new_seed = after["seed"] if after else ""
                new_puzzles = len(after["slot_data"]["slots"]) if after else 0
                say(5, f"cache now: seed {new_seed}, {new_puzzles} puzzles "
                       f"(was {online_seed}, {online_puzzles})")
                results.append(("5 NOT STALE new seed replaces the cache",
                                bool(new_seed) and new_seed != online_seed
                                and new_puzzles == 18))
                # The old run's save must still exist and be untouched by this.
                old_save = os.path.join(
                    SAVE_DIR, f"save_ap_{SLOT}_{online_seed}.json")
                results.append(("5 NOT STALE the old run's save is intact",
                                os.path.exists(old_save)))
                time.sleep(3)
                close_game()

    passed = sum(1 for _, ok in results if ok)
    for name, ok in results:
        print(f"  {'PASS' if ok else 'FAIL'}  {name}", flush=True)
    print(f"Done: {passed}/{len(results)} checks passed", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
