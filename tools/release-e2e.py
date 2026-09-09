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

#: Run size. --quick lowers these; see main().
#:
#: The full run is a RELEASE GATE - a real game, a real MultiServer, eight
#: puzzles played to the credits, about fifteen minutes. It is the wrong tool
#: for "did this edit break anything", and using it that way once cost twelve
#: runs and about three hours to land one set of fixes.
#:
#: --quick answers that question instead: three puzzles, no throwaway arrow
#: session, every correctness check kept - the error census, the
#: mod-versus-harness reconciliation, the campaign-save isolation. What it
#: gives up is coverage of the arrow, the pause-menu Exit, and the launch
#: count, which are the parts that need a second session.
PUZZLES = 8

#: Requested pack size. How many packs that BUYS is items._pack_cap's
#: call, not ours - at 8 puzzles the cap is 1, so the request widens to a
#: single pack of 4. Asserting a pack count here instead of reading the
#: generator's boundaries is what made 'the run has 2 packs' fail on every
#: run for as long as the cap has existed.
PACK_SIZE = 2

#: solve_level writes this into the text it returns when a level has no
#: unsolved controllers left and the game still will not complete it.
#:
#: A CONSTANT because the first version of this was two string literals,
#: and they did not match: play() grepped for "waiting for the completion",
#: which is a say() to stdout, while the text it searched is the game log.
#: The skip path could never fire and nothing said so - the run just
#: stalled the way it had before the fix. One name removes the whole bug
#: class; self_test asserts it is used on both sides.
EXHAUSTED_MARK = "harness: level exhausted, no completion"
MAX_ROUNDS = 60
QUICK = False

TOTAL = 7


def say(phase, msg):
    print(f"[{phase}/{TOTAL}] {msg}", flush=True)


def sha(path):
    if not os.path.isfile(path):
        return None
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def read_save(path):
    """Decode one of the game's save files.

    They are UTF-8 with a BOM and every codepoint shifted up by 11, which is
    obfuscation rather than encryption - the first bytes decode to {"guid".
    """
    if not os.path.isfile(path):
        return None
    try:
        text = open(path, encoding="utf-8-sig").read()
        return json.loads("".join(chr(ord(c) - 11) for c in text))
    except Exception:
        return None


#: Campaign fields a run is allowed to move, and why.
#:
#: saveTimestamp changes on any write at all. dailyTidyProgress rolls its own
#: calendar forward: the game appends one history entry per day it has not seen
#: yet, at LAUNCH, before an Archipelago session exists and therefore before
#: SaveRedirect is armed. Vanilla does this whether the mod is installed or not.
IGNORED_CAMPAIGN_FIELDS = ("saveTimestamp", "dailyTidyProgress")


def campaign_progress(path):
    """What a run must never touch, as a comparable value.

    NOT a hash of the file, and that distinction cost a full run to diagnose.
    A sha caught the daily calendar rolling over on 2026-09-07 and reported it
    as the run writing to the campaign save - the run had written nothing, and
    levelCompletionData was identical. Worse, it was a once-per-day failure:
    the same build passed on a second run the same day, so the check was
    green except for the first run after midnight, which is exactly the
    pattern that teaches people to ignore a red result.

    So compare the fields that carry actual campaign progress, and pin the
    daily completion count separately - that one IS ours to protect, since a
    run finishing a 995-1000 puzzle used to credit a real daily.
    """
    data = read_save(path)
    if data is None:
        return None
    kept = {k: v for k, v in data.items() if k not in IGNORED_CAMPAIGN_FIELDS}
    return json.dumps(kept, sort_keys=True)


def campaign_diff(before, after):
    """Which top-level fields moved, for the failure line.

    A bare "the campaign progress is untouched: FAIL" is the most alarming
    line this harness can print and it says nothing about severity. On
    2026-09-09 it fired for playerPrefs.fullscreen - a display setting the
    harness itself had flipped - while levelCompletionData was byte-identical,
    and telling those two apart took a manual decode of both saves. Name the
    fields and the reader can judge in one line.
    """
    if before is None or after is None:
        return "one of the saves could not be read"
    a = json.loads(before)
    b = json.loads(after)
    moved = sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))
    return ", ".join(moved) if moved else "nothing"


def daily_completions(path):
    """The player's real daily-tidy completion count."""
    data = read_save(path)
    if data is None:
        return None
    progress = data.get("dailyTidyProgress") or {}
    return progress.get("CompleteCount")


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
        """Call with the game CLOSED, immediately before starting it.

        The delete is RETRIED and then insisted on, which is the third face of
        the offset trap above. It used to be a bare try/except that swallowed
        the failure and set pos = 0 anyway:

            try:
                os.remove(LOG)
            except OSError:
                pass         # never existed, or is held open; pos=0 anyway

        On Windows a process that has just exited can hold the handle for a
        moment longer, and then the remove fails, the file still holds the
        PREVIOUS session, and pos = 0 makes the reader consume all of it again
        as if it were new. Every count taken from the transcript is then wrong
        by one session - which is exactly what happened on 2026-09-07: two real
        launches counted as three and failed "two game launches, no more" on a
        build whose launch behaviour had not changed.

        Setting pos to the current size instead would be worse, not better: it
        is the same mistake the class docstring already warns about, because
        the new log can grow past the old size before the first sample and the
        shrink is then never observed.

        So the only safe outcomes are "deleted" or "stop". A harness that
        cannot trust its own transcript should say so rather than produce
        numbers.
        """
        for attempt in range(30):
            try:
                os.remove(LOG)
                break
            except FileNotFoundError:
                break                # never existed; nothing to reset past
            except OSError:
                if attempt == 0:
                    print("      waiting for the game to release the log",
                          flush=True)
                time.sleep(0.5)
        else:
            raise RuntimeError(
                f"could not delete {LOG} after 15s - the game still holds it. "
                "Refusing to continue: the transcript would replay the "
                "previous session and every count taken from it would be wrong.")
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


