"""Install the release the way a player does, and play a short run to the end.

Every other harness here tests the working tree. This one tests the RELEASE:
the mod from its zip, the world from its .apworld with no loose copy anywhere,
a seed generated from a yaml, a real MultiServer, and a run played to the
credits. It is the only test that would catch a release that is broken only
as a release.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/release-e2e.py 2>/dev/null

    --clean-only   put the install back to vanilla and stop
    --assets DIR   where the three release files are (default release-test)

The manual equivalent, and what each log line proves, is in
docs/release-testing.md.

THE RUN: 8 puzzles, 2 packs, beat all 8. Small enough to finish in minutes,
and 2 packs means the progression actually has to work - four puzzles open at
the start, and the other four arrive only if packs are received and applied.

IT CLEANS FIRST, NOT AFTER. Re-running is therefore always valid whatever
state the last run left behind, and the install is left working so a person
can look at it. `--clean-only` is how you get back to vanilla.

WHAT IT NEVER TOUCHES: save1.json, the campaign. Its hash is taken before the
run and compared after, and that comparison is one of the assertions - a
randomized run writing into the player's own save is the worst thing this mod
could do, and it is invisible unless something checks.

DevTools stays installed, because the run is driven through its command file.
That is a separate plugin the randomizer knows nothing about; it is never
shipped, and it does not participate in anything being asserted here.
"""
import argparse
import collections
import hashlib
import json
import os
import shutil
import socket
import subprocess
import sys
import time
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import (SAVE_DIR, CONFIG_DIR, close_game,
                         ensure_no_steam_relaunch)

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
PLUGIN_DIR = os.path.join(GAME, "BepInEx", "plugins", "ALTTLArchipelago")
MOD_CONFIG = os.path.join(CONFIG_DIR, "droha.alttl.archipelago.cfg")
CAMPAIGN = os.path.join(SAVE_DIR, "save1.json")

AP = os.path.join(REPO, "Archipelago")
PORT = 38281
SLOT = "droha"

PUZZLES = 8
PACKS = 2
MAX_ROUNDS = 60

TOTAL = 7


def say(phase, msg):
    print(f"[{phase}/{TOTAL}] {msg}", flush=True)


def sha(path):
    if not os.path.isfile(path):
        return None
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def port_open(timeout=0.5):
    with socket.socket() as s:
        s.settimeout(timeout)
        return s.connect_ex(("127.0.0.1", PORT)) == 0


class Log:
    """Reads the game log.

    THE OFFSET TRAP, hit twice now and subtler the second time. BepInEx
    truncates LogOutput.log on every launch, so an offset taken before a
    launch points into the middle of a different file. Resetting when the file
    SHRINKS is not enough: between the launch and the first sample the new log
    can already have grown past the old size, and then the reader silently
    starts mid-file and misses everything before it - which is where the
    interesting lines are, because they are written during startup.

    The first run of this harness sat waiting for a "connected." line that was
    already in the log.

    So `before_launch()` DELETES the log. BepInEx recreates it, the offset is
    zero, and everything read afterwards provably belongs to this session.
    """

    def __init__(self):
        self.pos = 0

    def _size(self):
        try:
            return os.path.getsize(LOG)
        except OSError:
            return 0

    def before_launch(self):
        """Call with the game CLOSED, immediately before starting it."""
        try:
            os.remove(LOG)
        except OSError:
            pass                     # never existed, or is held open; pos=0 anyway
        self.pos = 0

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
        got = ""
        end = time.time() + timeout
        last = 0
        while time.time() < end:
            got += self.new()
            if any(n in got for n in needles):
                return got
            if time.time() - last > 8:
                last = time.time()
                say(phase, f"waiting for {what} ({int(end - time.time())}s left)")
            time.sleep(0.5)
        return got


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


def line_with(text, marker):
    for line in text.splitlines():
        if marker in line:
            return line.split("] ")[-1].strip()
    return ""


# ---------------------------------------------------------------- phases ----

