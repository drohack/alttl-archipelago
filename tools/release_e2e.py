"""Install the release the way a player does, and play a short run to the end.

Every other harness here tests the working tree. This one tests the RELEASE:
the mod from its zip, the world from its .apworld with no loose copy anywhere,
a seed generated from a yaml, a real MultiServer, and a run played to the
credits. It is the only test that would catch a release that is broken only
as a release.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/release_e2e.py 2>/dev/null

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
import re
import shutil
import socket
import subprocess
import sys
import time
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import (CONFIG_DIR, EXE, GAME, SAVE_DIR, SCREEN_KEY,
                         close_game, ensure_no_steam_relaunch,
                         restore_snapshot, take_snapshot)

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
PLUGIN_DIR = os.path.join(GAME, "BepInEx", "plugins", "ALTTLArchipelago")
MOD_CONFIG = os.path.join(CONFIG_DIR, "droha.alttl.archipelago.cfg")
DEVTOOLS_CONFIG = os.path.join(CONFIG_DIR, "droha.alttl.devtools.cfg")
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
#: call, not ours - at 8 puzzles the cap is 1, and MIN_OPENING raises the
#: size to 5, so the run opens 5 free and the single pack carries the
#: remaining 3. Asserting a pack count here instead of reading the
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

#: Written into the transcript when the harness starts going back over levels
#: it has already beaten, so the phase is visible when reading a run. Nothing
#: is excluded on the strength of it - see the error census, which used to.
REVISIT_MARK = "harness: revisit pass begins"

#: The game's credits "level". Found by IsCredits at runtime rather than by
#: this number anywhere in the mod - it is only here because clickcard: takes
#: a level index, and the one the mod reports is stable across the build.
CREDITS_LEVEL_INDEX = 84
MAX_ROUNDS = 60
QUICK = False

#: --dlc plays the same run out of DLC content instead of the base game.
#:
#: The whole gate over DLC puzzles, which is what tools/probe-dlc.py does
#: not cover: that probe launches one level and stops, where this plays a
#: run to the credits and asserts the goal reaches the server. The parts
#: most likely to differ are the ones AFTER a solve - pack progression,
#: the beaten count, the credits card - and none of those care which
#: puzzle it was until they do.
DLC = False

TOTAL = 7


def say(phase, msg):
    print(f"[{phase}/{TOTAL}] {msg}", flush=True)


#: Every line this gate prints also lands here, overwritten at the start of
#: each run, so a run in progress can be watched without a status bar.
#:
#: The gate takes about fifteen minutes and prints throughout, but that output
#: only ever existed in whatever file the caller happened to redirect into -
#: a path nobody else could guess. droha runs it from an editor pane that
#: shows neither the background process nor its output: "i don't even see if
#: you have a process running in the background". A FIXED path is the whole
#: point. Open it once, leave it open, and the editor reloads it as it grows.
PROGRESS = os.path.join(REPO, "testserver", "logs", "e2e-progress.txt")


class Tee:
    """stdout that also writes the transcript to a file.

    Wraps the stream rather than replacing it, so the console still streams
    normally and a caller's own redirect keeps working. Every write is
    flushed: a watcher tailing the file should see a line when it is printed,
    not when a block buffer happens to fill.

    A failure to write the copy must never take the run down with it - the
    file is a convenience and the gate is the point - so the file side is
    wrapped and swallowed while the real stream is not.
    """

    def __init__(self, stream, path):
        self.stream = stream
        self.file = None
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            self.file = open(path, "w", encoding="utf-8", errors="replace")
        except Exception as e:
            stream.write(f"could not open {path}: {e}\n")

    def write(self, text):
        n = self.stream.write(text)
        if self.file is not None:
            try:
                self.file.write(text)
                self.file.flush()
            except Exception:
                self.file = None
        return n

    def flush(self):
        self.stream.flush()
        if self.file is not None:
            try:
                self.file.flush()
            except Exception:
                self.file = None

    def isatty(self):
        return self.stream.isatty()


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
#: Fields the campaign save may legitimately differ in after a run.
#:
#: playerPrefs is the interesting one and it was NOT here until 0.3.3, which
#: means this check had been passing by luck. SaveRedirect.MirrorSettingsToCampaign
#: copies playerPrefs from the run save into the campaign save ON PURPOSE -
#: "SETTINGS ONLY, and that is the whole design", because droha's volume and
#: resolution changes made inside a run must not be lost when they go back to
#: the campaign. So a run is EXPECTED to move this field, and the check only
#: stayed green while no setting happened to change during a run. It went red
#: the first time one did: the harness sets a window size, the mod records the
#: resolution, and the mirror does its job.
#:
#: What still protects the player is unchanged: levelCompletionData and every
#: other progress field are compared, the daily count is pinned separately,
#: and SettingsSpliceTests in Core asserts the splice touches playerPrefs and
#: nothing else - which is the property that makes ignoring it safe here.
#: Fields in the campaign save that are not campaign PROGRESS, so a run
#: touching them is not the failure this check exists to catch.
#:
#: Everything that carries progress is still compared: levelCompletionData,
#: archiveCompletionData, lastPlayedLevels, newGamePlus and the rest. Each
#: name below is here because it moved for a reason that had nothing to do
#: with the run, and left this check crying wolf.
#:
#: installedDlc is the newest, added 2026-09-17. The game records which DLC
#: it authenticated at startup, so the FIRST launch after buying one
#: rewrites it - which looked exactly like a run writing to the campaign
#: save. It is not:
#:
#:   - the mod never writes it. Its only mention of DLC installation is
#:     Connection.InstalledDlcOrNull, which READS DLCManager.DLCInfo to
#:     decide whether to refuse a seed.
#:   - the game saves without the mod at all. tools/levelsweep.py parks the
#:     plugin out of BepInEx entirely and the game still logs "Game Saved
#:     to: save1.json" during those runs.
#:
#: So it is the game keeping its own books about what the player owns, and
#: nothing the mod could prevent or should be blamed for.
IGNORED_CAMPAIGN_FIELDS = ("saveTimestamp", "dailyTidyProgress",
                           "playerPrefs", "installedDlc")


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


def dlc_completions(path):
    """DLC puzzles recorded in a save, by level id.

    THERE IS NO SEPARATE DLC SAVE, and that is worth stating because the
    obvious assumption is the opposite. The game keeps one file and
    levelCompletionData is a flat list with no scoping - campaign chapters and
    DLC puzzles sit in the same array, distinguished only by a "DLC1 " or
    "DLC2 " prefix on the level id. So SaveRedirect, which rewrites
    GetSavePath wholesale, protects DLC progress for free and there is no
    second path to patch.

    campaign_progress already compares levelCompletionData, so a leak would
    fail the untouched check anyway. This exists to make the failure SAY so:
    "a DLC puzzle was written to the campaign save" is a diagnosis, while
    "the campaign progress is untouched: FAIL" is an alarm that has already
    fired twice for things that were not this.
    """
    data = read_save(path)
    if data is None:
        return None
    rows = data.get("levelCompletionData") or []
    return sorted(r.get("levelId", "") for r in rows
                  if str(r.get("levelId", "")).startswith("DLC"))


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
    # TupperwareTower registers Foundation and Falling Blocks, both
    # StackableGrid, and the table lists only the Tower. That is DELIBERATE and
    # already measured: they are the tower's mechanism rather than objectives,
    # they never raise a solved event, and as locations they could never be
    # earned. tools/probe-dead-controllers.py established it and
    # test_fill_stress.py's split comment records the day they were deleted -
    # "Now (24, 94). TupperwareTower lost three groups and gained one back".
    # The level still requires Grids through extraAbilities, because the
    # falling blocks are dimmed without it.
    #
    # It belongs on this list and was missed when those locations went. Absent
    # from it, the gate failed every run on a gap the project had already
    # decided about - and the obvious-looking repair, putting the two
    # locations back, re-creates two checks no player can collect.
    "TupperwareTower",
)


#: Levels the harness cannot force to completion, so a Skip is the only way it
#: has to finish the slot. Asserted, not merely tolerated - see the two checks
#: on the skip record.
#:
#: `solve` sets a controller's solved flag and dispatches the event. That
#: finishes most levels and does NOT finish a phased one: its phase machine
#: waits for the real drag path, which synthetic pointer input cannot produce
#: because DragObject has no OnDrag and nothing starts the settle tween. The
#: level is then EXHAUSTED - every controller solved, no LevelComplete - and
#: the harness spends a Skip, which is what a player stuck on that puzzle
#: would do anyway.
#:
#: THIS LIST IS A LEDGER OF WHAT IS UNPROVEN, not a list of things that are
#: fine. A level here is beaten in the transcript without ever having been
#: solved, so the gate says nothing about whether a human can finish it. That
#: is a manual item in docs/release-testing.md, deliberately not automated:
#: pretending a Skip proves solvability is the failure this list exists to
#: stop being invisible.
#:
#: Adding a name here is a decision to stop testing that level's solve path.
#: Measure first - the far likelier cause of a new entry is a regression in
#: solve routing or ability gating, which is exactly what the check is for.
KNOWN_UNFORCEABLE = (
    # Registers Foundation, Tower and Falling Blocks; only the Tower is an
    # objective. Solving it leaves the tower standing but the level unfinished
    # - see KNOWN_TABLE_GAPS above for why the other two are not locations.
    #
    # PLAYED BY HAND, 2026-09-15, holding exactly the two abilities the table
    # declares and nothing else: "beat the level just fine, no issues moving
    # tupperwear". The mod logged `abilities: 0 locked, 3 open` - nothing on
    # the level was dimmed - then Solution 1 and the Beaten token. So the
    # narrowed table is SUFFICIENT, which is the thing an earlier playthrough
    # could not settle: that one was done while the abilities were overstated,
    # so it was performed holding more than the level asks for.
    "TupperwareTower",
    # ANSWERED BY HAND, 2026-09-15, and the answer is that the level is fine.
    # droha played it holding exactly the four abilities the table declares,
    # with `abilities: 0 locked, 7 open`, and beat it in full: all five part
    # checks, Solution 1 and the Beaten token.
    #
    # The reason the harness cannot is `Computer Errors` - a sequence you
    # START and FINISH, in droha's words, rather than an arrangement you tidy.
    # Zero objects, dependsOn Computer Desktop, needs Gadgets. Forcing its
    # solved flag sets a bit for a sequence that never ran, which explains all
    # three things the gate sees: six controllers at solved=True with no
    # LevelComplete, the flag needing to be forced twice before it stuck, and
    # the level's own solvedNow sitting at 1 while six flags flipped.
    #
    # It is NOT a phase level - levelClass is `DesktopComputer` - and this
    # comment said "phased" for one release because that was the nearest
    # familiar shape, not because anyone looked.
    "Desktop Computer",
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


def write_devtools_config():
    """Silence the run.

    A gate plays real sessions for a quarter of an hour, and until now it did
    so with the music and the effects going on whichever machine happened to
    be running it. droha: "can you set the tests to have the music and sf
    muted?"

    MuteAudio holds AudioListener.volume at zero rather than touching the
    player's own volume settings, which live inside the encoded save next to
    actual progress. Nothing here is persisted and nothing of theirs moves.

    Partial on purpose: BepInEx fills in every key not named here with its
    default, so this says the one thing it means to say.
    """
    with open(DEVTOOLS_CONFIG, "w", encoding="utf-8", newline="\n") as f:
        f.write("[Debug]\n"
                "MuteAudio = true\n")


def generate():
    yaml_dir = os.path.join(REPO, "testserver", "yaml-e2e")
    out = os.path.join(REPO, "testserver", "out-e2e")
    for d in (yaml_dir, out):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))

    # Under --dlc every source but the two DLCs is switched off, so every
    # slot is a DLC puzzle and the run cannot pass on base-game content by
    # accident. Appended AFTER the block below rather than spliced into it:
    # a `+` in the middle of adjacent string literals ends the implicit
    # concatenation and every literal after it becomes a syntax error.
    dlc = ("  cupboards_and_drawers: true\n"
           "  seeing_stars: true\n"
           "  cupboards_weight: 50\n"
           "  stars_weight: 50\n"
           "  generator_weight: 0\n"
           "  archive_weight: 0\n"
           "  base_weight: 0\n"
           "  mechanic_coverage: 0\n"
           "  archive_packs: []\n"
           # FOUR SKIPS IN HAND, and this is about the harness rather
           # than the content. Drawer and cupboard levels are common
           # in Cupboards and Drawers, and the harness cannot pull a
           # drawer open - it sets a controller's solved flag, and a
           # drawer is an interaction, not an arrangement. So a level
           # can be beaten with one of its locations unearned, and an
           # item placed there strands the run.
           #
           # A PLAYER NEVER HITS THIS: droha played Game Pieces, Tea
           # Cabinet and Robots by hand on 2026-09-17 and every one of
           # those locations fired. The Skips exist so the harness can
           # work around its own limitation, the same way
           # KNOWN_UNFORCEABLE already does for TupperwareTower.
           #
           # GRANTED UP FRONT rather than raising skip_count, because
           # a Skip placed behind a closed slot is no use to a run that
           # is stuck precisely because its slots will not open. The
           # first attempt at this failed exactly there: "there is no
           # Skip to spend yet", eight rounds running.
           "  start_inventory:\n"
           "    Skip: 4\n"
           # LOCATIONS THE HARNESS CANNOT EARN, kept clear of
           # progression. Every one is a container - a drawer you pull
           # open, a cupboard door you swing - and the harness solves
           # by setting a controller's solved flag, which is not the
           # same thing. The group therefore never reports.
           #
           # A PLAYER EARNS THESE NORMALLY. droha played Game Pieces,
           # Tea Cabinet and Robots by hand on 2026-09-17 and every one
           # of these locations fired; see docs/manual-container-test.md.
           # So this is not a statement about the game, it is the
           # harness declaring its own blind spot so the generator does
           # not put the run's only Puzzle Pack behind it - which is
           # exactly what stranded three slots twice running.
           #
           # A Skip DOES rescue it, and this comment used to say the
           # opposite. It claimed the payout was filled from the
           # LevelComplete a skip triggers and that "a level that is
           # already beaten never fires one again". Measured 2026-09-18
           # by tools/probe-skip-beaten.py: it fires. Re-entering a
           # beaten puzzle reloads it, and a reloaded level completes
           # and skips like any other, so the remaining locations are
           # sent. The premise had to be wrong - the only way to press
           # Skip is to be standing in a loaded level.
           "  exclude_locations:\n"
           "    - Clock Cupboard (Cupboards and Drawers) - Cupboard Doors\n"
           "    - Craft Supplies (Cupboards and Drawers) - Drawers\n"
           "    - Daggers (Cupboards and Drawers) - Drawers\n"
           "    - Game Pieces (Cupboards and Drawers) - Drawers\n"
           "    - Jewelry Box (Cupboards and Drawers) - Drawers\n"
           "    - Nested Drawers (Cupboards and Drawers) - Drawers\n"
           "    - Sewing Box (Cupboards and Drawers) - Drawers\n"
           "    - Tea Cabinet (Cupboards and Drawers) - Cupboard Doors\n"
           "    - Combs (Seeing Stars) - Drawer\n"
           "    - Junk Drawer Transforming (Seeing Stars) - Drawer\n"
           "    - Material Drawers (Seeing Stars) - Drawer Controller\n"
           "    - Robots (Seeing Stars) - Spring\n"
           "    - Robots (Seeing Stars) - Spring Containable\n"
           "    - Sticky Drawer (Seeing Stars) - Drawer\n") if DLC else ""

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
            "  accessibility: full\n"
            + dlc)

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


#: Controller types that cannot be force-solved and never carry a location.
#: Measured across the whole level table: of thirty-nine types, Pannables is
#: the only one no level ever makes a location of. See
#: src/ALTTLArchipelago.Core/ControllerTypes.cs, which holds the same list for
#: the mod's own audit.
#: A plain substring, not a regex. The first version used one, and the
#: "\b" in its pattern reached this file as a literal backspace
#: character - it printed as type=Pannables, matched nothing, and the
#: self-test below was the only reason it did not ship silently
#: skipping nothing at all.
NEVER_SOLVABLE = "type=Pannables"


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
        if not body.rstrip().endswith("solved=False"):
            continue

        # SCENERY IS NOT WORK. A Pannables controller carries no location in
        # any of the ten levels that have one, and cannot be force-solved:
        # SetSolved takes, but dispatching the solved event through the game's
        # own handler throws ArgumentOutOfRange, so it reports unsolved again
        # on the next pass. Every retry then went on it, the budget ran out,
        # and the level was called unfinishable - which is how Desktop
        # Computer held the 0.3.2 gate at 7 of 8.
        #
        # Matched on the type the listing prints, not the controller's name,
        # because the names are per-level and the type is the fact.
        if NEVER_SOLVABLE in body:
            continue

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

#: The hashed value names Unity generates. Stable for a given build.
MODE_VALUE = "Screenmanager Fullscreen mode_h3630240806"
WIDTH_VALUE = "Screenmanager Resolution Width_h182942802"
HEIGHT_VALUE = "Screenmanager Resolution Height_h2627697771"


def display_setting():
    """What the player has the game set to. NEVER writes.

    THIS USED TO FORCE WINDOWED AND IT WAS A MISTAKE. droha asked, reasonably,
    that a test run not seize the screen, and the answer was to write six
    registry values before every launch. Three things went wrong with that:

      - It did not work. The game applies its own `playerPrefs` from
        save1.json after startup, so it launched fullscreen anyway while the
        harness printed "windowed 1280x720, not fullscreen" on the strength of
        having written the registry.
      - The game rewrites those keys from its RUNTIME state on exit, so every
        run left the display wherever the run had ended up - including on the
        wrong monitor.
      - It kept overwriting a setting that belongs to the person at the
        keyboard. droha, after the third time: "why does it re-write those? It
        shouldn't! That's the whole thing I've been trying to tell you."

    The setting is already what they want. Reading it and saying so gets the
    same outcome, and a run that finds fullscreen can warn instead of
    silently changing it.
    """
    try:
        import winreg
    except ImportError:
        return None

    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, SCREEN_KEY, 0,
                            winreg.KEY_QUERY_VALUE) as key:
            mode = winreg.QueryValueEx(key, MODE_VALUE)[0]
            width = winreg.QueryValueEx(key, WIDTH_VALUE)[0]
            height = winreg.QueryValueEx(key, HEIGHT_VALUE)[0]
        return mode, width, height
    except OSError:
        return None


#: Every feature Plugin patches. If one is missing from "features live", it
#: was defined and never installed; if it appears after "FEATURES DISABLED",
#: the patch threw at runtime and the whole class is off.
EXPECTED_FEATURES = ("save redirect", "connection pane", "track", "skips",
                     "hints", "navigation", "daily guard", "title screen")


def whole_log():
    """The whole of the last session's log, for questions about startup.

    The Log class hands out new text since the last read, which is right for
    waiting on something and wrong for asking "what did this launch say about
    itself" - the startup lines were consumed long before step 7.
    """
    try:
        with open(LOG, "r", encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def patch_problem(text):
    """What a launch says about its own patching, or None if all is well.

    THE CHECK NOBODY WAS DOING. DlcGuard spent an entire feature's development
    with its Harmony attributes never installed, because it was missing from
    Plugin's patch list - and the game announced that at every single launch
    in the "features live" line, which no test ever read. Registering it then
    produced "PATCH FAILED, dlc guard IS DISABLED: IL Compile Error", which a
    STATIC check cannot see at all: the class is registered and carries
    attributes, and the patcher still refuses it at runtime.

    tools/check-patches.py covers the static half. This is the other half, and
    both are needed - each catches a failure the other cannot.
    """
    if "PATCH FAILED" in text or "FEATURES DISABLED" in text:
        for line in text.splitlines():
            if "PATCH FAILED" in line or "FEATURES DISABLED" in line:
                return line.split("] ", 1)[-1].strip()
        return "a patch failed"

    live = line_with(text, "features live:")
    if not live:
        return "the launch never said which features are live"
    missing = [f for f in EXPECTED_FEATURES if f not in live]
    if missing:
        return f"features never installed: {', '.join(missing)}"
    return None


def describe_display():
    """One line for the log, and whether it is going to take the screen."""
    now = display_setting()
    if now is None:
        return "display setting unknown", False
    mode, width, height = now
    windowed = mode == WINDOWED
    names = {0: "exclusive fullscreen", 1: "borderless fullscreen",
             2: "maximised", 3: "windowed"}
    return (f"{names.get(mode, f'mode {mode}')} {width}x{height}"
            + ("" if windowed else " - the player's own setting, left alone"),
            windowed)


def launch_and_connect(log, phase, what):
    """Start the game and wait for the run to be up. Returns the log text."""
    if ensure_no_steam_relaunch():
        say(phase, "wrote steam_appid.txt so the game stops restarting itself")
    what_display, _windowed = describe_display()
    say(phase, what_display)
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
    #: Beaten slots the run is stuck behind, to be cleared with a Skip.
    #: See the stall handler below for when this is populated.
    skip_anyway = set()
    #: [(level_id, was_still_ability_gated, reason)] for every Skip spent.
    #: reason is "unforceable" - the level could not be beaten - or
    #: "unreachable-check" - it was beaten and an item sat on one of its
    #: locations the harness cannot work. Only the first kind is judged
    #: against KNOWN_UNFORCEABLE.
    #: The gate asserts against this rather than letting a skipped level read
    #: as an ordinary win, which is how "2 of 8 were never solved" stayed
    #: buried in the transcript.
    spent = []
    #: Slots gone back to after everything was beaten, to pick up checks that
    #: were gated by an ability at the time. One pass each; see the revisit
    #: block below for why it exists and why it cannot spin.
    revisited = set()
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
        # ALL BEATEN IS NOT THE SAME AS DONE. Beating every puzzle leaves
        # behind any part that was gated by an ability at the time, and the
        # goal can sit behind exactly one of those - see the revisit block
        # below. Stop when the credits are open, or when a revisit pass has
        # been spent on every slot and there is nothing further to try.
        if len(beaten) >= len(slots) and (credits or revisited >= beaten.keys()):
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

            # GO BACK FOR WHAT YOU COULD NOT REACH THE FIRST TIME.
            #
            # Beating every puzzle is not the same as collecting every check.
            # A part gated behind an ability you did not hold stays uncollected
            # when the level is finished, and the ability may arrive much
            # later - which is the whole point of partial solving, and exactly
            # what a player does about it: walk back and tidy the drawer now
            # that Drawer has turned up.
            #
            # The harness never did. A run beat 8 of 8 and still failed the
            # goal, because the Credits item sat on a Drawer part of a level
            # finished six rounds before Drawer arrived - from a Skip spent on
            # the very last puzzle. Nothing was wrong with the seed or the mod.
            #
            # One pass per slot, so this cannot spin: a level revisited and
            # still short is a level whose remaining checks are genuinely out
            # of reach, and that is a finding rather than something to retry.
            if not candidates and not credits:
                candidates = [i for i in sorted(beaten) if i not in revisited]
                if candidates and REVISIT_MARK not in transcript:
                    transcript += "\n" + REVISIT_MARK + "\n"
                if candidates:
                    say(6, f"round {step}: all beaten but the credits are not "
                           f"open - revisiting for checks that were gated")

            # LAST RESORT, and the reason it exists is a real stall.
            #
            # A level can be BEATEN while one of its locations stays unearned,
            # because the harness solves by setting a controller's solved flag
            # and some controllers are interactions rather than arrangements -
            # a drawer you pull open, a cupboard door you swing. Forcing the
            # flag sets a bit for something that never happened, so the group
            # never reports. `docs/release-testing.md` already records that
            # shape for Desktop Computer's `Computer Errors`.
            #
            # If an ITEM sits on such a location the run simply stops: the
            # 0.4.0 DLC gate lost three slots because the only Progressive
            # Puzzle Pack was on `Game Pieces - Drawers`. A Skip grants every
            # location on the puzzle it clears, so spending one there releases
            # the item and the run continues.
            #
            # Only while slots are still closed, which is what distinguishes
            # "the run is stuck" from "the run is finished". A skip spent here
            # is reported separately from an unforceable one - they mean
            # different things and only the other kind is allowlisted.
            if not candidates and open_slots < len(slots):
                stuck = [i for i in sorted(beaten) if i not in skipped]
                if stuck:
                    candidates = stuck
                    skip_anyway.update(stuck)
                    say(6, f"round {step}: {open_slots} of {len(slots)} slots "
                           f"open and nothing solvable - an item is on a "
                           f"location the harness cannot reach; spending a "
                           f"Skip to release it")

            if not candidates:
                say(6, f"round {step}: {len(beaten)}/{len(slots)} beaten and "
                       f"{open_slots} open - nothing left to try")
                break
            current = min(candidates, key=lambda i: (attempts[i], i))
            if current in beaten:
                revisited.add(current)
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
        forced = current in skip_anyway and current not in skipped
        if forced or (not done and exhausted and current not in skipped):
            # READ THE GATING BEFORE SPENDING, because spending destroys the
            # evidence: a Skip grants every location on the slot, so once it
            # lands there is no way to tell whether the level was unfinishable
            # or merely still waiting on an ability the mod had not granted.
            # Both arrive here as EXHAUSTED. Only one of them is acceptable.
            gated = any("waiting on " in line
                        for line in (chunk + tail).splitlines())
            log.new()
            dev("skip", 1.5)
            more = log.wait(["beaten:", "skip:", "check:"], 12, 6,
                            "the skip")
            transcript += more
            chunk += more
            tail += more
            if "skip: spent one" in more:
                # LATCHED ONLY ON A SPEND. This used to mark the slot before
                # asking, so a level that reached the skip path before any
                # Skip item had arrived was written off permanently - and
                # Skips are items, so early in a run there are none. Desktop
                # Computer asked once in round 2, was told there was nothing
                # to spend, and was never offered another chance across the
                # next seventeen rounds while two Skips sat in the inventory.
                skipped.add(current)
                spent.append((level_id, gated,
                              "unreachable-check" if forced
                              else "unforceable"))
                say(6, f"slot {current} {level_id} cannot be force-solved; "
                       f"spent a Skip"
                       + (" WHILE STILL ABILITY-GATED" if gated else ""))
            elif "skip:" in more:
                say(6, f"slot {current} {level_id} cannot be force-solved "
                       f"and there is no Skip to spend yet")
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

        if credits or (len(beaten) >= len(slots)
                       and revisited >= beaten.keys()):
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
        # PLAY THE CREDITS. The run is not over until the player watches the
        # ending: 0.3.2 stopped reporting the goal the instant the Credits
        # item arrived, because that read as the run ending without an ending
        # - droha beat the puzzle granting it and "that instant it said i
        # completed the game. I didn't have to go out and play the credits at
        # all". There is deliberately no timeout on the far side of that, so a
        # harness that never opens the card never sees a goal, which is
        # correct behaviour and looked like a regression.
        #
        # The card is the last thing on the track and IsRefused lets it
        # through once the beaten count is met, so this is the same click a
        # player makes.
        if credits:
            say(6, "playing the credits, which is what reports the goal")
            # RETRIED, because a fixed settle is a guess about how long the
            # level select takes to build and the guess has been wrong. A DLC
            # run lost the goal report on 2026-09-17 to exactly this: three
            # seconds after menu:levels, clickcard answered "open menu:levels
            # first" and the credits were never played, so the run finished
            # 21/23 with two failures that had nothing to do with the mod.
            #
            # The click reports its own refusal, so retry on that rather than
            # on a longer sleep - which would only move the guess.
            for attempt in range(3):
                dev("menu:levels", 3.0 + 2.0 * attempt)
                log.new()
                dev(f"clickcard:{CREDITS_LEVEL_INDEX}", 6.0)
                clicked = log.new()
                transcript += clicked
                if "open menu:levels first" not in clicked:
                    break
                say(6, f"the level select was not ready for the credits card; "
                       f"retrying ({attempt + 1}/3)")

        say(6, "staying connected for the goal report")
        transcript += log.wait(["goal: reported to the server"], 60, 6,
                               "the goal report")
        time.sleep(8)
        transcript += log.new()
        if "credits: unlocked" in transcript:
            credits = True

    close_game()
    return list(beaten.values()), credits, transcript, open_slots, first, spent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default="release-test")
    parser.add_argument("--clean-only", action="store_true")
    parser.add_argument("--quick", action="store_true",
                        help="three puzzles, no arrow session - for iterating. "
                             "The full run is the release gate.")
    parser.add_argument("--dlc", action="store_true",
                        help="draw every puzzle from the two DLCs. Needs "
                             "both installed. Run this AS WELL AS the "
                             "ordinary gate, never instead of it - the "
                             "ordinary one is the regression run.")
    args = parser.parse_args()
    assets = os.path.join(REPO, args.assets)

    global QUICK, DLC
    if args.dlc:
        DLC = True
        print("DLC MODE: every puzzle drawn from Cupboards and Drawers "
              "or Seeing Stars. Both must be installed.", flush=True)
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
    dlc_before = dlc_completions(CAMPAIGN)

    say(1, "cleaning the install back to vanilla")
    for item in clean():
        print(f"      removed {item}", flush=True)
    if args.clean_only:
        print("Done: install is vanilla (DevTools left in place)", flush=True)
        return 0

    # THE ASSETS ARE CHECKED BEFORE THEY ARE INSTALLED.
    #
    # This gate calls itself "the only test that would catch a release that is
    # broken only as a release", and it accepted whatever was in --assets. The
    # default is release-test/, which holds the PREVIOUS release between
    # releases - install_mod refuses two zips as ambiguous and takes one stale
    # zip without a word. It green-lit 0.3.1's artifacts twice during 0.3.2,
    # including the .apworld with the unreadable manifest.
    #
    # Checked against this checkout's version, so "they agree with each other"
    # is not enough - last release's files agree with each other perfectly.
    say(2, "checking the assets before installing them")
    expected = subprocess.run(
        [sys.executable, os.path.join(REPO, "tools", "check-version.py")],
        capture_output=True, text=True)
    version = ""
    m = re.search(r"version (\d+\.\d+\.\d+)", expected.stdout)
    if m:
        version = m.group(1)
    argv = [sys.executable,
            os.path.join(REPO, "tools", "check-release-assets.py"), assets]
    if version:
        argv += ["--expect", version]
    if subprocess.run(argv).returncode != 0:
        sys.exit("REFUSING TO RUN: the assets in "
                 f"{os.path.relpath(assets, REPO)} did not check out. Build "
                 f"them with tools/package-release.py, or pass --assets dist.")

    say(2, "installing the mod from its zip")
    zip_name, dlls = install_mod(assets)
    print(f"      {zip_name} -> {len(dlls)} dll(s) in BepInEx/plugins", flush=True)
    write_config()
    write_devtools_config()

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
        beaten, credits, transcript, open_slots, text, spent = play(log, plan)
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
        # ASKED OF THE SEED, NOT HARD-CODED. The opening is the first pack
        # boundary, and that moves when level selection changes - it went from
        # 4 to 5 the moment the mechanic reserve was capped, and a literal "4
        # open" then failed a run that was entirely correct. This file already
        # carries one scar from exactly that mistake, in the pack_size comment
        # above: a hard-coded 2 that no 8-puzzle seed could ever satisfy.
        opening = plan["boundaries"][0] if plan["boundaries"] else 0
        results.append((f"the track opens with {opening} puzzles, not all "
                        f"{PUZZLES}",
                        f"{opening} open" in track and opening < PUZZLES))

        # One launch for the whole run, and asserted. It took a correct boot:
        # teardown plus unwinding to the title after each puzzle to get here,
        # and both are easy to undo by accident - a regression should fail the
        # test rather than quietly making it ten times slower.
        launches = whole.count("A Little To The Left Archipelago loaded")
        arrows = whole.count("navigation: replay Next") +                  whole.count("navigation: post-level Continue")
        # THE WHOLE RUN, revisit pass included.
        #
        # This briefly read only the part before REVISIT_MARK, to get past
        # twelve "solve failed" errors a revisit produced. That was the wrong
        # fix and it was mine: teaching the gate not to look at a phase is
        # indistinguishable from hiding a real error in it. The throw was the
        # game's own win check reacting to a solved event on a level it had
        # already finished, so DevTools now says that in a sentence at info
        # level, and everything that is still an ERROR is still counted here.
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

        # WHAT THE SKIPS COVERED FOR, made visible and then asserted.
        #
        # A Skip finishes a slot and banks its Beaten token, so a skipped
        # level is indistinguishable from a solved one in every count above.
        # A run can therefore go green having never solved a quarter of its
        # puzzles, and the only trace is a line in the middle of a
        # fifteen-minute transcript. These two checks are what turn that from
        # something you have to notice into something the gate says.
        if spent:
            print(f"      {len(spent)} of {len(beaten)} beaten by spending a "
                  f"Skip, never solved:", flush=True)
            for level_id, gated, reason in spent:
                note = " STILL ABILITY-GATED" if gated else ""
                if reason == "unreachable-check":
                    # Not a surprise and not allowlisted: the level was beaten
                    # and an item sat on a location the harness cannot work.
                    why = " (beaten; an item was on a check out of reach)"
                else:
                    why = "" if level_id in KNOWN_UNFORCEABLE else " UNEXPECTED"
                print(f"         {level_id}{why}{note}", flush=True)

        # 1. Only levels already known to need one. A NEW name here is the
        #    signal worth having: the harness could force that level last
        #    release and cannot now, which points at solve routing or at the
        #    controller table, not at the level.
        surprises = [l for l, _, reason in spent
                     if reason == "unforceable" and l not in KNOWN_UNFORCEABLE]
        results.append(("a Skip was spent only where one is known to be "
                        "needed", not surprises))

        # 2. And none of them was still waiting on an ability. This is the
        #    check with teeth. An ability-gated level reaches the skip path
        #    looking exactly like an unfinishable one - every controller the
        #    player can reach is solved - so without this, a mod that wrongly
        #    withheld an ability would be PAPERED OVER by the Skip and the run
        #    would pass. The harness must never buy its way past a gating bug.
        papered = [l for l, gated, _ in spent if gated]
        results.append(("no Skip covered for a level the mod was still "
                        "gating", not papered))

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
    results.append(("no DLC puzzle was written to the campaign save",
                    dlc_completions(CAMPAIGN) == dlc_before))
    problem = patch_problem(whole_log())
    if problem:
        print(f"      patching: {problem}", flush=True)
    results.append(("every feature the mod ships was actually patched in",
                    problem is None))
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

    # THE SKIP LEDGER'S TWO VERDICTS, because they are pure list work and
    # getting them backwards would turn both checks into decoration that
    # passes on every run. The gating one matters most: a Skip spent on a
    # level the mod was still gating must FAIL even though the level is on
    # the known list, or a gating bug buys its way to a green run.
    sample = [("TupperwareTower", False), ("Desktop Computer", False)]
    if [l for l, _ in sample if l not in KNOWN_UNFORCEABLE]:
        sys.exit("self-test: the known-unforceable levels are not allowlisted")
    if [l for l, gated in sample if gated]:
        sys.exit("self-test: an ungated skip was read as gated")
    bad = [("TupperwareTower", True), ("Pasta", False)]
    if not [l for l, _ in bad if l not in KNOWN_UNFORCEABLE]:
        sys.exit("self-test: an unexpected skipped level was not caught")
    if not [l for l, gated in bad if gated]:
        sys.exit("self-test: a skip over an ability-gated level was not caught")

    # THE HARNESS MUST NOT WRITE THE PLAYER'S DISPLAY SETTINGS. It used to,
    # it did not even work, and it kept moving the game to another monitor.
    # Any reintroduction should fail here rather than in someone's face.
    src_text = open(__file__, encoding="utf-8").read()
    # Split so the needle does not appear verbatim in the needle's own line -
    # the first version of this matched itself and failed every time.
    if ("winreg.SetValue" + "Ex") in src_text:
        sys.exit("self-test: this harness writes the registry again - the "
                 "display belongs to the player, read it and report it")

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
                 "describe_display",
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

    pannable = ("[Info   :ALTTL Dev Tools]   [1] Pannables "
                "type=Pannables solved=False")
    draggable = ("[Info   :ALTTL Dev Tools]   [2] Computer Desktop "
                 "type=Draggables solved=False")
    if unsolved_controllers(pannable) != []:
        sys.exit("self-test: a Pannables controller must not be solved")
    if unsolved_controllers(draggable) != [2]:
        sys.exit("self-test: an ordinary unsolved controller must be solved")
    if unsolved_controllers(pannable + "\n" + draggable) != [2]:
        sys.exit("self-test: scenery must be skipped and the rest kept")

    if open_count("track: 8 puzzles, 4 open, 2 packs", 0) != 4:
        sys.exit("self-test: open_count misread the opening track line")
    if open_count("track: 1/2 packs, 6 puzzles open (+2)", 4) != 6:
        sys.exit("self-test: open_count misread the pack line")


def main_restoring():
    """Run the gate, and put the player's environment back afterwards.

    THE ONE HARNESS THAT DID NOT DO THIS, and it is the most destructive of
    them: step 1 deletes the mod's config and the run saves outright, then
    writes a config pointing at localhost. Every other harness here wraps
    itself in harness_env for exactly that reason, and docs/in-game-testing.md
    has a section titled "Put the player's environment back when the harness
    exits" that this file quietly ignored.

    droha found it the way these are always found - by looking: "the game is
    still open, i can't tell if you're still testing"... and then a config
    pointing at localhost with their real server gone. The snapshots that
    could have recovered it had been pruned.

    A snapshot, not a `with` block, because the body calls sys.exit on a
    failed check and a context manager would still have to survive that - this
    way the restore is in a finally and every exit path goes through it.
    """
    snap = take_snapshot("release-e2e")
    try:
        return main()
    finally:
        # KEEP THE GAME LOG, whatever happened.
        #
        # This gate is flaky and the log is the only way to tell one failure
        # from another - but the next run overwrites it, so by the time a
        # failure is worth comparing against a success, the evidence is gone.
        # That is exactly how a 0-of-8 run was reduced to guesswork: same zip,
        # same seed, same code as a run that beat all eight, and nothing left
        # to diff.
        #
        # Named by the clock and kept next to the server logs. Cheap, and it
        # turns "it is flaky" into something that can be read.
        try:
            os.makedirs(os.path.join(REPO, "testserver", "logs"), exist_ok=True)
            kept = os.path.join(
                REPO, "testserver", "logs",
                time.strftime("e2e-%Y%m%d-%H%M%S.log"))
            shutil.copyfile(LOG, kept)
            print(f"game log kept at {os.path.relpath(kept, REPO)}", flush=True)
        except Exception as e:
            print(f"could not keep the game log: {e}", flush=True)

        # Quiet on the way out: the gate's own verdict is what matters, and a
        # restore that announces itself between the checks and the summary
        # reads like part of the result.
        restore_snapshot(snap, quiet=True)
        print(f"environment restored from {os.path.basename(snap)}", flush=True)


if __name__ == "__main__":
    sys.stdout = Tee(sys.stdout, PROGRESS)
    print(f"progress is being written to "
          f"{os.path.relpath(PROGRESS, REPO)}", flush=True)
    self_test()
    raise SystemExit(main_restoring())