#: Errors the run is allowed to log. EMPTY, and it should stay that way.
#:
#: It briefly held the DevTools menu: NullReferenceException, on the reasoning
#: that it was test-only and caught. That reasoning was wrong in the way
#: comfortable reasoning usually is: the error was real, it was the mod's, and
#: tolerating it would have hidden the fact that the mod opened the level
#: select without its Close button - a half-built menu handed to players.
#:
#: The cause was Navigation redirecting ReplayMenu.LevelSelect to
#: GoToLevelSelectForLevel. Letting the game open its own screen fixed the
#: missing control and the exception together, because they were one bug.
#:
#: Before adding anything here, find the cause. This list existing at all is a
#: standing invitation not to.
KNOWN_ERRORS = (
    # Vanilla's own ReplayMenu.LevelSelect, about once per run.
    #
    # Not ours and not avoidable from here. Letting the game open its own level
    # select is what gives the screen its Close button and what stopped
    # MenuManager.TransitionMenuOut throwing six or seven times a run; the cost
    # is that the game itself raises a NullReferenceException on roughly one
    # call in eight against a run state it was never written for.
    #
    # Swallowing it with a Harmony finalizer was tried and is WORSE - see
    # Navigation.BeforeReplayLevelSelect. The exception is the game aborting a
    # routine; forcing it to return normally carried on from a state the game
    # never meant to reach and cost seven of eight puzzles.
    "Il2CppInterop: During invoking native->managed trampoline",
)


def error_census(text, limit=15):
    """Every distinct error in the transcript, with counts, most frequent first.

    Grouped by signature rather than listed raw: one stale listener produces a
    hundred identical stack traces, and a hundred lines of the same thing is
    how a report gets skimmed instead of read. The signature is the message
    line, trimmed of the BepInEx prefix and of anything that varies per
    occurrence.
    """
    counts = {}
    context = {}
    lines = text.splitlines()
    for i, line in enumerate(lines):
        stripped = line.strip()
        if not stripped.startswith("[Error") and not stripped.startswith("[Fatal"):
            continue

        # "[Error  :     Unity] message" -> "Unity: message"
        source, _, message = stripped.partition("] ")
        source = source.split(":", 1)[-1].strip() if ":" in source else "?"
        message = message.strip()

        # Stack traces arrive as their own lines; the first line is the claim.
        signature = f"{source}: {message}"[:150]
        counts[signature] = counts.get(signature, 0) + 1

        # WHAT LED UP TO IT, once per signature.
        #
        # The count alone sent three separate investigations down the wrong
        # path: the error was blamed on a redundant menu transition, then on
        # the mod's level-select rewrite, and controlled probes refuted both.
        # Neither guess would have survived five seconds of looking at the
        # lines immediately before it, which the harness had all along and
        # never showed.
        if signature not in context:
            before = [l.strip() for l in lines[max(0, i - 6):i] if l.strip()]
            context[signature] = before[-4:]

    ordered = sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))[:limit]
    return [(sig, n, context.get(sig, [])) for sig, n in ordered]


def unexplained(errors):
    """The errors that are NOT on the known list - the ones that fail a run."""
    return [e for e in errors
            if not any(known in e[0] for known in KNOWN_ERRORS)]


#: Levels whose runtime controllers are KNOWN to differ from the shipped
#: table, so the mod's audit is expected to complain about them.
#:
#: Kept in step with SurveyCrossCheckTests.KnownGaps by hand. That is a small
#: duplication and worth it: the C# test compares two FILES and can prove the
#: gap statically, while this one watches what a level does when a player is
#: actually in it, and the whole point of the pair is that the second can find
#: something the first cannot.
KNOWN_TABLE_GAPS = (
    "Books (Randomized)",
    "Desktop Computer",
    "MedicineCabinet",
    "Radial Dance Party",
    "Record Player",
    "TupperwareNesting",
)