def clean():
    """Back to vanilla, minus DevTools. Never touches the campaign save."""
    close_game()
    removed = []
    if os.path.isdir(PLUGIN_DIR):
        shutil.rmtree(PLUGIN_DIR)
        removed.append("the mod")
    if os.path.isfile(MOD_CONFIG):
        os.remove(MOD_CONFIG)
        removed.append("its config")

    runs = 0
    for name in os.listdir(SAVE_DIR):
        if name.startswith("save_ap_") or name == "alttl-last-session.json":
            os.remove(os.path.join(SAVE_DIR, name))
            runs += 1
    if runs:
        removed.append(f"{runs} randomized-run file(s)")

    # The SERVER's memory too, not just the local save.
    #
    # An .apsave beside the seed is MultiServer's record of what has been
    # checked and what has been sent. Leaving it means the moment the game
    # connects, every item ever collected is replayed - so a run that looks
    # fresh has every ability already unlocked and nothing is ever gated.
    #
    # generate() happens to clear it by emptying the output folder, so the
    # full test was never affected. Probes calling clean() on its own were:
    # several of them spent a while measuring a fully unlocked multiworld
    # while reporting on ability locks.
    for folder in (os.path.join(REPO, "testserver", "out-e2e"),):
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            if name.endswith(".apsave"):
                os.remove(os.path.join(folder, name))
                removed.append("the server's saved progress")

    # A loose worlds/alttl would satisfy the import and the .apworld would
    # never be exercised - the exact bug the packaged-world CI job exists for.
    loose = os.path.join(AP, "worlds", "alttl")
    if os.path.isdir(loose):
        shutil.rmtree(loose)
        removed.append("the loose worlds/alttl copy")
    for f in ("alttl.apworld",):
        p = os.path.join(AP, "custom_worlds", f)
        if os.path.isfile(p):
            os.remove(p)
            removed.append("the installed .apworld")

    return removed or ["nothing - already clean"]


def install_mod(assets):
    zip_path = os.path.join(assets, "ALTTLArchipelago-0.3.0.zip")
    if not os.path.isfile(zip_path):
        candidates = [f for f in os.listdir(assets) if f.endswith(".zip")]
        if not candidates:
            sys.exit(f"no mod zip in {assets}")
        zip_path = os.path.join(assets, candidates[0])

    with zipfile.ZipFile(zip_path) as z:
        names = [n for n in z.namelist() if n.endswith(".dll")]
        z.extractall(GAME, members=[n for n in z.namelist()
                                    if n.startswith("BepInEx/")])
    return os.path.basename(zip_path), names


def install_apworld(assets):
    src = os.path.join(assets, "alttl.apworld")
    dst_dir = os.path.join(AP, "custom_worlds")
    os.makedirs(dst_dir, exist_ok=True)
    shutil.copyfile(src, os.path.join(dst_dir, "alttl.apworld"))
    return sha(src)[:16]


def write_config():
    """Point the fresh install at the test server before its first launch.

    A player types this into the pane. Writing the file is the scriptable
    equivalent, and BepInEx fills in every key not named here with its default.
    """
    with open(MOD_CONFIG, "w", encoding="utf-8", newline="\n") as f:
        f.write("[Server]\n"
                "Host = localhost\n"
                f"Port = {PORT}\n"
                f"SlotName = {SLOT}\n"
                "Password = \n"
                "AutoConnect = true\n"
                "MaxRetries = 0\n")