def table_audit(text):
    """Audit complaints about levels we have NOT already written down.

    The mod compares the shipped controller table against what the level in
    front of the player actually registered, and says so:

        CONTROLLER MISMATCH on TupperwareNesting: 7 registered, 2 in the table
        UNEARNABLE LOCATIONS on <level>: the table expects <names>

    Both were WARNINGS, and the census counts only errors, so a run could go
    green with the table wrong - which is exactly how six levels stayed
    under-captured through every previous release gate. The first one droha
    ever saw was found by reading a log, not by the harness.

    They are not promoted to errors in the mod, because a phased level raises
    a mismatch legitimately while it is still revealing itself. Deciding here
    keeps that nuance: a complaint about a level on the known list is data, a
    complaint about any OTHER level is a new gap and fails the run.
    """
    out = []
    for line in text.splitlines():
        if "CONTROLLER MISMATCH on " not in line and \
           "UNEARNABLE LOCATIONS on " not in line:
            continue
        marker = "CONTROLLER MISMATCH on " if "CONTROLLER MISMATCH on " in line \
            else "UNEARNABLE LOCATIONS on "
        level = line.split(marker, 1)[1].split(":", 1)[0].strip()
        if level in KNOWN_TABLE_GAPS:
            continue
        out.append(line.split("] ")[-1].strip())
    return out


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
    """Unpack the mod zip, and refuse if it is ambiguous which one.

    The version used to be written in here as a literal, which quietly
    became "prefer 0.3.0 if it is present". An assets folder holding last
    release's zip beside this one would then test LAST release and report
    a pass, which is the one outcome this harness must never produce.
    """
    candidates = sorted(f for f in os.listdir(assets) if f.endswith(".zip"))
    if not candidates:
        sys.exit(f"no mod zip in {assets}")
    if len(candidates) > 1:
        sys.exit(f"{len(candidates)} mod zips in {assets} - "
                 f"which one is the release? {', '.join(candidates)}")
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
            # The smallest pack the generator will honour, to get as many
            # packs as an 8-puzzle run can carry.
            #
            # IT WILL NOT HONOUR 2 HERE, and the assertion below no longer
            # pretends otherwise. items._pack_cap caps a run at
            # round(puzzle_count * 0.18) packs, which for 8 puzzles is ONE,
            # so the request widens to a single pack of 4 and the
            # boundaries come out [4, 8] - not the [4, 6, 8] this comment
            # used to claim as "measured, not guessed". Measured it was,
            # but before the cap existed; it then went on being asserted
            # as a hard-coded 2 that no 8-puzzle seed could satisfy.
            f"  pack_size: {PACK_SIZE}\n"
            # QUICK trades the two things that make a run long and variable,
            # not its size: puzzle_count has a floor of 8, so there is nothing
            # to shrink there.
            #
            # ability_locks off - gating is what stalls a run. Twice in a row
            # the full run sat at 2 of 8 waiting on Containers, which is
            # correct behaviour and useless feedback when the question is "did
            # my edit break check routing".
            #
            # cat traps off - a trap resets a puzzle mid-solve, so the same
            # change can pass one run and fail the next. Deliberately ON in the
            # full run, because a run where the cat never interferes is not the
            # run players get.
            f"  ability_locks: {'false' if QUICK else 'true'}\n"
            f"  starting_abilities: {6 if QUICK else 1}\n"
            f"  cat_trap_chance: {0 if QUICK else 25}\n"
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
        # NOBODY ELSE ON THE PORT. This is a pre-condition, not a nicety.
        #
        # Readiness below is "the port answers", which cannot tell OUR server
        # from someone else's. On 2026-09-07 a MultiServer left over from an
        # earlier run still held 38281: this run's server printed its usual
        # "Hosting game at ...:38281" line, then failed to bind with
        # errno 10048 - and the message comes BEFORE the bind, so the log
        # looked healthy. port_open() saw the stale server, returned true at
        # once, and the harness went on to point the game at a multiworld from
        # a different seed. What reached the log was
        #
        #     login refused: The slot name did not match any slot on the server
        #
        # which reads like a bug in the mod's connection handling and is
        # nothing of the kind. The traceback that explained it sat unread in
        # testserver/logs/e2e-server.err, because nothing had failed loudly.
        if port_open():
            raise SystemExit(
                f"port {PORT} is already in use - something else is serving "
                "there, and this run would silently test against ITS "
                "multiworld. Stop it and try again (the previous run's "
                "MultiServer is the usual culprit).")

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

    def stderr_tail(self, lines=12):
        """The server's own complaint, for a failure that mentions the server."""
        try:
            self.err.flush()
        except Exception:
            pass
        try:
            with open(os.path.join(REPO, "testserver", "logs", "e2e-server.err"),
                      encoding="utf-8", errors="replace") as fh:
                return "".join(fh.readlines()[-lines:])
        except OSError:
            return ""

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

    # A CAT TRAP MUST NOT COST A PASS.
    #
    # The trap is an Archipelago ITEM, not a property of the level - the mod
    # applies it to whatever puzzle is running, so no list of "levels with
    # cats" can predict it. What it does is knock the puzzle over mid-solve,
    # and a fixed budget of five passes then runs out on exactly the levels
    # that were unluckiest rather than the ones that are actually stuck.
    #
    # That is what made the full run a coin flip: identical code and seed beat
    # 8 of 8 three times and stalled at 2 of 8 twice. A stalled run then misses
    # the checks behind those levels, so the abilities never arrive and the
    # rest of the run is starved - one trap at the wrong moment costs six
    # puzzles.
    #
    # So a pass that a trap interrupted is refunded. The budget still exists
    # for a level that genuinely will not finish; it is just no longer spent on
    # the game doing what the game is supposed to do.
    budget = 5
    refunds = 0
    MAX_REFUNDS = 6
    attempt = -1
    while True:
        attempt += 1
        if attempt >= budget:
            break
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
        # "no level running" is NOT proof of completion, and treating it as
        # such is how a run reported 8 of 8 beaten while the mod had banked 7.
        # DevTools says it whenever ActiveLevelInterface is null, which covers
        # a finished level torn down, a level that never loaded, and one we
        # navigated away from. Mirror solved all its controller groups, filed
        # seven part checks, never triggered its win condition, and was counted
        # as beaten anyway - so the credits gate sat one token short with every
        # visible check green.
        #
        # Kept as an EXIT from the solve loop, because there is nothing left to
        # solve either way, but reported honestly: the caller decides, and it
        # decides by asking the mod.
        if "LevelComplete " in out:
            return True, text
        if "no level running" in out:
            return "LevelComplete " in text, text

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
            finished = ("LevelComplete " in more
                        or "no level running" in more)
            if not finished:
                # EXHAUSTED: nothing left to solve and the game still will
                # not call it done. Marked in the returned text because
                # that text is the only channel back to the caller - the
                # say() above goes to stdout, and a first attempt at this
                # grepped for that message and so never matched anything.
                text += "\n" + EXHAUSTED_MARK + "\n"
            return finished, text

        if attempt:
            say(6, f"pass {attempt + 1}: {len(todo)} controller(s) still unsolved "
                   f"(a cat trap resets the puzzle)")

        trapped = False
        for i in todo:
            dev(f"solve:{i}", 0.9)
            chunk = log.new()
            text += chunk
            if "cat(s) reset the puzzle" in chunk:
                trapped = True
            if "LevelComplete " in chunk or "no level running" in chunk:
                return True, text

        # The puzzle was knocked over while we were solving it. That is the
        # game working, not the level being unfinishable, so give the pass
        # back - bounded, so a trap arriving every pass still terminates.
        if trapped and refunds < MAX_REFUNDS:
            refunds += 1
            budget += 1
            say(6, f"a cat trap reset the puzzle mid-solve; "
                   f"refunding the pass ({refunds}/{MAX_REFUNDS})")

    if refunds:
        say(6, f"gave up after {budget} passes, {refunds} of them refunded "
               f"for cat traps")
    return False, text


#: Unity FullScreenMode. 0 exclusive, 1 borderless, 2 maximised, 3 windowed.
WINDOWED = 3
WINDOW_SIZE = (1280, 720)

#: Where Unity keeps the player's screen choice.
SCREEN_KEY = r"HKCU\Software\maxinferno\A Little To The Left"

#: The hashed value names Unity generates. They are stable for a given build.
SCREEN_VALUES = {
    # THE BOOLEAN, and leaving it out is why this did not work. Unity keeps
    # "which mode" and "am I fullscreen" as SEPARATE values, and the game reads
    # this one. Setting only the mode left it fullscreen, and on exit the game
    # wrote its own choice back over every key below - so a run that printed
    # "windowed 1280x720" actually played at 1920 borderless and flipped
    # playerPrefs.fullscreen to true in the player's save. Which then failed
    # the campaign-save assertion, for a display setting.
    "Screenmanager Is Fullscreen mode_h3981298716": 0,
    "Screenmanager Fullscreen mode_h3630240806": WINDOWED,
    "Screenmanager Fullscreen mode Default_h401710285": WINDOWED,
    "Screenmanager Resolution Use Native_h1405027254": 0,
    "Screenmanager Resolution Width_h182942802": WINDOW_SIZE[0],
    "Screenmanager Resolution Height_h2627697771": WINDOW_SIZE[1],
}


def force_windowed():
    """Never take over the whole screen.

    A test run should not be able to seize the display. droha asked for this
    directly - a fullscreen game is disruptive to sit next to, and it makes
    every screenshot the size of the monitor.

    Done through the registry rather than the command line on purpose: this
    game shows a configuration dialog when it is given Unity's -screen-*
    arguments, which reads as a hang to anything waiting on the log. The exe
    must be launched with NO arguments.

    Best effort. A missing key means a different game build or a different
    machine, and that is not a reason to fail a run.
    """
    try:
        import winreg
    except ImportError:
        return False

    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                            r"Software\maxinferno\A Little To The Left", 0,
                            winreg.KEY_SET_VALUE | winreg.KEY_QUERY_VALUE) as key:
            for name, value in SCREEN_VALUES.items():
                winreg.SetValueEx(key, name, 0, winreg.REG_DWORD, value)
            # READ BACK. The previous version returned True on a successful
            # write and the caller printed "not fullscreen" on the strength of
            # it, which was a claim about the registry rather than about the
            # game. Reporting what is actually stored costs one read.
            for name, value in SCREEN_VALUES.items():
                if winreg.QueryValueEx(key, name)[0] != value:
                    return False
        return True
    except OSError:
        return False