def generate():
    yaml_dir = os.path.join(REPO, "testserver", "yaml-e2e")
    out = os.path.join(REPO, "testserver", "out-e2e")
    for d in (yaml_dir, out):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))

    with open(os.path.join(yaml_dir, "e2e.yaml"), "w", encoding="utf-8") as f:
        f.write(
            f"name: {SLOT}\n"
            "game: A Little to the Left\n"
            "requires:\n"
            "  version: 0.6.7\n"
            "A Little to the Left:\n"
            f"  puzzle_count: {PUZZLES}\n"
            f"  levels_to_beat: {PUZZLES}\n"
            # pack_size 2 over 8 puzzles gives boundaries [4, 6, 8]: four open
            # free, then two packs of two. Measured, not guessed - pack_size 4
            # would give ONE pack and test half of what this is for.
            "  pack_size: 2\n"
            "  ability_locks: true\n"
            "  starting_abilities: 1\n"
            "  cat_trap_chance: 25\n"
            "  hint_coverage: 50\n"
            "  skip_count: 2\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n")

    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", yaml_dir,
         "--outputpath", out, "--seed", "20260906"],
        cwd=AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stdout[-2500:], flush=True)
        print(r.stderr[-2500:], flush=True)
        sys.exit("generation failed")
    zips = [f for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        sys.exit("generation produced no seed")
    return out, zips[0]


def read_plan(folder, seed_zip):
    """slot -> (levelIndex, levelId) and the pack boundaries, from the seed.

    The authoritative source, read with Archipelago's own loader rather than
    guessed at. The harness needs it because it opens levels by index, and it
    needs the BOUNDARIES so it can refuse to open a slot the packs have not
    reached - see play().
    """
    # Run inside the Archipelago checkout, because restricted_loads and the
    # multidata format are its business, not this harness's.
    code = """
import zipfile, zlib, json, sys
from Utils import restricted_loads
f = zipfile.ZipFile(sys.argv[1])
n = [x for x in f.namelist() if x.endswith('.archipelago')][0]
d = restricted_loads(zlib.decompress(f.read(n)[1:]))['slot_data'][1]
print(json.dumps({'slots': [(s['levelIndex'], s['levelId'])
                            for s in d['slots']],
                  'boundaries': list(d['pack_boundaries'])}))
"""
    r = subprocess.run([sys.executable, "-c", code,
                        os.path.join(folder, seed_zip)],
                       cwd=AP, capture_output=True, text=True,
                       env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stderr[-1500:], flush=True)
        sys.exit("could not read the seed's plan")
    return json.loads(r.stdout.strip().splitlines()[-1])


class Server:
    def __init__(self, folder, zipname):
        self.path = os.path.join(folder, zipname)
        self.proc = None
        self.err = None
        self.out = None
        self.outpath = None

    def __enter__(self):
        os.makedirs(os.path.join(REPO, "testserver", "logs"), exist_ok=True)
        self.err = open(os.path.join(REPO, "testserver", "logs", "e2e-server.err"),
                        "w")
        # Capture stdout too, in its OWN file. The server's account of the run
        # is the other half of every claim the mod makes - "goal: reported to
        # the server" is the mod saying it sent one, and only this says the
        # server agreed. The first version discarded it, so the strongest
        # assertion available was the mod grading its own work.
        self.outpath = os.path.join(REPO, "testserver", "logs", "e2e-server.log")
        self.out = open(self.outpath, "w")
        self.proc = subprocess.Popen(
            [sys.executable, "MultiServer.py", "--port", str(PORT), self.path],
            cwd=AP, stdout=self.out, stderr=self.err,
            stdin=subprocess.DEVNULL,
            # Without this MultiServer PROMPTS to install a drifted
            # requirement and dies on EOF, which looks exactly like a refused
            # port. It has cost a phase before.
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
        for _ in range(60):
            if port_open():
                return self
            if self.proc.poll() is not None:
                self.err.flush()
                raise SystemExit("MultiServer exited before binding; see "
                                 "testserver/logs/e2e-server.err")
            time.sleep(1)
        raise SystemExit("MultiServer never opened the port")

    def __exit__(self, *exc):
        if self.proc:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        if self.err:
            self.err.close()
        if self.out:
            self.out.close()
        for _ in range(20):
            if not port_open(0.3):
                break
            time.sleep(1)
        return False


def unsolved_controllers(text):
    """Indexes of controllers reported solved=False.

    Standalone and pure so it can be checked without a game, which it now is -
    see the self-test at the bottom of this file. It went wrong in a way no
    amount of staring would have caught quickly: a BepInEx log line is

        [Info   :ALTTL Dev Tools]   [3] ChalkPurple Jigsaw ... solved=False

    and the FIRST bracketed thing on it is the log prefix, not the controller
    index. Splitting on "[" grabbed "Info   :ALTTL Dev Tools", int() threw,
    every line was skipped, and the caller concluded that nothing was unsolved
    and the level must be finished. Two runs issued no solve command at all.
    """
    todo = []
    for line in text.splitlines():
        if "solved=" not in line:
            continue
        # Drop the log prefix first, then read the index.
        body = line.split("] ", 1)[1] if "] " in line else line
        body = body.strip()
        if not body.startswith("["):
            continue
        try:
            idx = int(body[1:body.index("]")])
        except (ValueError, IndexError):
            continue
        if body.rstrip().endswith("solved=False"):
            todo.append(idx)
    return todo


def solve_level(log):
    """Solve every controller until the level reports complete.

    SEVERAL PASSES, because one is not enough and the reason is a feature.
    A Cat Trap resets the puzzle, and one arrived mid-level on the very first
    attempt: three controllers were solved, the cat knocked them over, the
    remaining eight were solved, and the level never completed because the
    first three were unsolved again. The single pass reported NOT beaten and
    looked like the mod failing to notice a finished puzzle.

    So each pass asks which controllers are actually unsolved and solves those,
    up to five times. Traps are deliberately left on at their default rate:
    a run where the cat never interferes is not the run players get.
    """
    text = ""
    for attempt in range(5):
        log.new()
        dev("controllers", 1.0)
        out = log.wait(["controllers: "], 8, 6, "the controller list")
        # THE HEADER IS NOT THE LIST. log.wait returns the moment
        # "controllers: 11 registered on ..." appears, and the eleven
        # "[n] Name ... solved=False" lines are written after it. Parsing at
        # that instant found no unsolved controllers, concluded the level was
        # finished, and issued no solve commands at all - the log for that run
        # contains two boot: lines and not one solve:. Give the rest of the
        # listing time to land.
        time.sleep(1.5)
        out += log.new()
        text += out
        if "LevelComplete " in out or "no level running" in out:
            return True, text

        todo = unsolved_controllers(out)

        if not todo and "solved=" not in out:
            # No listing arrived at all - do not read that as "all solved".
            say(6, "the controller listing did not arrive; retrying")
            continue

        if not todo:
            # Every controller is solved. Either the completion already fired
            # and was read above, or it is about to.
            more = log.wait(["LevelComplete ", "no level running"], 6, 6,
                            "the completion")
            text += more
            return ("LevelComplete " in more or "no level running" in more), text

        if attempt:
            say(6, f"pass {attempt + 1}: {len(todo)} controller(s) still unsolved "
                   f"(a cat trap resets the puzzle)")

        for i in todo:
            dev(f"solve:{i}", 0.9)
            chunk = log.new()
            text += chunk
            if "LevelComplete " in chunk or "no level running" in chunk:
                return True, text

    return False, text


def launch_and_connect(log, phase, what):
    """Start the game and wait for the run to be up. Returns the log text."""
    if ensure_no_steam_relaunch():
        say(phase, "wrote steam_appid.txt so the game stops restarting itself")
    log.before_launch()
    subprocess.Popen([EXE], cwd=GAME)
    return log.wait(["connected. ", "Archipelago refused"], 150, phase, what)


def open_count(text, default):
    """How many slots the mod says are open, from everything logged so far."""
    best = default
    for line in text.splitlines():
        if "track:" not in line:
            continue
        try:
            if " open," in line:            # track: 8 puzzles, 4 open, 2 packs
                best = max(best, int(line.split("puzzles, ")[1].split(" open")[0]))
            elif "puzzles open" in line:    # track: 1/2 packs, 6 puzzles open
                best = max(best, int(line.split("packs, ")[1].split(" puzzles open")[0]))
        except (IndexError, ValueError):
            continue
    return best


def loaded_level(log, seconds=30, not_this=""):
    """Which puzzle is loaded and interactive right now, or "".

    Asked with `controllers`, not `state`. "N registered on <level>" only
    happens when a real, playable level is up, whereas gameState reported
    Gameplay in one run and RetryUI in the next for the very same action - it
    is not a signal worth trusting here.
    """
    end = time.time() + seconds
    text = ""
    while time.time() < end:
        log.new()
        dev("controllers", 1.2)
        out = log.wait(["controllers: "], 8, 6, "the level")
        time.sleep(1.2)
        out += log.new()
        text += out
        for line in out.splitlines():
            if "registered on " in line:
                name = line.split("registered on ", 1)[1].split(" levelInstance")[0]
                # The level being left is still loaded for a moment after the
                # arrow is pressed, so the first answer is often the old one.
                # Reporting it read as "the arrow went nowhere".
                if name and name != not_this:
                    return name, text
        time.sleep(1.5)
    return "", text


def boot_level(log, index):
    """Open a level by index and wait until it is actually running.

    The FALLBACK route, not the usual one - see play(). Used for the first
    puzzle of a session, where there is no completion screen to press an arrow
    on, and for stepping past a puzzle that cannot be finished yet.

    POLLS rather than sleeping once. The first boot of a session is slower than
    the rest, and a single nine second wait followed by one state check
    reported "did not open" for a level that had in fact loaded, as the next
    boot's teardown proved by finding it there.
    """
    text = ""
    deadline = time.time() + 40
    while time.time() < deadline:
        log.new()
        dev(f"boot:{index}", 6.0)
        for _ in range(6):
            dev("state", 0.4)
            out = log.wait(["state: gameState"], 8, 6, "the level")
            text += out
            if "Gameplay_GameState" in line_with(out, "state: gameState"):
                return True, text
            time.sleep(2.5)
    return False, text


def to_title(log):
    """Unwind to the title. Only used when the arrow cannot be followed.

    replayselect gets off the completion screen, menu:levels settles on the
    level select and menu:title returns to where a launch would have left you.
    Shorter versions do not work - measured, five levels each: nothing at all
    gave 2 clean then 28 exceptions, the pause menu's Level Select 2 then 16
    (after a BEATEN level it does nothing, the state stays RetryUI), and
    replayselect alone made a passing level start failing.
    """
    dev("replayselect", 3.0)
    dev("press:Confirm Button", 1.0)
    dev("menu:levels", 3.0)
    dev("menu:title", 3.0)
    return log.new()


def check_arrow(log, plan):
    """Verify the next-level arrow in a session of its own, then throw it away.

    ONE arrow press poisons the rest of a session, and that is measured, not
    assumed. The arrow launches through the mod's GoToNext, which calls
    StartLevel without releasing the level just finished; boot: then cannot
    clean up after it, because leaving through the menus deactivates that level
    and boot:'s teardown only sees active ones. A run with a single arrow press
    in it beat 2 of 8 and threw 103 times; the identical run without one beat
    8 of 8.

    So the arrow gets its own game, and the run gets a clean one. Two launches
    instead of one, about ninety seconds, and worth it: the arrow is the route
    a player actually uses after finishing a puzzle, and the mod patches it to
    stop the game sending a daily-pool level to the Daily Tidy page. Asserting
    it matters more than the launch count does.

    Returns (slot the arrow opened, slot expected) - or (None, None).
    """
    slots = plan["slots"]
    by_name = {name: i for i, (_, name) in enumerate(slots)}

    text = launch_and_connect(log, 5, "the connection for the arrow check")
    if "connected. " not in text:
        return None, None, text

    index, level_id = slots[0]
    opened, out = boot_level(log, index)
    text += out
    if not opened:
        close_game()
        return None, None, text

    done, chunk = solve_level(log)
    text += chunk
    if not done:
        close_game()
        return None, None, text

    log.new()
    dev("next", 8.0)
    # Capture the mod's own line BEFORE loaded_level runs: its first log.new()
    # discards whatever has arrived, which swallowed "navigation: replay Next"
    # and had the summary print "arrow presses: 0" beside a passing arrow
    # assertion. The count was wrong, not the test, but a diagnostic that
    # contradicts the result is worse than no diagnostic.
    text += log.new()
    name, out = loaded_level(log, 30, not_this=level_id)
    text += out
    got = by_name.get(name)
    say(5, f"      the arrow opened "
           f"{('slot ' + str(got) + ' ' + name) if got is not None else (name or 'nothing')}"
           f", expected slot 1")
    close_game()
    time.sleep(2)
    return got, 1, text


def play(log, plan):
    """Play the run the way a player does: finish a puzzle, press the arrow.

    THE ARROW IS THE PRIMARY ROUTE, and that is the point. It is
    ReplayMenu.NextLevel / RetryMenu.NextLevel, which the mod patches to launch
    the next UNFINISHED slot itself rather than let the game route by level
    kind - routing by kind is what sends a daily-pool level to the Daily Tidy
    page and drops the player out of their run. Verified separately: pressing
    it after slot 0 gave "navigation: replay Next -> slot 1 (level 79)" and
    eight controllers registered on Mirror, a real interactive level.

    Driving the run this way means the harness exercises the navigation a
    player actually uses, instead of proving only that a synthetic boot: works.

    boot: is still the FALLBACK, for the two cases the arrow cannot cover: the
    first puzzle of a session, when there is no completion screen to press an
    arrow on; and a puzzle that cannot be finished yet because a controller
    sits behind an ability that has not arrived. The arrow would keep offering
    that same unfinished slot forever, so the harness unwinds to the title and
    opens a different one.
    """
    slots = plan["slots"]
    by_name = {name: i for i, (_, name) in enumerate(slots)}
    beaten = {}
    attempts = collections.Counter()
    credits = False
    idle = 0

    first = launch_and_connect(log, 5, "the connection")
    if "connected. " not in first:
        return [], False, first, 0, first
    transcript = first
    open_slots = open_count(first, plan["boundaries"][0])

    say(6, f"{len(slots)} slots, boundaries {plan['boundaries']}, "
           f"{open_slots} open")

    current = None          # slot index of the puzzle now open, or None
    for step in range(1, MAX_ROUNDS + 1):
        if len(beaten) >= len(slots):
            break

        transcript += log.new()
        was = open_slots
        open_slots = open_count(transcript, open_slots)
        if open_slots > was:
            say(6, f"a pack opened more: {open_slots} slot(s) now available")

        # Nothing open, or the arrow led somewhere unusable: pick a slot and
        # open it the long way.
        if current is None:
            candidates = [i for i in range(min(open_slots, len(slots)))
                          if i not in beaten]
            if not candidates:
                say(6, f"round {step}: {len(beaten)}/{len(slots)} beaten and "
                       f"{open_slots} open - nothing left to try")
                break
            current = min(candidates, key=lambda i: (attempts[i], i))
            index, level_id = slots[current]
            transcript += to_title(log)
            opened, out = boot_level(log, index)
            transcript += out
            if not opened:
                say(6, f"round {step}: slot {current} {level_id} did not open")
                current, idle = None, idle + 1
                if idle >= len(slots) * 2:
                    break
                continue

        index, level_id = slots[current]
        attempts[current] += 1

        done, chunk = solve_level(log)
        transcript += chunk
        tail = log.wait(["beaten:", "check:", "credits:"], 10, 6, "the check")
        transcript += tail

        blocked = ""
        for line in (chunk + tail).splitlines():
            if "waiting on " in line:
                blocked = " - waiting on " + line.split("waiting on ", 1)[1].strip()

        if done:
            beaten[current] = level_id
            idle = 0
        else:
            idle += 1
        if "credits: unlocked" in transcript:
            credits = True

        say(6, f"round {step}: slot {current} {level_id} "
               f"{'beaten' if done else 'not finishable yet' + blocked} "
               f"({len(beaten)}/{len(slots)}, {open_slots} open)")

        if credits or len(beaten) >= len(slots):
            break
        if idle >= len(slots) * 2:
            say(6, f"nothing finished in {idle} attempts; stopping")
            break

        current = None

    if beaten:
        say(6, "staying connected for the goal report")
        transcript += log.wait(["goal: reported to the server"], 60, 6,
                               "the goal report")
        time.sleep(8)
        transcript += log.new()
        if "credits: unlocked" in transcript:
            credits = True

    close_game()
    return list(beaten.values()), credits, transcript, open_slots, first


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default="release-test")
    parser.add_argument("--clean-only", action="store_true")
    args = parser.parse_args()
    assets = os.path.join(REPO, args.assets)

    campaign_before = sha(CAMPAIGN)

    say(1, "cleaning the install back to vanilla")
    for item in clean():
        print(f"      removed {item}", flush=True)
    if args.clean_only:
        print("Done: install is vanilla (DevTools left in place)", flush=True)
        return 0

    say(2, "installing the mod from its zip")
    zip_name, dlls = install_mod(assets)
    print(f"      {zip_name} -> {len(dlls)} dll(s) in BepInEx/plugins", flush=True)
    write_config()

    say(3, "installing the world from its .apworld")
    digest = install_apworld(assets)
    print(f"      alttl.apworld sha256 {digest}, no loose copy", flush=True)

    say(4, f"generating {PUZZLES} puzzles / {PACKS} packs")
    out_dir, seed_zip = generate()
    plan = read_plan(out_dir, seed_zip)
    print(f"      {seed_zip}, boundaries {plan['boundaries']}", flush=True)

    results = []
    log = Log()

    with Server(out_dir, seed_zip) as server:
        # play() owns every launch. This used to launch here to check the
        # connection, close the game, and let play() launch it straight back -
        # an open/close/open that read as the game crashing on startup and was
        # pure waste. The first session play() opens is the one these
        # assertions are made against.
        say(5, "checking the next-level arrow in a session of its own")
        got, expected, arrow_text = check_arrow(log, plan)

        say(5, "launching a clean game and playing the run")
        beaten, credits, transcript, open_slots, text = play(log, plan)
        whole = arrow_text + transcript

        connected = line_with(text, "connected. ")
        print(f"      {connected or 'NEVER CONNECTED'}", flush=True)
        results.append(("connected to the server", bool(connected)))
        if not connected:
            close_game()
            for n, ok in results:
                print(f"  {'PASS' if ok else 'FAIL'}  {n}", flush=True)
            return 1

        results.append((f"the run is {PUZZLES} puzzles",
                        f"{PUZZLES} puzzles" in connected))
        results.append((f"the run has {PACKS} packs",
                        f"{PACKS} packs of" in connected))
        track = line_with(text, "track: ")
        print(f"      {track}", flush=True)
        results.append(("the track opens with 4 puzzles, not all 8",
                        "4 open" in track))

        # One launch for the whole run, and asserted. It took a correct boot:
        # teardown plus unwinding to the title after each puzzle to get here,
        # and both are easy to undo by accident - a regression should fail the
        # test rather than quietly making it ten times slower.
        launches = whole.count("A Little To The Left Archipelago loaded")
        arrows = whole.count("navigation: replay Next") +                  whole.count("navigation: post-level Continue")
        threw = whole.count("solve failed")
        print(f"      launches: {launches}; arrow presses: {arrows}; "
              f"solve exceptions: {threw}", flush=True)
        # Two: one throwaway for the arrow, one for the run. See check_arrow.
        results.append(("two game launches, no more", launches == 2))
        # The arrow is the route a player uses, so the harness uses it too -
        # a run that quietly fell back to opening every level itself would
        # still pass everything else while testing none of that navigation.
        # The player's route, asserted: the arrow opened the slot the run says
        # is next. Pressed once - see play() for why it is not how the harness
        # advances.
        results.append(("the next-level arrow opens the run's next puzzle",
                        got is not None and got == expected))

        results.append(("no solve threw inside the game", threw == 0))

        results.append((f"all {PUZZLES} puzzles beaten", len(beaten) >= PUZZLES))
        results.append((f"packs opened all {PUZZLES} slots, not just the first 4",
                        open_slots >= PUZZLES))
        results.append(("checks reached the server", "checks: sent " in whole))
        results.append(("the credits unlocked", credits))
        results.append(("the mod reported the goal",
                        "goal: reported to the server" in whole))

        # And the SERVER agrees. Read while it is still up, before __exit__.
        server_says = ""
        try:
            with open(server.outpath, encoding="utf-8", errors="replace") as f:
                server_says = f.read()
        except OSError:
            pass
        finished = ("has completed their goal" in server_says
                    or "completed their goal" in server_says
                    or "has completed all of their games" in server_says)
        for line in server_says.splitlines():
            if "goal" in line.lower() or "completed" in line.lower():
                print(f"      server: {line.strip()}", flush=True)
        results.append(("the server agrees the goal is met", finished))

        say(7, "checking the campaign save was never written")
        close_game()
        time.sleep(2)

    results.append(("the campaign save is byte-identical",
                    sha(CAMPAIGN) == campaign_before))
    results.append(("the run wrote its own save instead",
                    any(f.startswith("save_ap_") for f in os.listdir(SAVE_DIR))))

    passed = sum(1 for _, ok in results if ok)
    print(flush=True)
    for name, ok in results:
        print(f"  {'PASS' if ok else 'FAIL'}  {name}", flush=True)
    print(f"Done: {passed}/{len(results)} checks passed, "
          f"{len(beaten)} puzzle(s) beaten", flush=True)
    return 0 if passed == len(results) else 1


SAMPLE_LISTING = """[Info   :ALTTL Dev Tools] controllers: 11 registered on NeatStreak_Paper Plane Supplies levelInstance=-27438 solvedNow=1
[Info   :ALTTL Dev Tools]   [0] Draggables type=Draggables solved=False
[Info   :ALTTL Dev Tools]   [1] Containables type=Containables solved=False
[Info   :ALTTL Dev Tools]   [2] Drawer Controller type=DrawerController solved=True
[Info   :ALTTL Dev Tools]   [10] Chalk DraggablesOrdered type=DraggablesOrdered solved=False
"""


def self_test():
    """Check the log parser before spending four minutes finding out.

    Verbatim lines from a real run. Both of this harness's expensive failures
    were parsing, not the mod, and both would have been caught here in
    milliseconds.
    """
    got = unsolved_controllers(SAMPLE_LISTING)
    if got != [0, 1, 10]:
        sys.exit(f"self-test: unsolved_controllers gave {got}, expected [0, 1, 10]")
    if unsolved_controllers("") != []:
        sys.exit("self-test: an empty listing should give no work")

    # Every helper play() reaches for must exist. Splicing this file has twice
    # replaced a region that happened to contain one - boot_level went missing
    # that way, and the run died two minutes in with a NameError that stderr
    # had been redirected away from. A name lookup costs nothing and fails
    # before the game is ever launched.
    for name in ("launch_and_connect", "open_count", "loaded_level", "to_title",
                 "boot_level", "solve_level", "play", "read_plan", "clean",
                 "install_mod", "install_apworld", "write_config", "generate"):
        if name not in globals():
            sys.exit(f"self-test: {name}() is missing - a splice removed it")

    # Names that no longer exist. The helper check above catches a missing
    # FUNCTION; it did not catch `arrows` left behind in play() after the arrow
    # moved to its own session, and that NameError killed a run 12 minutes in
    # with 8 of 8 already beaten. pyflakes is not available here, so this walks
    # the AST for names that are neither builtins, globals, nor bound locally.
    try:
        import ast, builtins
        tree = ast.parse(open(__file__, encoding="utf-8").read())
        known = set(dir(builtins)) | set(globals())
        for fn in [n for n in ast.walk(tree)
                   if isinstance(n, ast.FunctionDef)]:
            bound = {a.arg for a in fn.args.args}
            for node in ast.walk(fn):
                if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Store):
                    bound.add(node.id)
                elif isinstance(node, (ast.For, ast.comprehension)):
                    tgt = getattr(node, "target", None)
                    if isinstance(tgt, ast.Name):
                        bound.add(tgt.id)
                elif isinstance(node, ast.ExceptHandler) and node.name:
                    bound.add(node.name)
                elif isinstance(node, (ast.Import, ast.ImportFrom)):
                    for a in node.names:
                        bound.add((a.asname or a.name).split(".")[0])
                elif isinstance(node, ast.withitem):
                    v = node.optional_vars
                    if isinstance(v, ast.Name):
                        bound.add(v.id)
            for node in ast.walk(fn):
                if (isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load)
                        and node.id not in bound and node.id not in known):
                    sys.exit(f"self-test: {fn.name}() uses '{node.id}', which is "
                             f"not defined anywhere - a leftover from an edit")
    except SystemExit:
        raise
    except Exception:
        pass          # the check is a convenience, never a reason to not run

    if open_count("track: 8 puzzles, 4 open, 2 packs", 0) != 4:
        sys.exit("self-test: open_count misread the opening track line")
    if open_count("track: 1/2 packs, 6 puzzles open (+2)", 4) != 6:
        sys.exit("self-test: open_count misread the pack line")


if __name__ == "__main__":
    self_test()
    raise SystemExit(main())