def launch_and_connect(log, phase, what):
    """Start the game and wait for the run to be up. Returns the log text."""
    if ensure_no_steam_relaunch():
        say(phase, "wrote steam_appid.txt so the game stops restarting itself")
    if force_windowed():
        say(phase, f"windowed {WINDOW_SIZE[0]}x{WINDOW_SIZE[1]}, not fullscreen")
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


def game_is_running():
    """Is the game still there at all?

    Nothing asked this, and the harness could therefore spend the rest of its
    life driving a process that had exited: a run on 2026-09-08 sat printing
    "waiting for the level (7s left)" for minutes after the game was gone,
    because a dead game and a slow one look identical through a log file.
    Every wait in here is really "wait for the game to say something", which a
    closed game never will.
    """
    try:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "@(Get-Process | Where-Object {$_.ProcessName -like '*Little*'})"
             ".Count"],
            capture_output=True, text=True, check=False, timeout=20)
        return not out.stdout.strip().startswith("0")
    except Exception:
        return True          # on doubt, carry on; this must never stop a run


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
    dev("replayselect", 4.0)
    dev("press:Confirm Button", 1.0)

    # A click at the Close button first, then the forced transition.
    #
    # BE HONEST ABOUT WHAT THIS STEP DOES: measured over one run, 8 unwinds,
    # the click was handled 7 times but only ONCE actually reached the title.
    # The other 7 fell through to menu:title below. So this is not what made
    # the run clean.
    #
    # What made the run clean was a change in the MOD: Navigation no longer
    # redirects ReplayMenu.LevelSelect to GoToLevelSelectForLevel, so the game
    # opens its own level select. The identical menu:title that used to throw a
    # NullReferenceException in MenuManager.TransitionMenuOut 6-7 times a run
    # now runs 7 times with zero errors, because it is tearing down a properly
    # opened menu instead of a half-built one.
    #
    # The click is kept because it is free and occasionally saves the forced
    # transition, and press: is used rather than clickbutton: because only
    # press: sends real pointer events - clickbutton: fires onClick.Invoke()
    # and silently does nothing to a control wired through IPointerClickHandler,
    # which is how this button was written off as dead once already.
    # log.new() CONSUMES the buffer, so every read has to be kept - a retry
    # loop that drops what it read would quietly starve the error census of
    # the lines it exists to inspect.
    text = log.new()
    for attempt in range(3):
        dev("press:Close Button", 2.5)
        chunk = log.new()
        text += chunk
        if "no active control named Close Button" not in chunk:
            break
        time.sleep(1.5)

    # Only if every click missed. DevTools skips a transition to a state the
    # game is already in, so this is a no-op when Close worked - and the log
    # says which, because a fallback that is silent is a fallback nobody knows
    # they are relying on.
    dev("menu:title", 3.0)
    return text + log.new()


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

    text += check_pause_exit(log)

    close_game()
    time.sleep(2)
    return got, 1, text


def check_pause_exit(log):
    """Press the pause menu's Exit, with a real run up.

    Done HERE, inside the throwaway arrow session, because the two things this
    needs are a genuine connected run and a genuinely running level - and this
    session already has both. A standalone probe had neither reliably: the
    offline run takes a moment to come up after launch, so pressing Exit too
    early measured the UNPATCHED path and passed for behaviour the fix never
    touched, and forcing the pause menu open with DevTools while the game sat
    on a level select drew both screens at once, which is not a state a player
    can reach.

    What the fix is: vanilla ExitGame decides where to go from the level's
    KIND, and a run's levels leave it with no answer, so it went nowhere - the
    same failure already recorded for LevelSelect. Measured before the fix, the
    click landed and the handler ran and the same twelve buttons were still on
    screen afterwards.

    The assertion is the mod's own line plus the pause menu being gone. Either
    alone is too weak: the line without the menu closing would mean the
    redirect ran and did not work, and the menu closing without the line would
    mean vanilla handled it and the fix never engaged.
    """
    log.new()
    dev("pause", 2.5)
    dev("buttons", 1.5)
    before = log.new()
    if "Resume Button" not in before:
        say(5, "      could not open the pause menu; Exit not checked")
        return before

    # press:, not clickbutton:. The question this check answers is "does Exit
    # work for a user", and a user generates pointerDown / pointerUp /
    # pointerClick - which is what press: sends. clickbutton: calls
    # onClick.Invoke() and reaches only serialised listeners, so it can pass
    # for a control a real click would never reach.
    dev("press:Exit Button", 3.0)
    dev("buttons", 1.5)
    after = log.new()

    redirected = "Exit -> the title screen" in after
    closed = "Resume Button" not in after
    say(5, f"      Exit: redirect ran={redirected}, pause menu closed={closed}")

    # A line the assertion can read. Both halves matter and only one was being
    # checked: the marker alone would pass for a redirect that ran and left the
    # player staring at the pause menu, which is the original bug wearing a
    # log line.
    verdict = (f"harness: exit check redirect={redirected} closed={closed}\n")
    return before + after + verdict


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
    #: Slots a Skip has already been spent on - one each, at most.
    skipped = set()
    credits = False
    idle = 0
    last_done = True   # nothing is running yet; see the boot site

    # STUCK IS NOT THE SAME AS SLOW, and the loop used to treat them alike.
    #
    # idle counts rounds that finished nothing, and stops after 2 per slot.
    # That is right for a run waiting on abilities: a slot that cannot be
    # finished yet is normal, and the next item may unblock it. It is quite
    # wrong for a level that will not LOAD - that never recovers, and at about
    # 45 seconds a round the harness spent ten minutes re-proving it before
    # anyone looked. A run that has broken should say so while it is still
    # worth reading, not eventually.
    unopened = 0            # boots that produced no level, in a row
    MAX_UNOPENED = 3
    last_progress = time.time()
    STALL_SECONDS = 420     # nothing beaten for seven minutes

    # Slots the mod already had a Beaten token for when this session connected -
    # the arrow session beats one, and it will never file a second token for it.
    restored = set()

    first = launch_and_connect(log, 5, "the connection")
    if "connected. " not in first:
        return [], False, first, 0, first
    transcript = first
    open_slots = open_count(first, plan["boundaries"][0])

    say(6, f"{len(slots)} slots, boundaries {plan['boundaries']}, "
           f"{open_slots} open")

    # "run state: ... N puzzle(s) beaten" is the mod saying how many it
    # brought with it. Which ones is not logged, so the harness cannot know
    # WHICH slots - but it can stop demanding a fresh token for that many.
    for line in transcript.splitlines():
        if "run state:" in line and "puzzle(s) beaten" in line:
            try:
                n = int(line.split("hint page(s) opened, ")[1].split(" puzzle")[0])
            except (IndexError, ValueError):
                n = 0
            restored = set(range(n))

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

            # Only unwind through the menus after a level was FINISHED.
            #
            # to_title exists to get off the completion screen, and it does
            # that by navigating - which DEACTIVATES the level it leaves.
            # boot:'s teardown destroys active levels only, so a deactivated
            # one survives with its CheckWinCondition still subscribed, and
            # every later synthetic solve dies inside the old level's handler.
            #
            # That hole was harmless while every round ended in a completion.
            # Once ability locks started gating the early slots, "boot a level,
            # fail to finish it, try another" became the common path and the
            # leak compounded: one run threw 144 times and beat 1 of 8, all of
            # them NullReferenceException in LevelInterface.CheckWinCondition.
            #
            # An unfinished level is still ACTIVE, so booting straight over it
            # lets the teardown do its job. Nothing to unwind, nothing to leak.
            if last_done:
                transcript += to_title(log)
            opened, out = boot_level(log, index)
            transcript += out
            if not opened:
                if not game_is_running():
                    say(6, "STOPPING: the game is no longer running. Everything "
                           "after this would be the harness talking to itself.")
                    break
                unopened += 1
                say(6, f"round {step}: slot {current} {level_id} did not open "
                       f"({unopened} in a row)")
                current, idle = None, idle + 1
                if unopened >= MAX_UNOPENED:
                    say(6, f"STOPPING: {unopened} boots in a row produced no "
                           f"level. That is a broken harness or a broken build, "
                           f"not a run waiting on items - the remaining rounds "
                           f"would only repeat it.")
                    break
                if idle >= len(slots) * 2:
                    break
                continue
            unopened = 0

        index, level_id = slots[current]
        attempts[current] += 1

        done, chunk = solve_level(log)
        transcript += chunk
        tail = log.wait(["beaten:", "check:", "credits:"], 10, 6, "the check")
        transcript += tail

        # A LEVEL THAT CANNOT BE FORCED GETS SKIPPED, ONCE.
        #
        # solve: sets a controller's solved flag and dispatches the event.
        # That is enough for most levels and not enough for a PHASED one:
        # PawPrints registers five controllers, all five solve, all five
        # checks fire, and PawPrintsPhaseLevel still never raises
        # LevelComplete because its phase machine wants the real solve
        # path. The mod is right to bank no Beaten token; the harness is
        # simply unable to finish that puzzle.
        #
        # It used to loop on it - nineteen rounds, 441 seconds, then stop
        # one puzzle short of the credits and fail six assertions that had
        # nothing wrong with them. Phased campaign levels became drawable
        # in 0.3.1, so this stopped being hypothetical.
        #
        # A Skip is the player's own answer to a puzzle they cannot do,
        # and since 0.3.1 it finishes the slot and counts toward the
        # credits. Spending one here keeps the run honest AND exercises
        # the Skip item, which nothing else in this harness touches.
        #
        # Once per slot, and only when the level is EXHAUSTED - every
        # controller solved with no completion. A skip on a level that
        # still has unsolved controllers would paper over a real routing
        # bug, which is the opposite of what this is for.
        # Only the truly exhausted case. A level with controllers still
        # unsolved is a level the harness gave up on, and skipping that
        # would hide a real routing or gating bug behind a green run.
        exhausted = EXHAUSTED_MARK in chunk
        if not done and exhausted and current not in skipped:
            skipped.add(current)
            log.new()
            dev("skip", 1.5)
            more = log.wait(["beaten:", "skip:", "check:"], 12, 6,
                            "the skip")
            transcript += more
            chunk += more
            tail += more
            if "skip: spent one" in more:
                say(6, f"slot {current} {level_id} cannot be force-solved; "
                       f"spent a Skip")
            elif "skip:" in more:
                say(6, f"slot {current} {level_id} cannot be force-solved "
                       f"and there was no Skip to spend")
            done = "beaten:" in more

        blocked = ""
        for line in (chunk + tail).splitlines():
            if "waiting on " in line:
                blocked = " - waiting on " + line.split("waiting on ", 1)[1].strip()

        # The mod's word is final. A level is beaten when the mod banks its
        # Beaten token, not when the harness runs out of controllers to solve.
        # A level already beaten in the arrow session files no new token, so a
        # slot the mod restored at connect counts too.
        if done and "beaten:" not in (chunk + tail) and current not in restored:
            done = False
            blocked = blocked or " - solved, but the mod banked no Beaten token"

        last_done = done
        if done:
            beaten[current] = level_id
            idle = 0
            last_progress = time.time()
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

        # The wall clock, as a backstop to the round counters. Rounds can be
        # slow for legitimate reasons, so this is generous - but a run that has
        # beaten nothing for seven minutes is not going to.
        stalled = time.time() - last_progress
        if stalled > STALL_SECONDS:
            say(6, f"STOPPING: nothing has been beaten for {int(stalled)}s. "
                   f"{len(beaten)}/{len(slots)} done, {open_slots} open.")
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
    parser.add_argument("--quick", action="store_true",
                        help="three puzzles, no arrow session - for iterating. "
                             "The full run is the release gate.")
    args = parser.parse_args()
    assets = os.path.join(REPO, args.assets)

    global QUICK
    if args.quick:
        QUICK = True
        print("QUICK MODE: no arrow session, ability locks off, no cat traps. "
              "Run without --quick before a release.", flush=True)

    if port_open():
        print(f"port {PORT} is already in use. A leftover MultiServer there "
              "would be silently tested against instead of this run's - stop "
              "it first.", flush=True)
        return 1

    campaign_before = campaign_progress(CAMPAIGN)
    dailies_before = daily_completions(CAMPAIGN)

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

    say(4, f"generating {PUZZLES} puzzles, asking for packs of {PACK_SIZE} "
           f"(the cap decides how many)")
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
        if QUICK:
            # Skipped, and the assertions that depend on it are skipped with
            # it rather than silently passing on an empty transcript - a check
            # that cannot fail is worse than one that is absent.
            say(5, "quick mode: skipping the arrow session")
            got, expected, arrow_text = None, None, ""
        else:
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
        # Against the boundaries the GENERATOR chose, not a number written
        # here. The two are allowed to differ - the pack cap widens packs
        # to fit - and when they do it is the generator that is right.
        # What must never differ is the generator's plan and the mod's
        # reading of it, which is the thing worth asserting.
        want_packs = max(0, len(plan["boundaries"]) - 1)
        results.append((f"the mod sees the generator's {want_packs} pack(s)",
                        f"{want_packs} packs of" in connected))
        results.append(("the run is more than its free opening",
                        want_packs >= 1))
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
        errors = error_census(whole)
        print(f"      launches: {launches}; arrow presses: {arrows}; "
              f"solve exceptions: {threw}", flush=True)
        # Two: one throwaway for the arrow, one for the run. See check_arrow.
        # Quick mode runs only one session, so the expected count changes with
        # it rather than the check being dropped.
        results.append(("two game launches, no more" if not QUICK
                        else "one game launch, no more",
                        launches == (1 if QUICK else 2)))
        # The arrow is the route a player uses, so the harness uses it too -
        # a run that quietly fell back to opening every level itself would
        # still pass everything else while testing none of that navigation.
        # The player's route, asserted: the arrow opened the slot the run says
        # is next. Pressed once - see play() for why it is not how the harness
        # advances.
        # Both live in the arrow session. In quick mode they are OMITTED, not
        # passed - a skipped check must not look like a green one, or the
        # cheap run starts getting mistaken for the full one.
        if not QUICK:
            results.append(("the pause menu Exit leaves the level",
                            "harness: exit check redirect=True closed=True" in whole))
            results.append(("the next-level arrow opens the run's next puzzle",
                            got is not None and got == expected))

        results.append(("no solve threw inside the game", threw == 0))

        # EVERY error, not just the one kind this harness happened to grep for.
        #
        # Until 2026-09-07 the only failure counted was "solve failed", so a
        # run could report "solve exceptions: 0" while the game logged dozens
        # of others and the harness called it green. droha spotted it in a log:
        # to_title's unwind was failing at every step -
        #
        #     SetGameState for Type Levels_GameState failed
        #     press: no active control named Confirm Button
        #     SetGameState: Levels_GameState already active
        #     menu failed: NullReferenceException at
        #         MenuManager.TransitionMenuOut / CloseActiveMenu
        #
        # - and none of it was counted, reported, or assertible. A harness that
        # greps for one string is not measuring the game's health; it is
        # measuring its own vocabulary.
        new_errors = unexplained(errors)
        if errors:
            print("      errors logged during the run:", flush=True)
            for signature, n, before in errors:
                tag = "" if (signature, n, before) in new_errors else "  [known]"
                print(f"         {n:4d}  {signature}{tag}", flush=True)
                if (signature, n, before) in new_errors:
                    for prior in before:
                        print(f"               after: {prior[:120]}", flush=True)
        results.append(("the game logged no unexplained errors", not new_errors))

        # The location table against the running game, asserted rather than
        # logged. See table_audit.
        gaps = table_audit(whole)
        if gaps:
            print("      the shipped table disagrees with the running game:",
                  flush=True)
            for line in gaps[:10]:
                print(f"         {line[:160]}", flush=True)
        results.append(("the controller table matches every level played",
                        not gaps))

        results.append((f"all {PUZZLES} puzzles beaten", len(beaten) >= PUZZLES))

        # The mod's tally, beside the harness's. These measure the same thing
        # from opposite sides, and when they disagree the harness is wrong -
        # it counts what it did, the mod counts what was banked.
        banked = len(set(l.split("beaten: ")[1].strip()
                         for l in whole.splitlines() if "beaten: " in l))
        print(f"      the mod banked {banked} Beaten token(s) this session",
              flush=True)
        # Restored is read from the transcript here rather than passed out of
        # play(): the mod states it at connect, and main() has the whole log.
        carried = 0
        for line in whole.splitlines():
            if "run state:" in line and "puzzle(s) beaten" in line:
                try:
                    carried = max(carried, int(
                        line.split("hint page(s) opened, ")[1].split(" puzzle")[0]))
                except (IndexError, ValueError):
                    pass
        print(f"      the mod carried {carried} in and banked {banked} here",
              flush=True)
        results.append(("the mod agrees every puzzle was beaten",
                        banked + carried >= PUZZLES))
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

    campaign_after = campaign_progress(CAMPAIGN)
    if campaign_after != campaign_before:
        print(f"      campaign fields that moved: "
              f"{campaign_diff(campaign_before, campaign_after)}", flush=True)
    results.append(("the campaign progress is untouched",
                    campaign_after == campaign_before))
    results.append(("the run credited no real daily",
                    daily_completions(CAMPAIGN) == dailies_before))
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

    # THE EXHAUSTION MARKER MUST BE PRODUCED AND CONSUMED. Both sides go
    # through EXHAUSTED_MARK now, so this only has to prove the constant
    # is actually reached from both - a splice that reintroduced a literal
    # on either side would drop the count below two.
    src = open(__file__, encoding="utf-8").read()
    if src.count("EXHAUSTED_MARK") < 3:
        sys.exit("self-test: EXHAUSTED_MARK is no longer used on both sides")

    # The display fix. Unity keeps the mode and the boolean separately and
    # the game reads the boolean; setting only the mode let a run play at
    # 1920 borderless while printing "not fullscreen".
    if "Screenmanager Is Fullscreen mode_h3981298716" not in SCREEN_VALUES:
        sys.exit("self-test: force_windowed would not actually leave fullscreen")
    if SCREEN_VALUES["Screenmanager Is Fullscreen mode_h3981298716"] != 0:
        sys.exit("self-test: the fullscreen boolean must be 0")

    # campaign_diff names the fields that moved. The bare FAIL it replaced
    # read as "the run wrote the player's campaign save" when what had
    # actually changed was a display setting.
    before = json.dumps({"levelCompletionData": [1], "playerPrefs": {"a": 1}},
                        sort_keys=True)
    after = json.dumps({"levelCompletionData": [1], "playerPrefs": {"a": 2}},
                       sort_keys=True)
    if campaign_diff(before, after) != "playerPrefs":
        sys.exit(f"self-test: campaign_diff named "
                 f"{campaign_diff(before, after)!r}, expected 'playerPrefs'")
    if campaign_diff(before, before) != "nothing":
        sys.exit("self-test: campaign_diff should say nothing moved")
    if "could not be read" not in campaign_diff(None, after):
        sys.exit("self-test: campaign_diff should cope with an unreadable save")

    # Every helper play() reaches for must exist. Splicing this file has twice
    # replaced a region that happened to contain one - boot_level went missing
    # that way, and the run died two minutes in with a NameError that stderr
    # had been redirected away from. A name lookup costs nothing and fails
    # before the game is ever launched.
    for name in ("launch_and_connect", "open_count", "loaded_level", "to_title",
                 "check_pause_exit", "error_census", "unexplained", "table_audit",
                 "force_windowed",
                 "game_is_running",
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
                elif isinstance(node, ast.Lambda):
                    # Lambda parameters are bindings too. Without this the
                    # checker reported error_census()'s sort key as an
                    # undefined name and refused to run the whole harness -
                    # a false positive that is worse than the misses it was
                    # written to catch, because it blocks rather than warns.
                    for a_ in node.args.args:
                        bound.add(a_.arg)
                    for a_ in node.args.posonlyargs + node.args.kwonlyargs:
                        bound.add(a_.arg)
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
