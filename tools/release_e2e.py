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

THE RUN: 15 puzzles (the option's floor is 10), beat all 15. Small enough to finish
in minutes, and more than one pack means the progression actually has to work
- some puzzles open at the start, and the rest arrive only if packs are
received and applied.

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
import functools
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
#: Where keep_log copies the game log, next to the server logs.
KEPT_LOGS = os.path.join(REPO, "testserver", "logs")
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
#: --quick answers that question instead: no throwaway arrow session,
#: ability locks off, cat traps off, every correctness check kept - the
#: error census, the mod-versus-harness reconciliation, the campaign-save
#: isolation. What it gives up is coverage of the arrow, the pause-menu
#: Exit, and the launch count, which are the parts that need a second
#: session.
#:
#: FIFTEEN. It was eight until 2026-09-23, when the option's floor rose to 15;
#: the floor is 10 since 2026-09-25 and the gate stays at 15 (three packs).
#: PUZZLES is never reassigned, by --quick or by anything else, so the
#: speedup quick buys is the second session and the stalls, not a shorter run.
PUZZLES = 15

#: Requested pack size. How many packs that BUYS is items._pack_cap's
#: call, not ours - it is round(puzzle_count * 0.18), and MIN_OPENING can
#: raise the size, so the opening and the pack count both come from the
#: generator's boundaries. Asserting a pack count here instead of reading the
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

#: Written when a Skip was about to land on a level other than the one it
#: was bought for; the run stops there and a check fails on it.
WRONG_SKIP_MARK = "harness: STOPPED - a Skip would have landed on the wrong level"

#: Written when a Skip did nothing: the game never skipped and the mod did not
#: release the slot. The run stops there and a check fails on it.
NOT_USED_MARK = "harness: STOPPED - a Skip did nothing"

#: The mod's verdict on a Skip request, one of which it always writes. The
#: harness waits for these, not for any `skip:` line: DevTools' own
#: `skip: calling MainMenu.SkipLevel` matched that before the game had acted.
SKIP_VERDICTS = ("skip: spent one", "skip: not used", "skip: refused",
                 "skip: waiting for the game")

#: Written when a visit was not the paper plan's. The run stops there.
OFF_PLAN_MARK = "harness: STOPPED - the run left its paper plan"


def skip_outcome(text):
    """What a Skip request did, from the mod's lines: "spent", "not used",
    "refused", "waiting", or None if it said nothing. Pure.

    The mod charges once the Skip has done something: the game skipped (the
    payout lands inside SkipLevel, so `spent one` follows it), or, where the
    game will not skip, the mod released the slot itself (2026-09-25).
    `waiting` means the game may still skip late; if nothing lands, the Skip
    did nothing. `not used` is what the mod said before 2026-09-25.
    """
    if "skip: not used" in text:
        return "not used"
    if "skip: spent one" in text:
        return "spent"
    if "skip: refused" in text:
        return "refused"
    if "skip: waiting for the game" in text:
        return "waiting"
    return None


def active_level(text):
    """(levelId, index, loaded) from the last DevTools `state:` line, or None.

    `state: gameState=... activeLevel=Pencils (Randomized) index=999 ...
    loaded=True transitioning=False`. Pure.
    """
    found = None
    for line in text.splitlines():
        if "state: gameState=" not in line or "activeLevel=" not in line:
            continue
        rest = line.split("activeLevel=", 1)[1]
        if " index=" not in rest:
            continue
        level_id, tail = rest.split(" index=", 1)
        index = tail.split()[0]
        if not index.lstrip("-").isdigit():
            continue
        found = (level_id, int(index), "loaded=True" in tail)
    return found


def skip_target_ok(text, index):
    """Would a Skip land on level `index`? Pure.

    `text` is everything logged since the level was opened. The level must
    be the loaded one, AND nothing may have completed it this visit: after
    a completion the mod queues the next slot, and `state` keeps reporting
    the finished level until the completion screen ends - measured by
    probe-skip-beaten --target pencils, 2026-09-24. The gate's Skip landed
    on Fruit Stickers 11 s after beaten Pencils completed again.
    """
    if "LevelComplete " in text or "navigation: next" in text:
        return False
    now = active_level(text)
    return now is not None and now[1] == index and now[2]


def await_skip_target(log, index, tries=10):
    """Ask the game what is running until level `index` has finished loading
    or something else is running. Returns the state text; judge it with
    skip_target_ok. boot_level returns at Gameplay_GameState, which can be
    a beat before loaded=True."""
    text = ""
    for _ in range(tries):
        log.new()
        dev("state", 0.5)
        got = log.wait(["state: gameState"], 4, 6, "the level state")
        text += got
        now = active_level(got)
        if now is not None and (now[1] != index or now[2]):
            break
    return text

#: The game's credits "level". Found by IsCredits at runtime rather than by
#: this number anywhere in the mod - it is only here because clickcard: takes
#: a level index, and the one the mod reports is stable across the build.
CREDITS_LEVEL_INDEX = 84
MAX_ROUNDS = 60
QUICK = False

#: --steady: cat traps off, so a run is repeatable. See the flag's help.
STEADY = False

#: --dlc plays the same run out of DLC content instead of the base game.
#:
#: The whole gate over DLC puzzles, which is what tools/probe-dlc.py does
#: not cover: that probe launches one level and stops, where this plays a
#: run to the credits and asserts the goal reaches the server. The parts
#: most likely to differ are the ones AFTER a solve - pack progression,
#: the beaten count, the credits card - and none of those care which
#: puzzle it was until they do.
DLC = False

#: --only-arrow: run steps 1-5 (install, seed, the arrow session) and stop
#: with the arrow's two verdicts - one gate part on its own.
ONLY_ARROW = False

TOTAL = 7


#: While play() runs: a callable giving (puzzles beaten, puzzles in the
#: run, this visit, visits in the seed's paper plan).
_progress = None

#: The phase play() runs in, the long one; its lines carry the counts.
PLAY_PHASE = 6

#: What each of the TOTAL steps is, so `[step 6/7]` never reads as progress.
PHASE_NAMES = {1: "clean", 2: "assets", 3: "world", 4: "seed", 5: "arrow",
               6: "play", 7: "save"}


def set_progress(source):
    """Make play-phase lines carry visit and puzzle totals, or stop."""
    global _progress
    _progress = source


def say(phase, msg):
    """One progress line, every counter with its total.

    During play: `[visit 4/17 | 2/15 beaten]`, the visit counted against
    the seed's paper plan. Otherwise the step, named. droha: "it's really
    hard to tell how far along the test is", then "shouldn't we know exactly
    how many of each ... we are doing? This is a set seed".
    """
    if phase == PLAY_PHASE and _progress is not None:
        beaten, total, visit, planned = _progress()
        head = f"[visit {visit or '-'}/{planned} | {beaten}/{total} beaten]"
    else:
        head = f"[step {phase}/{TOTAL} {PHASE_NAMES.get(phase, '')}]".replace(" ]", "]")
    print(f"{head} {msg}", flush=True)


def round_line(current, level_id, outcome, open_slots, total):
    """The end-of-visit line: the level first, the bookkeeping after it."""
    return f"{level_id} {outcome} (slot {current}, {open_slots} of {total} open)"


def check_lines(results):
    """The final verdicts, numbered: `[check 12/27] PASS  name`. Pure."""
    return [f"[check {n}/{len(results)}] {'PASS' if ok else 'FAIL'}  {name}"
            for n, (name, ok) in enumerate(results, 1)]


def off_plan(visit, level_id, planned):
    """Why visit `visit` (1-based) is not the paper plan's, or None. Pure."""
    if visit > len(planned):
        return (f"visit {visit} is past the plan's {len(planned)} "
                f"({level_id})")
    want = planned[visit - 1]["level"]
    if want != level_id:
        return f"visit {visit} should be {want}, but the harness chose {level_id}"
    return None


def first_clearable(numbers, judge, report):
    """The first seed number `judge` accepts, reporting every verdict, or
    None. `judge(n)` returns (clears, lines). Pure apart from `report`."""
    for number in numbers:
        clears, lines = judge(number)
        for line in lines:
            report(line)
        if clears:
            return number
    return None


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
    # Forcing KeysDraggables and DrawerController completes it before Dining
    # Room, Parking Lot and Landscape ever register (DLC gate, 2026-09-24);
    # for a player they register in that order (droha, 2026-09-23).
    "DLC1 Boss",
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
#: Part locations forcing never collects, because forcing another controller
#: completes the level first; only a Skip releases one. EMPTY since
#: 2026-09-24. Its one entry was Workbench's Draggables For Targets, which
#: shares ToolsController's 21 objects, so forcing Tools completed the level
#: (full gate, visit 21). droha then played Workbench and that check never
#: fired for a player either, so it is notALocation in levels.json. Kept for
#: the next part like it, measured before it is added.
KNOWN_EARLY_COMPLETE = frozenset()

#: Levels that register NO controller until a player starts them, so forcing
#: has nothing to solve and collects nothing there. Radial Dance Party's dances
#: register one at a time as each is played (droha's hand test, 2026-09-24);
#: the base gate that day read "controllers: 0 registered on Radial Dance
#: Party" five times on one visit while its paper plan forced the dances, and
#: stopped at the next visit. Every location here is unforceable, so a seed
#: holding one never clears on paper and the seed walk moves on. Played by hand.
KNOWN_NOTHING_TO_FORCE = frozenset({"Radial Dance Party"})

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


def _scenery_only_levels():
    """Levels whose every controller is scenery (abilities.json notPuzzles)."""
    data = os.path.join(REPO, "apworld", "alttl", "data")
    with open(os.path.join(data, "abilities.json"), encoding="utf-8") as f:
        scenery = set(json.load(f)["notPuzzles"])
    with open(os.path.join(data, "levels.json"), encoding="utf-8") as f:
        levels = json.load(f)["levels"]
    out = set()
    for level in levels:
        live = [c for c in level["controllers"] if not c.get("notALocation")]
        if live and all(c["type"] in scenery for c in live):
            out.add(level["levelId"])
    return frozenset(out)


#: Levels with no puzzle controller at all: every one is scenery, which
#: solve cannot force (see unsolved_controllers), so only a Skip finishes
#: them. Derived from the table so a new one cannot be missed - DLC2 Corn,
#: Drink Glasses and MerryMess_Presents on 2026-09-24, when the DLC gate
#: spent a Skip on Corn at visit 1 while its paper plan had forced it.
SCENERY_ONLY = _scenery_only_levels()

def _forceability():
    """fixtures/forceability.jsonl by levelId: every level forced alone."""
    path = os.path.join(REPO, "fixtures", "forceability.jsonl")
    if not os.path.isfile(path):
        return {}
    with open(path, encoding="utf-8") as f:
        rows = [json.loads(line) for line in f if line.strip()]
    return {row["levelId"]: row for row in rows}


#: EVERY LEVEL, MEASURED ALONE: tools/probe-forceable.py --all, then --recheck
#: and --groups-rest, written to fixtures/forceability.jsonl. droha,
#: 2026-09-24: "shouldn't we just run every single level now and build a plan
#: around them? that way we don't have to 'figure them out' on a gate run?"
#: The per-level facts below come from it rather than from one gate each.
FORCEABILITY = _forceability()

#: Levels where forcing every controller, pass after pass, never finishes
#: the level. Their parts are forced like any other; only a Skip ends them.
FORCING_NEVER_FINISHES = frozenset(
    lid for lid, row in FORCEABILITY.items()
    if row["all"] is None and row["forceable"] > 0)

#: Every level only a Skip finishes. The yaml's starting Skips still follow
#: KNOWN_UNFORCEABLE alone, so a measured level moves no seed.
UNFORCEABLE = (frozenset(KNOWN_UNFORCEABLE) | SCENERY_ONLY
               | FORCING_NEVER_FINISHES)

#: Levels the game finishes on ONE group, whatever the table's Beaten asks
#: for: forcing that group completes the level, the mod banks the Beaten
#: token and withholds any Solution the logic has not reached. First seen on
#: DLC2 Cupcakes (DLC gate, 2026-09-24: Colors forced, Solution 1 withheld
#: for Swapping, Beaten banked); the sweep found six more, several with two
#: or three such groups. {levelId: groups that finish it alone}
KNOWN_COMPLETES_ON = {"DLC2 Cupcakes": frozenset({"Colors"})}
KNOWN_COMPLETES_ON.update({
    lid: frozenset(g for g, took in row["groups"].items() if took is not None)
    for lid, row in FORCEABILITY.items()
    if any(took is not None for took in row["groups"].values())})

#: The order a level's controllers register at load, which is the order
#: solve_level forces them in - so which group finishes it first.
CONTROLLER_ORDER = {lid: row["order"] for lid, row in FORCEABILITY.items()
                    if row.get("order")}

#: Levels that register fewer controllers at load than the table lists.
KNOWN_TABLE_GAPS = tuple(KNOWN_TABLE_GAPS) + tuple(sorted(
    lid for lid, row in FORCEABILITY.items()
    if row["atLoad"] < row["table"] and lid not in KNOWN_TABLE_GAPS))


def never_registered_parts():
    """{(levelId, group display)} forcing can never solve: groups whose
    controllers are not registered at load, on a level that completes in
    one pass with fewer controllers than its table - DLC1 Boss finishes on
    Keys and Drawer before Dining Room, Parking Lot and Landscape register.
    A level whose phases register pass by pass (TupperwareNesting) is not
    one of them."""
    _load_names()
    out = set()
    for lid, row in FORCEABILITY.items():
        if row.get("all") is None or (row.get("passes") or 1) > 1:
            continue
        if row.get("atLoad", 0) >= row.get("table", 0):
            continue
        order = set(row.get("order") or [])
        if not order:
            continue
        parts = (_NAMES["levels"].get(lid) or {}).get("parts") or {}
        for part in parts.values():
            if not set(part.get("members") or []) & order:
                out.add((lid, part["display"]))
    return frozenset(out)


def strands_the_rest(level_id, group):
    """Does forcing this group finish its level before the next solve?"""
    took = ((FORCEABILITY.get(level_id) or {}).get("groups") or {}).get(group)
    return took is not None and took <= 1


def group_rank(level_id, location):
    """Where a part location's group comes in its level's forcing order."""
    _load_names()
    parts = (_NAMES["levels"].get(level_id) or {}).get("parts") or {}
    group = location.rsplit(" - ", 1)[-1]
    members = next((p.get("members") or [] for p in parts.values()
                    if p["display"] == group), [])
    order = CONTROLLER_ORDER.get(level_id) or []
    return min((order.index(m) for m in members if m in order), default=10 ** 6)


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
        # The mod writes `TupperwareNesting (at 6 controller(s) so far)`;
        # without this every phased level on the known list read as new.
        level = level.split(" (at ", 1)[0].strip()
        if level in KNOWN_TABLE_GAPS:
            continue
        out.append(line.split("] ")[-1].strip())
    return out


def line_with(text, marker):
    for line in text.splitlines():
        if marker in line:
            return line.split("] ")[-1].strip()
    return ""


#: DevTools lines that report the game's pause state (PauseTrace.cs, the
#: timeScale watch, boot), each carrying `Paused=True|False`.
PAUSE_STATE_MARKERS = ("game: ", "time: ", "boot: ")


def pause_warnings(text):
    """Every solve sent while the game last said Paused=True, or its clock
    was at 0. A paused game holds every gameplay event; going to the title
    pauses it on its own and boot undoes that, so a pause alone is not a
    warning. Pure."""
    out, stopped, state = [], False, ""
    for line in text.splitlines():
        if "command: solve:" in line:
            if stopped:
                out.append(f"{line.split('command: ', 1)[1].strip()} sent "
                           f"while paused ({state})")
            continue
        marker = next((m for m in PAUSE_STATE_MARKERS if m in line), None)
        m = re.search(r"Paused=(True|False) timeScale=([0-9.]+)", line) if marker else None
        if m:
            stopped = m.group(1) == "True" or float(m.group(2)) == 0.0
            state = line[line.index(marker):].strip()
    return out


def report_pauses(text):
    """Print the pause warnings (droha: warn, never fail); return the count."""
    found = pause_warnings(text)
    if found:
        print(f"WARNING: {len(found)} solve(s) sent while the game was paused; "
              f"a paused game holds every event. Not a failed check.", flush=True)
        for line in found[:10]:
            print(f"      {line}", flush=True)
    return len(found)


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


def _cfg_flush(out, todo):
    """Append the keys set_cfg_keys has not placed yet, and forget them.

    Module-level rather than nested: the gate's self-test resolves names per
    function and read a nested helper as undefined.
    """
    for key, value in todo.items():
        out.append(f"{key} = {value}")
    todo.clear()


def set_cfg_keys(path, section, values):
    """Set these keys in one section of a BepInEx .cfg, touching nothing else.

    Every other section, key and comment is kept as it is. A missing file,
    section or key is created. This replaced two writers that overwrote the
    whole file, which silently wiped the mod's remembered [Display]
    WindowSize at every harness setup - so the next launch fell back to the
    game's own index-based choice and came up at 3840x2160. droha,
    2026-09-23: "you should have been the last one to open it at 720p, and i
    last had it at 1080p... so why the heck is it 4k right now?"
    """
    lines = []
    if os.path.isfile(path):
        with open(path, encoding="utf-8") as f:
            lines = f.read().splitlines()
    todo = dict(values)
    out, current, seen = [], None, False
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            if current == section:
                _cfg_flush(out, todo)
            current = stripped[1:-1]
            seen = seen or current == section
        elif (current == section and "=" in stripped
              and not stripped.startswith("#")):
            key = stripped.split("=", 1)[0].strip()
            if key in todo:
                line = f"{key} = {todo.pop(key)}"
        out.append(line)
    if current == section:
        _cfg_flush(out, todo)
    if not seen:
        if out and out[-1].strip():
            out.append("")
        out.append(f"[{section}]")
        _cfg_flush(out, todo)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")


def cfg_value(path, section, key):
    """One key's value from a BepInEx .cfg, or None."""
    if not os.path.isfile(path):
        return None
    current = None
    with open(path, encoding="utf-8") as f:
        for line in f.read().splitlines():
            stripped = line.strip()
            if stripped.startswith("[") and stripped.endswith("]"):
                current = stripped[1:-1]
            elif (current == section and "=" in stripped
                  and not stripped.startswith("#")):
                k, v = stripped.split("=", 1)
                if k.strip() == key:
                    return v.strip()
    return None


def write_config():
    """Point the install at the test server before the next launch.

    A player types this into the pane. Setting the keys is the scriptable
    equivalent, and BepInEx fills in every key not named with its default.

    ONLY THE SERVER KEYS, plus a window size when none is remembered: 720p is
    the size for test sessions (never 4K), but a size the player chose is
    theirs and is left alone.
    """
    set_cfg_keys(MOD_CONFIG, "Server", {
        "Host": "localhost",
        "Port": PORT,
        "SlotName": SLOT,
        "Password": "",
        "AutoConnect": "true",
        "MaxRetries": 0,
    })
    if not cfg_value(MOD_CONFIG, "Display", "WindowSize"):
        set_cfg_keys(MOD_CONFIG, "Display", {"WindowSize": "1280x720"})


def write_devtools_config():
    """Silence the run.

    A gate plays real sessions for a quarter of an hour, and until now it did
    so with the music and the effects going on whichever machine happened to
    be running it. droha: "can you set the tests to have the music and sf
    muted?"

    MuteAudio holds AudioListener.volume at zero rather than touching the
    player's own volume settings, which live inside the encoded save next to
    actual progress. Nothing here is persisted and nothing of theirs moves.

    One key, set in place: every other devtools setting is kept.
    """
    set_cfg_keys(DEVTOOLS_CONFIG, "Debug", {"MuteAudio": "true"})


def yaml_text(quick, steady, dlc_on):
    """The player yaml the gate generates from, as pure text.

    SEPARATE FROM generate() so the options can be read and asserted
    without wiping a directory or spending a minute inside Generate.py.
    --steady was dead code for exactly as long as nothing could read
    this string: STEADY was set by the flag and consulted nowhere, so
    every run described in a comparison as "repeatable" still had cat
    traps firing at 25% and every conclusion drawn from two such runs
    was noise. tools/test_scheduler.py now reads what this returns.

    Positional parameters only. self_test's undefined-name walk binds
    fn.args.args and nothing else, so a keyword-only parameter here is
    reported as a leftover from an edit and refuses to run the harness.
    """
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
           # NO exclude_locations. It used to list 14 drawer and cupboard
           # locations the harness cannot earn; by 2026-09-23 every one
           # became notALocation in levels.json, so they are no longer
           # locations and naming them made Generate.py reject the yaml.
           # A new entry must be a real location (test_scheduler checks).
           ) if dlc_on else ""

    # A SKIP IN HAND PER KNOWN_UNFORCEABLE LEVEL, as the --dlc block does.
    # Those levels only ever finish by a Skip, so without these the pool's
    # skip_count Skips went to them and none was left for an alternate
    # solution holding progression: the full gate of 2026-09-23 stalled at
    # 12/15 with Symmetry on Pencils Solution 2. See skip_shortfall.
    base_skips = ("  start_inventory:\n"
                  f"    Skip: {len(KNOWN_UNFORCEABLE)}\n")

    return (
            f"name: {SLOT}\n"
            "game: A Little to the Left\n"
            "requires:\n"
            "  version: 0.6.7\n"
            "A Little to the Left:\n"
            f"  puzzle_count: {PUZZLES}\n"
            f"  levels_to_beat: {PUZZLES}\n"
            # The smallest pack the generator will honour, to get as many
            # packs as the run can carry.
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
            # not its size: PUZZLES is 15 in every mode.
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
            #
            # STEADY turns the traps off WITHOUT touching ability locks,
            # which is the whole difference between it and --quick: a
            # steady run still faces every gate, it just faces them the
            # same way twice. This line read `0 if QUICK else 25` while
            # --steady printed "cat traps off" and changed nothing.
            f"  ability_locks: {'false' if quick else 'true'}\n"
            f"  starting_abilities: {6 if quick else 1}\n"
            f"  cat_trap_chance: {0 if quick or steady else 25}\n"
            "  hint_coverage: 50\n"
            "  skip_count: 2\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n"
            + (dlc or base_skips))


def generate():
    """The gate's seed: the first from GATE_SEED its paper plan can clear.
    Returns (out dir, seed zip, plan with "order" and "visits")."""
    yaml_dir = os.path.join(REPO, "testserver", "yaml-e2e")
    out = os.path.join(REPO, "testserver", "out-e2e")
    _number, seed_zip, plan = generate_into(
        yaml_dir, out, yaml_text(QUICK, STEADY, DLC), arrow=not QUICK,
        report=lambda line: say(4, line))
    return out, seed_zip, plan


def read_plan(folder, seed_zip):
    """slot -> (levelIndex, levelId) and the pack boundaries, from the seed.

    The authoritative source, read with Archipelago's own loader rather than
    guessed at. The harness needs it because it opens levels by index, and it
    needs the BOUNDARIES so it can refuse to open a slot the packs have not
    reached - see play().

    It also carries the per-location ABILITY REQUIREMENTS, which is what the
    mod itself gates on. Modelling those from names.json instead was a real
    mistake: the model and the seed disagreed, and the harness spent whole
    runs deciding a level was unplayable on a requirement the seed did not
    actually have. Packs are dropped here deliberately - a slot the packs
    have not opened is already excluded by `boundaries`, and the mod's own
    guard does not gate on packs either.
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
                  'boundaries': list(d['pack_boundaries']),
                  'ability_locks': bool(d.get('ability_locks', True)),
                  'starting_abilities': list(d.get('starting_abilities', [])),
                  'requirements': {k: v['abilities']
                                   for k, v in d['requirements'].items()}}))
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


#: Ability name -> nothing; just the set of names, for reading the item log.
#: Controller class -> the ability that unlocks it.
_ABILITY_NAMES = None
_CLASS_ABILITY = None
_LEVEL_ABILITIES = None


def _load_ability_tables():
    """The same two files the generator reads, so a table change moves this."""
    global _ABILITY_NAMES, _CLASS_ABILITY, _LEVEL_ABILITIES
    if _ABILITY_NAMES is not None:
        return

    with open(os.path.join(REPO, "apworld", "alttl", "data", "abilities.json"),
              encoding="utf-8") as f:
        table = json.load(f)
    # DLCABILITIES IS NESTED ONE LEVEL DEEPER, and reading it flat made the
    # harness blind to every DLC ability. The shape is
    # {"abilities": {ability: [classes]}} but
    # {"dlcAbilities": {"DLC2": {ability: [classes]}}} - see
    # DLC_ABILITY_CLASSES in data.py. Iterating both the same way walked the
    # DLC KEYS as if they were abilities, so "Distributing" never entered the
    # table at all.
    #
    # The cost was a stalled DLC gate: abilities_held could not see
    # Distributing arrive, so slot_has_work held DLC2 Pizza and its
    # neighbours back forever. Two of eight beaten in 76 rounds, 17 Skips
    # spent, and the seed was fine the whole time.
    owner = {}
    for ability, classes in (table.get("abilities") or {}).items():
        for cls in classes:
            owner[cls] = ability
    for per_dlc in (table.get("dlcAbilities") or {}).values():
        for ability, classes in (per_dlc or {}).items():
            for cls in classes:
                owner[cls] = ability

    with open(os.path.join(REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        levels = json.load(f)

    per_level = {}
    for lv in levels["levels"]:
        need = set(lv.get("extraAbilities") or [])
        for c in lv["controllers"]:
            ability = owner.get(c["type"])
            if ability:
                need.add(ability)
        per_level[lv["levelId"]] = need

    _ABILITY_NAMES = set(owner.values())
    _CLASS_ABILITY = owner
    _LEVEL_ABILITIES = per_level


def run_holds(transcript, plan):
    """What the scheduler should treat as held: every ability when the seed
    has locks off (--quick), because the mod then files every check whatever
    is held (SlotProgress.IsReachable) - the same rule the paper plan uses."""
    if not plan.get("ability_locks", True):
        _load_ability_tables()
        return set(_ABILITY_NAMES)
    return abilities_held(transcript)


def abilities_held(transcript):
    """Which abilities the run has actually been given, from the item log."""
    _load_ability_tables()
    held = set()
    for line in transcript.splitlines():
        if "received item: " not in line:
            continue
        name = line.split("received item: ", 1)[1].strip()
        if name in _ABILITY_NAMES:
            held.add(name)
    return held



_NAMES = None


def _load_names():
    global _NAMES
    if _NAMES is None:
        with open(os.path.join(REPO, "apworld", "alttl", "data", "names.json"),
                  encoding="utf-8") as f:
            _NAMES = json.load(f)


def locations_for_slots(plan):
    """slot index -> its location names, from the seed's requirement table.

    Matched on the DISPLAY name the locations are built from, longest first
    so "Coins 1 (Shape)" cannot be captured by a shorter level whose name is
    a prefix of it.
    """
    _load_names()
    display = {}
    for i, (_index, level_id) in enumerate(plan["slots"]):
        entry = _NAMES["levels"].get(level_id) or {}
        display[entry.get("display", level_id)] = i

    owner = {}
    for location in plan.get("requirements") or {}:
        for name in sorted(display, key=len, reverse=True):
            if location.startswith(name + " - "):
                owner.setdefault(display[name], []).append(location)
                break
    return owner


def slot_has_work(slot, plan, where, held, collected):
    """Is there anything on this slot I could legitimately earn right now?

    THE ONLY SCHEDULING RULE, and arriving at it took five wrong ones. The
    harness had grown three separate notions - is the level playable, has
    the ability set changed since I was last here, is this a revisit - and
    each was a partial shadow of this question. They disagreed with each
    other, and a run could sit at 7 of 8 for fourteen rounds with the check
    that unblocked it one visit away.

    Asked against the SEED's own requirements, which is what the mod gates
    on, so the harness and the mod cannot disagree about what is earnable.
    """
    requirements = plan.get("requirements") or {}
    for location in where.get(slot, ()):
        if location in collected:
            continue
        if set(requirements.get(location) or []) <= held:
            return True
    return False





def slot_rank(slot, slots, attempts, order):
    """Least-tried first, planned order as the tiebreak, index to settle it.

    ATTEMPTS BEFORE PLAN, and the order is not cosmetic. Ranking by the plan
    first locks the run onto its lowest-ranked level and retries that one
    every round - a gate run span on Desktop Computer from round 2 to the
    end and beat exactly one puzzle of eight.

    Module level rather than nested inside choose_slot because this file's
    self-test reads names statically and cannot see closures; a nested
    helper is reported as a leftover from an edit.
    """
    return (attempts.get(slot, 0), order.get(slots[slot][1], 10 ** 6), slot)

#: Rounds finishing nothing before the Skip path is allowed to intervene.
#: Two is noise - a cat trap costs a round, a phased level costs another.
STUCK_AFTER = 3



def arrow_slot(slots, plan, where):
    """The slot the arrow check should start on: one it can actually finish.

    It used to be slots[0] unconditionally, which only works when the first
    slot happens to need no abilities. Base-game seeds usually oblige; a DLC
    seed put DLC2 Pizza there, gated behind Distributing, so the session
    could never complete the puzzle it had to complete before pressing the
    arrow - and reported "the next-level arrow opens the run's next puzzle"
    and "the pause menu Exit leaves the level" as broken. Two navigation
    assertions failing because of an unrelated ability gate.

    The session holds the seed's starting abilities (precollected items
    arrive on connect) and nothing else. Slot 0 is the fallback when nothing
    qualifies, so the check still runs and fails honestly rather than being
    skipped.
    """
    # FINISHABLE, not merely "has work": the check must complete its level
    # before it can press the arrow. Seed 20260907 put Desktop Computer
    # first - one part needs nothing, so it had work, and forcing never
    # completes it. So: not KNOWN_UNFORCEABLE, and its Beaten token needs
    # nothing held.
    #
    # And only an OPEN slot, holding what the session really holds: the
    # seed's starting abilities arrive on connect there too. Assuming none
    # sent seed 20260907's check to slot 5, which no pack had opened.
    requirements = plan.get("requirements") or {}
    held = set(plan.get("starting_abilities") or ())
    opening = (plan.get("boundaries") or [len(slots)])[0]
    for i in range(min(opening, len(slots))):
        # Nor a level that finishes on one group: the session is modelled as
        # collecting everything reachable there, which such a level does not.
        if slots[i][1] in UNFORCEABLE or slots[i][1] in KNOWN_COMPLETES_ON:
            continue
        token = next((l for l in where.get(i, ()) if l.endswith(" - Beaten")),
                     None)
        if token is not None and not set(requirements.get(token) or ()) <= held:
            continue
        if slot_has_work(i, plan, where, held, set()):
            return i
    return 0

def skipless_levels():
    """Levels where a Skip does nothing. None, since 2026-09-25.

    It was every generator level: on Pencils (Randomized) the game's
    SkipLevel completes nothing, beaten or not. The mod now releases the slot
    itself where the game will not skip (Skips.cs, by the level's Skippable
    flag), so a Skip bought anywhere finishes its card. Kept as a function,
    and threaded through choose_slot, for a level that ever comes back.
    """
    return frozenset()


def choose_slot(slots, plan, where, open_slots, beaten, attempts, barren,
                held, collected, skipped, credits, idle,
                skips_out, skipless=frozenset()):
    """Which slot to play next, and why. Pure - no game, no log, no clock.

    EXTRACTED SO IT CAN BE TESTED IN MILLISECONDS. Every scheduling bug this
    harness has had was in these thirty lines, and each one was found by
    running a fifteen-minute game and reading the wreckage: a level retried
    every round because the planned order overrode attempt rotation, a
    revisit that never released its slot, a Skip announced and never spent,
    a run that ended "nothing left to try" with a Skip still in the bag.
    All of it is arithmetic over sets, and none of it needed a game to find.
    See test_scheduler.py, which replays those exact failures.

    Returns (slot, reason, buy_skip). `buy_skip` means the caller must let
    solve_level spend one rather than short-circuiting the visit - the stuck
    path picks an already-beaten slot precisely so a Skip gets spent on it.
    """
    order = {level: n for n, level in enumerate(plan.get("order") or [])}
    rank = functools.partial(slot_rank, slots=slots, attempts=attempts,
                             order=order)

    # Unbeaten slots the packs have opened, with something earnable on them.
    # `worth_visiting` is the whole rule: is there a location here I could
    # legitimately collect right now. The `barren` half of it drops slots a
    # visit has already proved empty, which is what stops the harness and
    # the requirement table arguing forever when they disagree.
    live = [i for i in range(min(open_slots, len(slots)))
            if i not in beaten
            and worth_visiting(i, plan, where, held, collected, barren)]

    # Beaten slots are candidates on the SAME test, not a fallback. A level
    # that is partially playable never lets the candidate list empty, so a
    # fallback-only revisit can starve: TupperwareTower held a Stacking
    # group and was picked every round from 16 to 29 while the check that
    # unblocked the run sat one revisit away on another level.
    live += [i for i in sorted(beaten)
             if i not in live
             and worth_visiting(i, plan, where, held, collected, barren)]

    # NO PROGRESS IS THE TRIGGER, NOT AN EMPTY CANDIDATE LIST.
    #
    # The earlier test was "nothing left to play", which a single stubborn
    # level defeats: DLC1 Clock Cupboard had earnable work by the seed's
    # table and the harness could never collect it, so it stayed a
    # candidate every round, the list never emptied, and the Skip path
    # never fired. Three of eight, with four levels waiting on an item a
    # Skip would have freed.
    #
    # Parking the level fixed that and cost more than it saved: removing it
    # from normal play changed WHEN Skips were spent, and a DLC gate went
    # from 23/25 to 19/25. Asking "has anything finished lately" leaves the
    # candidate list and the Skip order exactly as they were, and only adds
    # a recovery once the run has genuinely stalled.
    # AND ONLY IF A SKIP CAN ACTUALLY BE SPENT. The recovery picks a slot
    # so solve_level will buy one; with the supply exhausted the mod
    # answers "there is no Skip to spend yet", nothing happens, and the
    # slot is never marked skipped - so it is chosen again next round,
    # forever. A DLC gate cycled that way from round 20 at five of eight.
    if idle >= STUCK_AFTER and len(beaten) < len(slots) and not skips_out:
        # A PREFERENCE, NOT A FILTER. This is the third attempt and the
        # first that cannot strand the run.
        #
        # FILTERING these to "slots waiting on nothing" was tried and
        # took the base gate from 23/25 to 20/25, losing the credits,
        # the goal and the server's goal. The reasoning error is visible
        # without a run: the stall Skip exists FOR beaten slots with
        # uncollected locations, and those are uncollected precisely
        # BECAUSE they are ability-gated. Filter them and there is
        # usually no candidate at all, so the stall path stops firing.
        #
        # Ranking costs nothing. A slot whose remaining locations are
        # all unlocked is a strictly better place to spend - waiting
        # will never free it, so the Skip is the only way - and a
        # gated slot is still available when it is the only option.
        # That keeps the run moving AND stops it buying past a gate
        # whenever it has any choice, which is the case the gate's
        # "no Skip covered for a level the mod was still gating"
        # assertion actually caught.
        stalled = [i for i in sorted(beaten)
                   if i not in skipped
                   and slots[i][1] not in skipless
                   and has_uncollected(i, where, collected)]
        unlocked = [i for i in stalled
                    if not waiting_on_an_ability(i, plan, where, held,
                                                 collected)]
        stalled = unlocked or stalled
        if stalled:
            return (min(stalled, key=rank),
                    "buy a skip to release an item", True)

    if live:
        return min(live, key=rank), "playable", False

    # NOTHING EARNABLE ANYWHERE. Either the run is finished, or an item is
    # stranded on a location the harness cannot reach - a drawer it cannot
    # pull, a switch it cannot flip. A Skip grants a puzzle's remaining
    # locations outright, which is the only way to shake one loose.
    #
    # Stuck is "not every puzzle beaten", not "slots still closed". The
    # older test missed the case where all slots are open and an ITEM is
    # stranded: a DLC gate ended at seven of eight with an unspent Skip and
    # a seed the offline sim clears completely.
    # THE SAME SUPPLY GUARD as the stall path above. This one fires when
    # nothing is playable at all, without waiting out the idle rounds - but
    # asking for a Skip that cannot be bought loops just as hard here.
    if len(beaten) < len(slots) and not skips_out:
        stuck = [i for i in sorted(beaten)
                 if i not in skipped
                 and slots[i][1] not in skipless
                 and has_uncollected(i, where, collected)]
        # SAME PREFERENCE AS THE STALL PATH, and a distinct reason so
        # play() can tell the two apart. Nothing is playable here, so
        # waiting cannot free anything and the Skip is genuinely the
        # only way forward - but if there IS a candidate whose work is
        # already unlocked, spend there and leave the gated one alone.
        unlocked = [i for i in stuck
                    if not waiting_on_an_ability(i, plan, where, held,
                                                 collected)]
        if unlocked:
            return min(unlocked, key=rank), "buy a skip to release an item", True
        if stuck:
            return min(stuck, key=rank), LAST_RESORT, True

    # Everything beaten but the credits never opened: go back for checks
    # that were gated at the time, even where nothing looks earnable, since
    # this is the last chance to notice.
    if not credits and len(beaten) >= len(slots):
        again = [i for i in sorted(beaten) if i not in skipped]
        if again:
            return min(again, key=rank), "revisit for gated checks", False

    return None, "nothing left to try", False

def worth_visiting(slot, plan, where, held, collected, barren):
    """Is this slot worth opening right now?

    slot_has_work, minus anything a visit has already proved empty. The
    second half is not belt and braces - the transcript covers ONE game
    session and the run spans several, so a slot beaten during the arrow
    check has its locations filed where this code cannot see them and looks
    permanently unfinished. Without the barren guard the loop revisits it
    every round forever; a gate run spent rounds 9 to 60 doing exactly that
    on Stamps (Randomized).
    """
    if barren.get(slot) == frozenset(held):
        return False
    return slot_has_work(slot, plan, where, held, collected)

def collected_locations(transcript):
    """Every location the mod has said it filed.

    TWO PREFIXES, NOT ONE. A Beaten token is a LOCAL EVENT location - the
    server has no address for it - so the mod logs it as `beaten: X` and
    never as `check: X`. Reading only `check:` meant a Beaten location could
    never be marked collected, so its slot looked like it had work forever:
    a gate run spent rounds 20 to 60 revisiting Stamps (Randomized),
    collecting nothing each time, because its Beaten token was invisible to
    this function.
    """
    done = set()
    for line in transcript.splitlines():
        for prefix in ("check: ", "beaten: "):
            if prefix in line:
                done.add(line.split(prefix, 1)[1].strip())
    return done


def carried_checks(text):
    """Only the `check:` and `beaten:` lines of an earlier session. Pure.

    Nothing else carries: items are resent on every connect, and lines like
    `credits: unlocked` belong to the session that logged them.
    """
    return "".join(line + "\n" for line in text.splitlines()
                   if "check: " in line or "beaten: " in line)


def read_spoiler(out_dir, seed_zip):
    """(starting items, [(location, item)]) from the seed's own spoiler.

    WE GENERATED THIS SEED, so there is no reason to discover its order by
    trial. The spoiler names every placement, which is enough to work out a
    real completion order before the game is even launched - and to say
    immediately, rather than fifteen minutes in, if no such order exists.
    """
    with zipfile.ZipFile(os.path.join(out_dir, seed_zip)) as z:
        name = next(n for n in z.namelist() if n.endswith("_Spoiler.txt"))
        text = z.read(name).decode("utf-8", "replace")

    starting, placements, section = [], [], ""
    for raw in text.splitlines():
        line = raw.strip().lstrip("\ufeff")
        if line in ("Starting Items:", "Locations:", "Playthrough:"):
            section = line
            continue
        if not line:
            continue
        if section == "Starting Items:":
            starting.append(line)
        elif section == "Locations:" and ": " in line:
            location, item = line.split(": ", 1)
            placements.append((location.strip(), item.strip()))
    return starting, placements


def _location_requirement(location, names, levels_by_display, owner):
    """Which abilities a single location demands.

    A PART NEEDS ITS OWN GROUP'S ABILITIES; a Solution or Beaten needs the
    level's whole union. Getting that backwards would make the plan think a
    part was gated behind abilities it never needed.
    """
    for display, level_id in levels_by_display.items():
        if not location.startswith(display + " - "):
            continue
        suffix = location[len(display) + 3:]
        entry = names["levels"].get(level_id) or {}
        parts = entry.get("parts") or {}

        for part in parts.values():
            if part.get("display") == suffix:
                return level_id, set(part.get("abilities") or [])

        union = set()
        for part in parts.values():
            union |= set(part.get("abilities") or [])
        union |= _extra_abilities(level_id)
        return level_id, union
    return None, set()


def _extra_abilities(level_id):
    _load_ability_tables()
    with open(os.path.join(REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        for lv in json.load(f)["levels"]:
            if lv["levelId"] == level_id:
                return set(lv.get("extraAbilities") or [])
    return set()


def completion_plan(out_dir, seed_zip, plan):
    """Play the seed on paper. Returns (level order, unreachable locations).

    A straight reachability sweep: hold the starting items, collect every
    location now within reach, add whatever they held, repeat until nothing
    new opens. Packs are modelled too, because a location behind a slot that
    never opens is just as unreachable as one behind a missing ability.

    THE POINT IS THE SECOND RETURN VALUE. If it is not empty the seed cannot
    be cleared in any order, and that is a generation bug worth failing on in
    seconds instead of discovering as a stalled run twenty rounds later.
    """
    _load_ability_tables()
    with open(os.path.join(REPO, "apworld", "alttl", "data", "names.json"),
              encoding="utf-8") as f:
        names = json.load(f)

    slots = {level_id: i for i, (_index, level_id) in enumerate(plan["slots"])}
    by_display = {}
    for level_id in slots:
        entry = names["levels"].get(level_id) or {}
        by_display[entry.get("display", level_id)] = level_id

    starting, placements = read_spoiler(out_dir, seed_zip)
    held = {i for i in starting if i in _ABILITY_NAMES}
    # Locks off (--quick): nothing is gated, so every ability counts as held.
    # Judging it by requirements named 3 of 15 levels as the whole order.
    if not plan.get("ability_locks", True):
        held = set(_ABILITY_NAMES)
    packs = sum(1 for i in starting if i == "Progressive Puzzle Pack")

    boundaries = plan.get("boundaries") or []

    # THE SEED'S OWN REQUIREMENTS, as paper_run and the mod read them.
    # names.json does not subtract bypassedAbilities, so modelling from it
    # made Workbench's Solution ask for Drawer and called valid seeds "a
    # generation bug" (20260908, 20260911 on 2026-09-24). It still names the
    # level a location belongs to.
    requirements = plan.get("requirements") or {}
    need = {}
    for location, item in placements:
        level_id, _model = _location_requirement(
            location, names, by_display, _CLASS_ABILITY)
        need[location] = (level_id, set(requirements.get(location) or []),
                          item)

    collected, order = set(), []
    while True:
        progress = False
        for location, (level_id, abilities, item) in need.items():
            if location in collected or level_id is None:
                continue
            # How many slots the packs collected so far have opened.
            reachable_slots = (len(slots) if not boundaries
                               else boundaries[min(packs, len(boundaries) - 1)])
            if slots.get(level_id, 10 ** 6) >= reachable_slots:
                continue
            if not abilities <= held:
                continue

            collected.add(location)
            progress = True
            if item in _ABILITY_NAMES:
                held.add(item)
            elif item == "Progressive Puzzle Pack":
                packs += 1
            if level_id not in order:
                order.append(level_id)
        if not progress:
            break

    unreachable = [l for l in need if l not in collected and need[l][0]]
    return order, unreachable


def harness_cannot_force(plan, where):
    """Locations forcing a controller never collects; only a Skip does.

    An alternate solution (forcing makes one arrangement), every location
    of an UNFORCEABLE level, every location of a level that registers
    nothing (KNOWN_NOTHING_TO_FORCE), and the yaml's container excludes.
    """
    out = set()
    unregistered = never_registered_parts()
    for i, (_, level_id) in enumerate(plan["slots"]):
        for loc in where.get(i, ()):
            if (level_id, loc.rsplit(" - ", 1)[-1]) in unregistered:
                out.add(loc)
                continue
            # An unforceable level's PART checks are forced like any other
            # (the gate logs `check: Desktop Computer - Notes`); only its
            # completion never comes.
            if (level_id in KNOWN_NOTHING_TO_FORCE
                    or (level_id in UNFORCEABLE
                        and not is_part_location(loc))
                    or (solution_number(loc) or 0) >= 2
                    or loc in CONTAINER_EXCLUDES
                    or loc in KNOWN_EARLY_COMPLETE):
                out.add(loc)
    return out


def is_part_location(location):
    """A part check, sent mid-level: not a Solution, not the Beaten token."""
    return (solution_number(location) is None
            and not location.endswith(" - Beaten"))


def take_item(item, held, stock):
    """One collected item into the paper run's state: an ability is held, a
    pack opens the next slots, a Skip adds to supply."""
    if item and item in _ABILITY_NAMES:
        held.add(item)
    elif item == "Progressive Puzzle Pack":
        stock["packs"] += 1
    elif item == "Skip":
        stock["supply"] += 1


def paper_run(slots, plan, where, placements, starting=(), unreachable=(),
              skips=0, park=True, rounds=MAX_ROUNDS, flaky=(), park_after=2,
              stop_when_all_beaten=True, production=False, opening=(),
              final=None):
    """Play the run on paper with choose_slot, the harness's own scheduler.

    `opening`: slots played before the run - the full gate's arrow check
    beats one in a session of its own - collected and beaten, not visits.
    `final`: a dict to fill with the end state (held, collected, beaten),
    which why_unclearable reads.

    Pure. The seed is fixed, so this is the plan the gate reports against
    (`visit 4/17`). Packs open slots at the seed's boundaries, starting
    abilities and Skips are in hand, and a collected Skip adds to supply.

    `unreachable`: locations forcing never collects (harness_cannot_force).
    `flaky`: slots whose first visit achieves nothing (a model knob for the
    tests). `stop_when_all_beaten`: production stops there, because the
    credits open with the last token.

    `production` copies play()'s visit rules exactly, as measured on the
    2026-09-24 gate: a revisit forces nothing and parks after one empty
    visit; an attempt stops at a locked part with no Skip; only a
    KNOWN_UNFORCEABLE level with nothing locked left is exhausted into a
    Skip; a bought Skip grants everything left on the card. Off, it is the
    abstract model test_scheduler's whole-run tests were written against.

    Returns (beaten count, attempts per slot, Skips spent, visits), one
    visit per round: {slot, level, skip, got, beaten, reset}. `reset` marks
    a forced visit whose checks send a Cat Trap from a part location: the
    mod resets the level mid-solve and the harness refunds the pass.
    """
    _load_ability_tables()
    held = {i for i in starting if i in _ABILITY_NAMES}
    if not plan.get("ability_locks", True):
        held = set(_ABILITY_NAMES)
    stock = {"supply": skips + sum(1 for i in starting if i == "Skip"),
             "packs": sum(1 for i in starting if i == "Progressive Puzzle Pack")}
    boundaries = plan.get("boundaries") or []
    collected, beaten, skipped, barren = set(), set(), set(), {}
    stranded = set()
    fruitless = {}
    attempts = {i: 0 for i in range(len(slots))}
    idle = spent = 0
    visits = []

    # RESTORED, NOT BEATEN, as play() has it: `slot 0 Spice Jars was beaten
    # in an earlier session - not demanding a second token`. It counts as
    # beaten when a visit completes it or the end-of-attempt reconcile sees
    # its token, whichever comes first.
    restored = set()
    for slot in opening:
        for loc in where[slot]:
            if (loc not in collected and loc not in unreachable
                    and set(plan["requirements"][loc]) <= held):
                collected.add(loc)
                take_item(placements.get(loc), held, stock)
        restored.add(slot)

    for _ in range(rounds):
        if stop_when_all_beaten and len(beaten) >= len(slots):
            break
        open_slots = len(slots) if not boundaries else min(
            len(slots), boundaries[min(stock["packs"], len(boundaries) - 1)])
        slot, _why, buy = choose_slot(
            slots, plan, where, open_slots=open_slots, beaten=set(beaten),
            attempts=attempts, barren=barren, held=held, collected=collected,
            skipped=skipped, credits=False, idle=idle,
            skips_out=(spent >= stock["supply"]),
            skipless=skipless_levels() if production else frozenset())
        if slot is None:
            break

        attempts[slot] += 1
        got = set()
        if slot in flaky and attempts[slot] == 1:
            fruitless[slot] = fruitless.get(slot, 0) + 1
            idle += 1
            if park and fruitless[slot] >= park_after:
                barren[slot] = frozenset(held)
            visits.append({"slot": slot, "level": slots[slot][1],
                           "skip": False, "got": 0,
                           "beaten": slot in beaten, "reset": False})
            continue

        if production:
            # PLAY()'S VISIT, STATEMENT FOR STATEMENT, because the gate stops
            # at the first visit that differs from this plan. Measured against
            # the 2026-09-24 gate: a revisit forces nothing and leaves idle
            # alone; an attempt forces what is reachable (nothing, for a Skip
            # bought on a beaten slot), counts fruitless BEFORE any Skip, then
            # takes play()'s Skip path, then resets idle only if the level's
            # Beaten token arrived this visit.
            level_id = slots[slot][1]
            token = next((l for l in where[slot] if l.endswith(" - Beaten")), None)
            if slot in beaten and not buy:
                got = {loc for loc in where[slot] if loc not in collected
                       and loc not in unreachable and loc not in stranded
                       and set(plan["requirements"][loc]) <= held}
                for loc in sorted(got):
                    collected.add(loc)
                    take_item(placements.get(loc), held, stock)
                if not got and park:
                    barren[slot] = frozenset(held)
                visits.append({"slot": slot, "level": level_id, "skip": False,
                               "got": len(got), "beaten": True, "reset": False,
                               "kind": "revisit"})
                continue

            forced = set()
            if not (buy and slot in beaten):
                forced = {loc for loc in where[slot] if loc not in collected
                          and loc not in unreachable and loc not in stranded
                          and set(plan["requirements"][loc]) <= held}
            # A LEVEL THE GAME FINISHES ON ONE GROUP (KNOWN_COMPLETES_ON), the
            # way solve_level meets it: groups in controller order, stopping
            # at the first that ends the level. That banks the Beaten token;
            # every part not solved by then is stranded, because a revisit
            # forces nothing and the mod files only what the game restores
            # as solved - only a Skip releases those.
            triggers = KNOWN_COMPLETES_ON.get(level_id, frozenset())
            if triggers and token is not None and token not in collected:
                parts = sorted((l for l in forced if is_part_location(l)),
                               key=lambda l: group_rank(level_id, l))
                walked, hit = [], False
                for loc in parts:
                    walked.append(loc)
                    group = loc.rsplit(" - ", 1)[-1]
                    if group in triggers:
                        hit = True
                        # A completion within a second lands before the
                        # next solve, so the groups after it are never
                        # forced; a slower one lets the pass finish first.
                        if strands_the_rest(level_id, group):
                            break
                if hit:
                    forced = ({l for l in forced if not is_part_location(l)}
                              | set(walked) | {token})
                    stranded.update(
                        l for l in where[slot] if is_part_location(l)
                        and l not in collected and l not in walked)
            for loc in sorted(forced):
                collected.add(loc)
                take_item(placements.get(loc), held, stock)
            locked_part = any(loc not in collected and is_part_location(loc)
                              and not set(plan["requirements"][loc]) <= held
                              for loc in where[slot])
            done = (token in forced) if token else all(
                l in collected for l in where[slot])
            # play(): a restored slot that completes needs no second token.
            if (slot in restored and not done and not locked_part
                    and level_id not in UNFORCEABLE
                    and (token is None or token in collected)):
                done = True
            if not done and not forced:
                fruitless[slot] = fruitless.get(slot, 0) + 1
                if park and fruitless[slot] >= park_after:
                    barren[slot] = frozenset(held)
            else:
                fruitless[slot] = 0

            # play(): `(forced and not done) or (not done and exhausted and
            # current not in skipped)`. Exhausted - every controller solved,
            # no completion - is a KNOWN_UNFORCEABLE level with nothing left
            # locked; any other level completes when forced.
            exhausted = (not done and not locked_part
                         and level_id in UNFORCEABLE)
            skip = (((buy and not done) or (exhausted and slot not in skipped))
                    and spent < stock["supply"])
            granted = set()
            if skip:
                spent += 1
                skipped.add(slot)
                granted = {loc for loc in where[slot] if loc not in collected}
                for loc in sorted(granted):
                    collected.add(loc)
                    take_item(placements.get(loc), held, stock)
                done = done or (token in granted)

            if done:
                beaten.add(slot)
            idle = 0 if done else idle + 1
            # play()'s end-of-attempt reconcile: every slot whose Beaten token
            # has been seen is beaten, whichever slot was open.
            for i in range(len(slots)):
                tok = next((l for l in where[i] if l.endswith(" - Beaten")), None)
                if tok is not None and tok in collected:
                    beaten.add(i)
            visits.append({
                "slot": slot, "level": level_id, "skip": skip,
                "got": len(forced) + len(granted), "beaten": slot in beaten,
                "reset": any(is_part_location(loc)
                             and placements.get(loc) == "Cat Trap"
                             for loc in forced),
                "kind": "attempt"})
            # play(): "nothing finished in N attempts; stopping".
            if idle >= len(slots) * 2:
                break
            continue

        for loc in where[slot]:
            if loc in collected:
                continue
            if not set(plan["requirements"][loc]) <= held:
                continue
            # A Skip grants the whole puzzle; forcing is limited to what the
            # harness can perform.
            if buy or loc not in unreachable:
                got.add(loc)

        # Two Skip mechanisms, as in play(): `buy` re-opens a BEATEN slot to
        # release what is stranded on it; `unforced` is solve_level spending
        # one on an UNBEATEN level it could not force at all.
        unforced = (not buy and not got and slot not in beaten
                    and spent < stock["supply"])
        if buy or unforced:
            spent += 1
            skipped.add(slot)
        if unforced:
            got = {loc for loc in where[slot] if loc not in collected}

        for loc in got:
            collected.add(loc)
            take_item(placements.get(loc), held, stock)

        # A Skip completes the level at once, so its traps land inside the
        # completion grace and miss; only a forced visit gets knocked over.
        reset = (not (buy or unforced)
                 and any(is_part_location(loc) and placements.get(loc) == "Cat Trap"
                         for loc in got))

        # BEATEN MEANS THE TOKEN, as play() counts it.
        token = next((l for l in where[slot] if l.endswith(" - Beaten")), None)
        if token is None:
            if all(loc in collected for loc in where[slot]):
                beaten.add(slot)
        elif token in collected:
            beaten.add(slot)

        visits.append({"slot": slot, "level": slots[slot][1],
                       "skip": bool(buy or unforced), "got": len(got),
                       "beaten": slot in beaten, "reset": reset})

        if len(beaten) >= len(slots) and not got:
            break

        if got:
            idle = 0
            fruitless[slot] = 0
        else:
            idle += 1
            fruitless[slot] = fruitless.get(slot, 0) + 1
            if park and fruitless[slot] >= park_after:
                barren[slot] = frozenset(held)

    if final is not None:
        final.update(held=set(held), collected=set(collected),
                     beaten=set(beaten), idle=idle, barren=dict(barren))
    return len(beaten), attempts, spent, visits


def why_unclearable(plan, where, placements, final):
    """One line per level the paper run could not beat: what it still
    needs, and where that sits. Pure."""
    skipless = skipless_levels()
    where_is = {}
    for loc, item in placements.items():
        where_is.setdefault(item, loc)
    lines = []
    for i, (_, level_id) in enumerate(plan["slots"]):
        if i in final["beaten"]:
            continue
        if level_id in KNOWN_NOTHING_TO_FORCE:
            lines.append(f"{level_id} registers nothing until a player "
                         f"starts it - the harness cannot force it")
            continue
        token = next((l for l in where[i] if l.endswith(" - Beaten")), None)
        needs = sorted(set(plan["requirements"].get(token, ())) - final["held"])
        if not needs:
            lines.append(f"{level_id} was never finished (nothing it needs is "
                         f"missing - a pack or a Skip it never got)")
            continue
        for ability in needs:
            loc = where_is.get(ability)
            if loc is None:
                lines.append(f"{level_id} needs {ability}, which the seed "
                             f"never places")
                continue
            owner = next((plan["slots"][j][1] for j, locs in where.items()
                          if loc in locs), "")
            if (solution_number(loc) or 0) >= 2 and owner in skipless:
                how = ("an alternate solution on a generator level - forcing "
                       "makes one arrangement and the game cannot skip it")
            elif (solution_number(loc) or 0) >= 2:
                how = "an alternate solution, which only a Skip releases"
            else:
                how = "on a level the run never finished"
            lines.append(f"{level_id} needs {ability}, on {loc}: {how}")
    return lines


def plan_the_run(out_dir, seed_zip, plan, arrow=True, final=None):
    """paper_run for a generated seed: (beaten, Skips spent, visits).

    `arrow`: the full gate's arrow check beats arrow_slot in its own session
    first, so the run starts with it done. --quick has no arrow session.
    """
    where = locations_for_slots(plan)
    starting, placements = read_spoiler(out_dir, seed_zip)
    opening = [arrow_slot(plan["slots"], plan, where)] if arrow else []
    beaten, _attempts, spent, visits = paper_run(
        plan["slots"], plan, where, dict(placements), starting=starting,
        unreachable=harness_cannot_force(plan, where), production=True,
        opening=opening, final=final)
    return beaten, spent, visits


#: The first seed number the gate tries. What a number generates moves
#: whenever levels.json or the ids change, so generate_into checks each on
#: paper and walks on until one clears. droha: "We do occasionally change
#: the ids and levels so seeds might be changed? The e2e should double
#: check that before it runs".
GATE_SEED = 20260906
SEED_TRIES = 20


def credits_left_behind(placements, final):
    """The location holding the Credits item, if the paper run never
    collected it; None otherwise. Pure.

    The credits are an ITEM, placed like any other, and they open only
    once it is held and every slot is beaten. The DLC gate of 2026-09-24
    beat 15/15 with it still on Books Stacked (Seeing Stars) - Solution 2,
    an alternate solution forcing never makes.
    """
    where = next((loc for loc, item in placements if item == "Credits"), None)
    if where is None or where in final.get("collected", set()):
        return None
    return where


def judge_seed(out_dir, seed_zip, arrow):
    """The pre-flight for one generated seed: (clears, lines, plan).

    Clears only if every location is reachable in some order AND the
    harness's own scheduler, run on paper, beats every slot and collects
    the Credits item. On success plan carries "order" and "visits", which
    play() reports against.
    """
    plan = read_plan(out_dir, seed_zip)
    plan["order"], unreachable = completion_plan(out_dir, seed_zip, plan)
    n = len(plan["slots"])
    if unreachable:
        return (False, [f"{len(unreachable)} location(s) unreachable in any "
                        f"order - a generation bug: {unreachable[:3]}"], plan)
    final = {}
    beaten, spent, visits = plan_the_run(out_dir, seed_zip, plan,
                                         arrow=arrow, final=final)
    if beaten < n:
        _starting, placements = read_spoiler(out_dir, seed_zip)
        why = why_unclearable(plan, locations_for_slots(plan),
                              dict(placements), final)
        return (False, [f"the paper plan clears only {beaten}/{n}"]
                + ["   " + line for line in why[:4]], plan)
    _starting, placements = read_spoiler(out_dir, seed_zip)
    stranded = credits_left_behind(placements, final)
    if stranded:
        return (False, [f"the paper plan beats {n}/{n} but never collects "
                        f"the Credits item, on {stranded}"], plan)
    plan["visits"] = visits
    resets = sum(1 for v in visits if v["reset"])
    return (True, [f"the paper plan clears {n}/{n} in {len(visits)} "
                   f"visit(s), {spent} Skip(s), {resets} cat-trap reset(s)"],
            plan)


def generate_and_judge(yaml_dir, out, arrow, chosen, number):
    """Generate seed `number` into `out` and judge it: (clears, lines).
    Records (seed zip, plan) in `chosen`."""
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))
    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", yaml_dir,
         "--outputpath", out, "--seed", str(number)],
        cwd=AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stdout[-2500:], flush=True)
        print(r.stderr[-2500:], flush=True)
        sys.exit(f"generation failed for seed {number}")
    zips = [f for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        sys.exit(f"generation produced no seed for {number}")
    clears, lines, plan = judge_seed(out, zips[0], arrow)
    chosen[number] = (zips[0], plan)
    return clears, [f"seed {number}: {lines[0]}"] + lines[1:]


def generate_into(yaml_dir, out, yaml, arrow, report):
    """Write the yaml, then generate from GATE_SEED up until judge_seed
    accepts one. Returns (seed number, seed zip, plan); exits loudly if
    none of SEED_TRIES clears. `out` ends holding only the chosen seed."""
    for d in (yaml_dir, out):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(yaml_dir, "e2e.yaml"), "w", encoding="utf-8") as f:
        f.write(yaml)

    chosen = {}
    number = first_clearable(
        range(GATE_SEED, GATE_SEED + SEED_TRIES),
        functools.partial(generate_and_judge, yaml_dir, out, arrow, chosen),
        report)
    if number is None:
        sys.exit(f"REFUSING TO RUN: no seed from {GATE_SEED} to "
                 f"{GATE_SEED + SEED_TRIES - 1} can be cleared on paper")
    seed_zip, plan = chosen[number]
    return number, seed_zip, plan


def preflight_skips(out_dir, seed_zip, plan):
    """skip_shortfall for a generated seed: (demand, supply)."""
    _load_ability_tables()
    with open(os.path.join(REPO, "apworld", "alttl", "data", "names.json"),
              encoding="utf-8") as f:
        names = json.load(f)["levels"]
    run = {names.get(level_id, {}).get("display", level_id): level_id
           for _, level_id in plan["slots"]}
    starting, placements = read_spoiler(out_dir, seed_zip)
    return skip_shortfall(run, starting, placements, _ABILITY_NAMES)


#: Items worth spending a Skip to release: without them the run stalls.
#: Not a Skip: releasing one costs one.
SKIP_WORTHY = ("Progressive Puzzle Pack",)


def skip_shortfall(slot_displays, starting, placements, abilities):
    """(demand, supply) of Skips, from the seed alone. Pure.

    DEMAND is one Skip per level only a Skip can finish or empty: every
    UNFORCEABLE level in the run, plus every other level whose
    alternate solution (Solution 2+) holds an ability or pack, since
    forcing makes one arrangement. SUPPLY is the Skips in hand at the start
    plus those placed anywhere forcing reaches - which includes the PART
    checks of an UNFORCEABLE level, forced like any other.

    completion_plan cannot see this: it treats every location as earnable.
    The gate of 2026-09-23 stalled at 12/15 on a seed needing 3 and holding
    2, with Symmetry on Pencils Solution 2 gating two levels.

    `slot_displays` maps each run level's display name to its levelId.
    """
    unforceable = {d for d, level_id in slot_displays.items()
                   if level_id in UNFORCEABLE}
    needs = set(unforceable)
    supply = sum(1 for item in starting if item == "Skip")
    for location, item in placements:
        display = location.rsplit(" - ", 1)[0]
        alternate = (solution_number(location) or 0) >= 2
        waits = display in unforceable and not is_part_location(location)
        if item == "Skip" and not alternate and not waits:
            supply += 1
        if alternate and (item in abilities or item in SKIP_WORTHY):
            needs.add(display)
    return len(needs), supply

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


#: A row of the DevTools `locks` report.
LOCK_ROW = re.compile(
    r"\[(\d+)\] (.*?) type=(\S+) objects=(\d+) blocked=(\d+) dimmed=(\d+) "
    r"shared=(\d+) norenderer=(\d+) solved=(\S+)")

#: Written into the transcript when a level was refused because the run does
#: not hold its abilities. Distinct from EXHAUSTED_MARK, which means tried and
#: failed - a gated level has NOT been tried, must never buy a Skip, and comes
#: back on its own once the item arrives.
GATED_MARK = "harness: level gated, not attempted"

#: choose_slot's reason when a Skip is the ONLY way out of a deadlock:
#: nothing anywhere is playable, and every slot that could take a Skip is
#: still ability-gated.
#:
#: This exists so the verdict can tell an avoidable gate-paper from an
#: unavoidable one. On the base run of 2026-09-21 round 5 had one beaten
#: slot, every other slot ability-gated, and the missing ability sitting
#: on the beaten slot's own uncollected locations - a closed loop that
#: only a Skip opens. Failing the run for that is asking the harness to
#: deadlock instead; failing it for a gated Skip it did NOT have to spend
#: is the check worth keeping.
LAST_RESORT = "buy a skip: deadlocked, every candidate still gated"

#: The container locations the DLC yaml keeps progression off, as a
#: set the scheduler can consult. Same source as the yaml, so the two
#: cannot drift.
CONTAINER_EXCLUDES = frozenset(
    line.strip()[2:] for line in yaml_text(False, False, True).splitlines()
    if line.strip().startswith("- "))

#: Which controller indexes had been forced when the completion fired.
SOLVED_MARK = "harness: completed after solving "

#: solve_level publishes the CURRENT level's locked controller indexes
#: here, so the verdict never has to guess from prose that another
#: level may have written. "none" when nothing is locked.
LOCKED_MARK = "harness: locked controllers: "


def only_a_skip_can_finish(slot, where, collected):
    """Does this card hold something ONLY a Skip can release?

    Two kinds of location the harness can never earn by force-solving:

      * an ALTERNATE SOLUTION. Forcing a controller produces one
        arrangement; `Solution 2` is a different arrangement and the
        harness cannot make it. 121 locations in the table are these,
        70 of them DLC.
      * a CONTAINER the yaml already excludes - a drawer it cannot
        pull, a cupboard door it cannot swing.

    WHY THIS EXISTS INSTEAD OF AN ALLOWLIST. The surprise-Skip refusal
    asked `level_id not in KNOWN_UNFORCEABLE`, and that list holds two
    BASE-GAME levels. In a DLC run every legitimate Skip therefore
    looked like a surprise: the gate of 2026-09-21 refused one on DLC1
    Filing Cabinet - the card holding `Containers`, the exact card the
    run needed - and finished 2 of 8, down from 21/25 to 18/25.

    Derived from the seed rather than from a hand-kept list, so it is
    right for content nobody has written an allowlist entry for, and it
    is a pure function of (slot, where, collected) - checkable in
    milliseconds, unlike the Skip policy it replaces.
    """
    for location in where.get(slot, ()):
        if location in collected:
            continue
        number = solution_number(location)
        if number is not None and number >= 2:
            return True
        if location in CONTAINER_EXCLUDES or location in KNOWN_EARLY_COMPLETE:
            return True
    return False


def solution_number(location):
    """N from '<level> - Solution N', or None."""
    marker = " - Solution "
    if marker not in location:
        return None
    tail = location.rsplit(marker, 1)[1].strip()
    return int(tail) if tail.isdigit() else None


def has_uncollected(slot, where, collected):
    """Is there anything left on this card for a Skip to release?

    A Skip grants a puzzle's remaining locations. On a card where every
    location is already collected it grants NOTHING, and the mod says so
    - "skip: refused, slot 7 has nothing left to find".

    THE SKIP PATHS DID NOT CHECK THIS. On the DLC gate of 2026-09-21 the
    run reached round 21 needing one item, went looking for a slot to
    Skip, and picked DLC1 Daggers - which had nothing outstanding. The
    slot it needed was DLC1 Filing Cabinet, holding `Solution 2` with
    `Containers` on it, and DLC1 Bathroom Cupboard could not be beaten
    without it. The run finished 7 of 8 WITH THREE SKIPS STILL IN HAND.

    Safe to filter on, unlike gating: a card with nothing outstanding
    cannot yield anything to anybody, so removing it from the candidates
    loses nothing at all.
    """
    return any(location not in collected for location in where.get(slot, ()))


def waiting_on_an_ability(slot, plan, where, held, collected):
    """Would WAITING free anything still outstanding on this slot?

    Used to RANK the stall path's candidates, never to remove them.
    A slot whose outstanding locations all have their abilities held is
    the better place to spend a Skip: waiting cannot free it, so the
    Skip is the only route. A slot still behind an ability frees itself
    when the item lands, and Skipping it buys past a gate - which the
    gate asserts against and caught on the base run of 2026-09-21.

    USING THIS AS A FILTER COST TWO GATE RUNS. Nearly every beaten slot
    with work left is behind an ability - that is why the work is left -
    so filtering leaves no candidate and the run cannot finish.
    """
    requirements = plan.get("requirements", {})
    for location in where.get(slot, []):
        if location in collected:
            continue
        if not set(requirements.get(location, ())) <= set(held):
            return True
    return False


def beaten_slots(text, where):
    """Every slot whose Beaten token appears in `text`.

    READ THE TOKENS, DO NOT INFER FROM WHICH SLOT WAS OPEN. play() used
    to record a slot as beaten only when its token arrived in the round
    that slot was `current`, with a separate guess for slots carried in
    from an earlier session. Both failed on 2026-09-21:

      - the carried-in guess was `set(range(n))` from the mod's COUNT,
        which is "the first n slots" and named the wrong one whenever
        the arrow check did not start on slot 0;
      - the base run banked all 8 tokens and the harness recorded 7,
        because Desktop Computer's token arrived while another slot was
        current.

    Both disappear if the tokens are simply read and mapped back to
    their slots, which is what this does. The mod's word stays final -
    this only decides WHICH slot the mod was talking about.
    """
    out = {}
    for slot, locations in where.items():
        for location in locations:
            if not location.endswith(" - Beaten"):
                continue
            if f"beaten: {location}" in text:
                out[slot] = location
    return out


def locked_controllers(log):
    """Which controllers on the loaded level the player cannot touch.

    ASKED, NOT INFERRED. The obvious alternative is to map each controller's
    class through abilities.json and compare against the held set - and that
    re-derives the answer from the very table this is meant to audit, so it
    would agree with a wrong one every time.

    The mod's own "abilities: N locked" line is no better: it is emitted only
    when the summary CHANGES, once a second, and not at all when locks are off
    or nothing is connected, so its absence means four different things - and
    boot_level consumes it before this would ever see it.

    So the game is asked directly. `dimmed` counts objects wearing the exact
    grey AbilityLocks paints, which nothing else writes.

    THE LOAD RACE IS GONE, and this used to warn about it. AbilityLocks now
    applies on a postfix over ObjectController.RegisterObjectController, so a
    level is locked by the time it is on screen rather than on the next
    once-a-second pass. Confirmed by hand over nine levels on 2026-09-19:
    "instantly dimmed" every time. The poll survives only as a backstop, for
    an ability that arrives from the server while a level sits open.

    Still deliberately optimistic about a miss: reading an empty list for a
    level that is in fact gated costs one round and the level comes straight
    back, which is cheaper than a slower gate.
    """
    log.new()
    dev("locks", 1.0)
    out = log.wait(["locks: "], 15, 6, "the lock report")
    time.sleep(1.5)                      # the header is not the list
    out += log.new()

    locked = {}
    for line in out.splitlines():
        m = LOCK_ROW.search(line)
        if m and int(m.group(6)) > 0:
            locked[int(m.group(1))] = m.group(3)
    return locked, out


def table_gated(level_id, plan, held):
    """Controller names on this level whose group the seed's logic has not
    reached with `held`. Pure.

    The other half of what solve_level refuses. The grey says what the mod
    locks; this says what the logic requires, which is what the paper plan
    follows. A group can be locked without going grey - Paper Plane
    Supplies' Chalk DraggablesOrdered has seven objects with no renderer,
    shared with the open drawer - and forcing it anyway beat the level
    five visits ahead of the plan (third base gate, 2026-09-24). A part is
    judged by its own location; a level with one group by its Solution 1.
    """
    _load_names()
    entry = _NAMES["levels"].get(level_id) or {}
    display = entry.get("display", level_id)
    parts = entry.get("parts") or {}
    requirements = plan.get("requirements") or {}
    refused = set()
    for part in parts.values():
        loc = (f"{display} - {part['display']}" if len(parts) > 1
               else f"{display} - Solution 1")
        need = requirements.get(loc)
        if need is not None and not set(need) <= set(held):
            refused.update(part.get("members") or [])
    return refused


def listing_counts(text):
    """(registered, solved) from the last DevTools `controllers:` listing in
    `text`, or (0, 0). Pure."""
    registered, solved = 0, 0
    lines = text.splitlines()
    start = max((i for i, l in enumerate(lines) if "controllers: " in l
                 and " registered on " in l), default=None)
    if start is None:
        return 0, 0
    head = lines[start].split("controllers: ", 1)[1].split()[0]
    registered = int(head) if head.isdigit() else 0
    for line in lines[start + 1:]:
        if "controllers: " in line:
            break
        if "solved=True" in line:
            solved += 1
    return registered, solved


def pass_progress(before, after):
    """Did a pass move the level on - another controller registered, or
    another solved? Pure. A progressive level reveals one phase per pass
    (TupperwareNesting: 2, 3, ... 8 registered), and such a pass is not the
    level refusing to finish."""
    (reg_before, solved_before) = listing_counts(before)
    (reg_after, solved_after) = listing_counts(after)
    return reg_after > reg_before or solved_after > solved_before


#: Seconds a fully solved level may take to report complete. Measured
#: 2026-09-24 with DevTools alone: DLC2 Broken Vases raised LevelComplete
#: 13 s after its Draggables were solved, because its Pannables (Action
#: Only) plays first. The old 6 s wait called it exhausted and the DLC arrow
#: check failed there. The all-levels sweep then measured the slowest at 19 s
#: (DLC1 Boss), 17 s (DLC2 Windows) and 16 s (MedicineCabinet), so 30.
COMPLETION_WAIT = 30


def completion_wait(level_id):
    """The completion wait for one level: COMPLETION_WAIT, or longer where
    the sweep measured this level slower. DLC2 Curtains completed 49 s after
    it was forced (recheck, 2026-09-24). A multi-pass figure counts every
    pass, so only a one- or two-pass measurement is read as a delay."""
    row = FORCEABILITY.get(level_id) or {}
    took = row.get("all")
    if took is None or (row.get("passes") or 1) > 2:
        return COMPLETION_WAIT
    return max(COMPLETION_WAIT, took + 15)


def solve_level(log, refuse=frozenset(), wait=None):
    """Solve every controller until the level reports complete.

    `refuse`: controller names the seed's logic has not reached yet
    (table_gated). Refused like a greyed one, so the run never gets ahead
    of its paper plan on a lock the mod cannot paint. `wait`: how long a
    solved level may take to complete (completion_wait); COMPLETION_WAIT
    when not given.

    SEVERAL PASSES, because one is not enough and the reason is a feature.
    A Cat Trap resets the puzzle, and one arrived mid-level on the very first
    attempt: three controllers were solved, the cat knocked them over, the
    remaining eight were solved, and the level never completed because the
    first three were unsolved again. The single pass reported NOT beaten and
    looked like the mod failing to notice a finished puzzle.

    So each pass asks which controllers are actually unsolved and solves those,
    up to five times. Traps are deliberately left on at their default rate:
    a run where the cat never interferes is not the run players get.

    IT ALSO REFUSES A PUZZLE THE RUN HAS NOT UNLOCKED, which it did not used to
    and which made every ability gate in a run invisible to this harness.
    `solve:` sets a controller's solved flag and dispatches its event; the
    dimmer only ever touches the LevelObjects. The two never meet, so a forced
    solve walks straight through a gate - droha watched the gate finish DLC2
    Pizza with all 48 of its toppings greyed out on screen.
    """
    text = ""

    # PER CONTROLLER, NOT PER LEVEL, and the difference is the whole run.
    #
    # Refusing the whole level was written first and measured: the gate stalled
    # at 1 of 8 puzzles with 16 refusals, because a partially gated level still
    # has checks a player can earn. A part location needs only ITS OWN group's
    # abilities (see rules.part_requirements) while the solution and Beaten
    # whole union, so the right behaviour is to solve what is reachable, bank
    # those parts, and leave the level unfinished - which is exactly what a
    # player does. Refusing the lot forfeits the very checks that would have
    # unlocked the rest of the run, and starves it.
    gated, seen = locked_controllers(log)
    text += seen
    # THE LOGIC'S REFUSALS JOIN THE MOD'S, by name from the same report.
    # The grey still catches a group the table calls free (Fruit Stickers'
    # Remove Stickers, 2026-09-24); this stops a group the table calls
    # locked from being forced because nothing on it could be painted.
    unreached = {}
    for line in seen.splitlines():
        m = LOCK_ROW.search(line)
        if m and m.group(2) in refuse and int(m.group(1)) not in gated:
            unreached[int(m.group(1))] = m.group(3)
    if unreached:
        say(6, "not forcing " + ", ".join(sorted(set(unreached.values())))
               + " - the seed's logic has not reached "
               + ("them" if len(unreached) > 1 else "it"))
    painted = dict(gated)
    gated = {**gated, **unreached}
    # PUBLISH IT, for THIS level, in a form nothing else can imitate.
    #
    # play() used to decide "was the mod still gating this level" by
    # scanning the chunk for any line containing "waiting on ". That
    # string comes from AbilityLocks' once-a-second summary of whatever
    # level is ACTIVE, and a level loading behind the one being solved
    # writes its own. On the base gate of 2026-09-21 Stamps reported
    # "0 locked, 1 open, 9 objects" and was skipped as STILL GATED,
    # because summaries for an 18- and a 24-object level landed in the
    # same window - Stamps has nine. The Skip was correct and the
    # verdict was wrong.
    #
    # locked_controllers asks the game about the loaded level and
    # returns indexes, so this marker is scoped to the right level.
    text += (f"\n{LOCKED_MARK}"
             + (",".join(str(i) for i in sorted(gated)) if gated else "none")
             + "\n")
    if painted:
        say(6, "not forcing " + ", ".join(sorted(set(painted.values())))
               + " - the run cannot touch "
               + ("them" if len(painted) > 1 else "it"))

    # A CAT TRAP MUST NOT COST A PASS.
    #
    # The trap is an item at a FIXED location in the seed, so the spoiler
    # says exactly which check sends it and which level it knocks over (see
    # planned_passes). This comment used to call it unpredictable, which it
    # never was. What it does is knock the puzzle over mid-solve,
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
    solved_so_far = []
    budget = 5
    refunds = 0
    MAX_REFUNDS = 6
    # A PHASE THAT APPEARS IS NOT A WASTED PASS. TupperwareNesting registers
    # one controller per phase, eight in all, and the 2026-09-24 gate ran out
    # of its five passes on Large Square with three phases still to come.
    # Bounded, so a level that keeps changing still ends.
    phase_refunds = 0
    MAX_PHASE_REFUNDS = 12
    last_listing = ""
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
        if attempt and pass_progress(last_listing, out) \
                and phase_refunds < MAX_PHASE_REFUNDS:
            phase_refunds += 1
            budget += 1
            registered, solved = listing_counts(out)
            say(6, f"the level moved on ({registered} controller(s), {solved} "
                   f"solved) - that pass does not count "
                   f"({phase_refunds}/{MAX_PHASE_REFUNDS})")
        if "controllers: " in out:
            last_listing = out

        # DROP THE ONES THE RUN CANNOT REACH. Forcing them is what let the gate
        # walk through every ability gate in the run; leaving them is what a
        # player has to do. The level then simply does not complete, which is
        # the honest outcome and not an error.
        if gated:
            blocked_now = [i for i in todo if i in gated]
            todo = [i for i in todo if i not in gated]
            if blocked_now and not todo:
                # Nothing reachable is left undone, so this visit has taken the
                # level as far as the run's abilities allow. NOT exhausted -
                # that would buy a Skip, and a Skip must never paper over a
                # gate (see the "no Skip covered for a level the mod was still
                # gating" assertion).
                say(6, "everything still unsolved here is locked; leaving it "
                       "for when the abilities arrive")
                return False, text + GATED_MARK

        if not todo and "solved=" not in out:
            # No listing arrived at all - do not read that as "all solved".
            say(6, "the controller listing did not arrive; retrying")
            continue

        if not todo:
            # Every controller is solved. Either the completion already fired
            # and was read above, or it is about to.
            more = log.wait(["LevelComplete ", "no level running"],
                            wait or COMPLETION_WAIT, 6, "the completion")
            text += more
            finished = ("LevelComplete " in more
                        or "no level running" in more)
            if not finished:
                # EXHAUSTED: nothing left to solve and the game still will
                # not call it done. Marked in the returned text because
                # that text is the only channel back to the caller - the
                # say() above goes to stdout, and a first attempt at this
                # grepped for that message and so never matched anything.
                #
                # The clock goes in the log too. The DLC gate of 2026-09-24
                # forced DLC1 Boss after a boot that found timeScale 0, and
                # no solved event or completion ever arrived; five isolated
                # repeats all completed. A frozen clock holds a completion
                # but not the solved events, so it was not that alone.
                dev("time", 1.0)
                text += log.new()
                text += "\n" + EXHAUSTED_MARK + "\n"
            return finished, text

        if attempt:
            say(6, f"pass {attempt + 1}: {len(todo)} controller(s) still unsolved")

        trapped = False
        for i in todo:
            dev(f"solve:{i}", 0.9)
            solved_so_far.append(i)
            chunk = log.new()
            text += chunk
            if "cat(s) reset the puzzle" in chunk:
                trapped = True
            if "LevelComplete " in chunk or "no level running" in chunk:
                # WHAT THE LEVEL ACTUALLY NEEDED. The completion arrived after
                # these controllers and no others, so anything the level lists
                # beyond them was not required to finish it. That is the
                # measurement behind "TupperwareTower declares four abilities
                # and hand-testing suggested fewer" - recorded rather than
                # argued about.
                # WHY a controller went unforced matters as much as that it
                # did. "Not forced" covers two different things: LOCKED, so we
                # would not touch it, and ALREADY SOLVED when the level
                # loaded, so there was nothing to do. Only the first says
                # anything about what the level requires, and the first
                # version of this report conflated them - MedicineCabinet
                # looked like it completed without controller [0] when [0] is
                # Draggables, the baseline verb, which can never be locked.
                forced = ",".join(str(x) for x in solved_so_far)
                was_locked = ",".join(str(x) for x in sorted(gated)) or "none"
                text += ("\n" + SOLVED_MARK + forced
                         + " | locked: " + was_locked + "\n")
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
                     "hints", "navigation", "daily guard", "title screen",
                     "ability locks", "card stars")


def why_no_connection(text):
    """Why the mod did not connect, in a sentence, or None if it did.

    THE FAILURE THIS EXISTS FOR looks like nothing at all: a 150-second
    countdown and "never connected". Measured 2026-09-18 - with the Steam
    client closed, the game's DLCManager reports no DLC at all, so the mod
    refuses any seed built with one and never connects. Both a gate run and a
    probe died that way in a row, and the first suspect was a Harmony patch
    added minutes earlier, because nothing read the two lines that said
    exactly what had happened:

        DLC installed: none
        refusing the seed: this seed was built with ... which is not installed

    The tell in the DevTools dump is the daily count - 16 instead of 36.

    Launching the exe directly is still correct; it avoids the "no license"
    failure that steam:// gives on a family-shared copy. But DLC are
    entitlements, and only a running Steam client can vouch for them.
    """
    if "connected. " in text:
        return None

    refusal = line_with(text, "refusing the seed")
    if refusal:
        if "DLC installed: none" in text:
            return ("the game reports no DLC installed, so the mod refused a "
                    "DLC seed - START STEAM, or generate without DLC. " +
                    refusal.split("refusing the seed: ", 1)[-1].strip())
        return refusal.split("] ", 1)[-1].strip()

    if "no server at launch" in text:
        return ("the game found no server when it launched - it was started "
                "before MultiServer was listening")
    return None


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

    Returns (slot the arrow opened, a slot that would count as correct) -
    or (None, None). They are equal when the arrow opened another puzzle of
    this run, which is the property being asserted; see the note below.
    """
    slots = plan["slots"]
    by_name = {name: i for i, (_, name) in enumerate(slots)}

    text = launch_and_connect(log, 5, "the connection for the arrow check")
    if "connected. " not in text:
        return None, None, text

    # A SLOT THE ARROW CHECK CAN ACTUALLY FINISH, not slot 0.
    #
    # This opened slots[0] unconditionally, which only works when the first
    # slot happens to need no abilities. The base game usually obliges; the
    # DLC seed put DLC2 Pizza there, gated behind Distributing, so the
    # session could not complete it, never pressed the arrow, and failed
    # both "the next-level arrow opens the run's next puzzle" and "the pause
    # menu Exit leaves the level" - two assertions about navigation reported
    # as broken because of an unrelated ability gate.
    #
    # Nothing is held in this session, so the test is the one slot_has_work
    # already answers with an empty inventory. Falling back to slot 0 keeps
    # the old behaviour when no slot qualifies, rather than skipping the
    # check silently.
    where = locations_for_slots(plan)
    first = arrow_slot(slots, plan, where)
    if first != 0:
        say(5, f"arrow check using slot {first} {slots[first][1]} - "
               f"slot 0 {slots[0][1]} needs abilities this session lacks")

    index, level_id = slots[first]
    opened, out = boot_level(log, index)
    text += out
    if not opened:
        close_game()
        return None, None, text

    done, chunk = solve_level(log, refuse=table_gated(
        level_id, plan, set(plan.get("starting_abilities") or ())),
        wait=completion_wait(level_id))
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
    # ANY OTHER SLOT OF THIS RUN, and deliberately not a specific one.
    #
    # The old assertion demanded slot 1, which only held because the check
    # always started on slot 0. Computing "the lowest unfinished slot"
    # instead was also wrong: starting on slot 2, the arrow opened slot 4,
    # not slot 3. The mod's ordering is its own business and encoding a
    # guess at it makes this test fail on a working arrow.
    #
    # What the test is FOR is narrower than that. Next used to let the game
    # route by level kind, which sends a daily-pool puzzle to the Daily Tidy
    # page and drops the player out of their run entirely. So the question
    # is "did it open another puzzle of this run", and that is what is
    # asserted - a name that is not in the run, or no level at all, still
    # fails.
    got = by_name.get(name)
    expected = got if (got is not None and got != first) else first
    say(5, f"      the arrow opened "
           f"{('slot ' + str(got) + ' ' + name) if got is not None else (name or 'nothing')}"
           f", started on slot {first} {slots[first][1]}")

    text += check_pause_exit(log)

    close_game()
    time.sleep(2)
    return got, expected, text


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


def play(log, plan, earlier=""):
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
    planned = plan.get("visits") or []
    visit = [0]
    set_progress(lambda: (len(beaten), len(slots), visit[0], len(planned)))
    skipless = skipless_levels()
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
    #: Levels that reached the skip path without being on KNOWN_UNFORCEABLE.
    #: The Skip is REFUSED rather than spent, so these are the run's real
    #: defects instead of an aggregate discovered after the supply is gone.
    surprises = []
    surprised = set()
    #: Slots whose Skip was the only alternative to a deadlock.
    last_resort = set()
    needed = []
    #: Slots gone back to after everything was beaten, to pick up checks that
    #: were gated by an ability at the time. One pass each; see the revisit
    #: block below for why it exists and why it cannot spin.
    revisited = set()
    #: True once the mod has refused a Skip for want of supply, until one
    #: arrives. Read from its own words rather than counted here: the run
    #: starts with some, earns more as items, and the harness has no other
    #: view of the balance.
    skips_out = False
    #: slot -> what the run held when a visit there collected nothing.
    barren = {}
    #: slot -> consecutive attempts that collected nothing. Reset by any
    #: progress; see the parking note below for why one miss is not enough.
    fruitless = {}
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

    where = locations_for_slots(plan)

    first = launch_and_connect(log, 5, "the connection")
    if "connected. " not in first:
        # SIX VALUES, matching the return at the end of this function.
        # This returned five, and main() unpacks six - so the one path
        # written to report "connected to the server: FAIL" cleanly
        # raised ValueError instead, and the report it exists to print
        # was unreachable from the moment `spent` was added.
        return [], False, first, 0, first, [], []
    # What the arrow session filed, carried in: the mod logs `no location
    # for it` for an already-filed location, so without these the run sees
    # Stamps as unfinished and revisits it for nothing (2026-09-24 gate).
    transcript = carried_checks(earlier) + first
    open_slots = open_count(first, plan["boundaries"][0])

    say(6, f"{len(slots)} slots, boundaries {plan['boundaries']}, "
           f"{open_slots} open")

    # WHICH slots an earlier session already beat - not how many.
    #
    # THIS READ `restored = set(range(n))`, from the mod's "N puzzle(s)
    # beaten" count: "the first n slots". On 2026-09-21 the arrow session
    # beat slot 2 (DLC1 Clock Cupboard) and n was 1, so the harness marked
    # slot 0 restored and spent the whole run demanding a second Beaten
    # token from slot 2. It will never come - the mod does not re-file a
    # location the ledger already has, which is correct. Slot 2 was
    # revisited in rounds 2, 6, 10 and 19, parked twice, and finally bought
    # a Skip; the run ended reporting 5 of 8 when it had really done 6.
    #
    # The earlier session's own transcript names the level it beat, so read
    # that rather than guessing at indices.
    for i in beaten_slots(earlier, where):
        restored.add(i)
        say(6, f"slot {i} {slots[i][1]} was beaten in an earlier "
               f"session - not demanding a second token")

    # The mod's count is the cross-check. If it disagrees with what the
    # transcript named, SAY SO: the run is about to spend rounds on a slot
    # it cannot satisfy, and silence there is what cost the last gate.
    for line in transcript.splitlines():
        if "run state:" in line and "puzzle(s) beaten" in line:
            try:
                n = int(line.split("hint page(s) opened, ")[1].split(" puzzle")[0])
            except (IndexError, ValueError):
                n = 0
            if n != len(restored):
                say(6, f"WARNING: the mod carried {n} beaten puzzle(s) but "
                       f"the earlier transcript names {len(restored)}. A "
                       f"slot that is beaten and unnamed will be asked for "
                       f"a token it can never send.")

    current = None          # slot index of the puzzle now open, or None
    for step in range(1, MAX_ROUNDS + 1):
        # ALL BEATEN IS NOT THE SAME AS DONE. Beating every puzzle leaves
        # behind any part that was gated by an ability at the time, and the
        # goal can sit behind exactly one of those - see the revisit block
        # below. Stop when the credits are open, or when a revisit pass has
        # been spent on every slot and there is nothing further to try.
        #
        # READ FIRST, THEN DECIDE. The last token's credits come back from
        # the server a moment after it: on 2026-09-24 `credits: unlocked`
        # landed nine log lines after 15/15, this loop tested a stale flag,
        # and began a 26th visit the paper plan (rightly) did not have.
        transcript += log.new()
        if len(beaten) >= len(slots) and "credits: unlocked" not in transcript:
            transcript += log.wait(["credits: unlocked"], 15, 6, "the credits")
        if "credits: unlocked" in transcript:
            credits = True
        if len(beaten) >= len(slots) and (credits or revisited >= beaten.keys()):
            break

        was = open_slots
        open_slots = open_count(transcript, open_slots)
        if open_slots > was:
            say(6, f"a pack opened more: {open_slots} slot(s) now available")

        # What the run holds RIGHT NOW. Read once per round, because both
        # the playable filter and the revisit rule below depend on it.
        held = run_holds(transcript, plan)

        # Nothing open, or the arrow led somewhere unusable: pick a slot and
        # open it the long way.
        if current is None:
            # THE WHOLE DECISION LIVES IN choose_slot, which is pure and
            # tested in tools/test_scheduler.py.
            #
            # It used to be thirty lines inline here, and every scheduling
            # bug this harness has had was in them - each one found by
            # running a fifteen-minute gate and reading the wreckage, on a
            # freshly generated seed that made consecutive runs
            # incomparable. It is arithmetic over sets; it belongs
            # somewhere it can be tested in milliseconds, and the tests are
            # mutation-checked so they are known to fail when the bugs
            # come back.
            collected = collected_locations(transcript)
            current, why, buy_skip = choose_slot(
                slots, plan, where,
                open_slots=open_slots, beaten=set(beaten), attempts=attempts,
                barren=barren, held=held, collected=collected,
                skipped=skipped, credits=credits, idle=idle,
                skips_out=skips_out, skipless=skipless)

            if current is None:
                say(6, f"{len(beaten)}/{len(slots)} beaten and "
                       f"{open_slots} open - {why}")
                break

            if buy_skip:
                skip_anyway.add(current)
                if why == LAST_RESORT:
                    last_resort.add(current)
                say(6, f"{open_slots} of {len(slots)} slots "
                       f"open and nothing solvable - an item is on a "
                       f"location the harness cannot reach; spending a "
                       f"Skip to release it")
            elif why == "revisit for gated checks":
                if REVISIT_MARK not in transcript:
                    transcript += "\n" + REVISIT_MARK + "\n"
                say(6, f"all beaten but the credits are not "
                       f"open - revisiting for checks that were gated")

            if current in beaten:
                revisited.add(current)
            index, level_id = slots[current]

            # THE SEED IS FIXED, SO EVERY VISIT IS KNOWN IN ADVANCE: the paper
            # plan (plan_the_run) is this same scheduler run over the spoiler.
            # A visit that is not the planned one means the model or the mod
            # is wrong, and the rounds after it would only compound that.
            visit[0] += 1
            astray = off_plan(visit[0], level_id, planned)
            if planned and astray:
                say(6, f"UNEXPECTED: {astray}. Stopping the run - unit test "
                       f"this, then fix it.")
                transcript += "\n" + OFF_PLAN_MARK + "\n"
                break

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
                say(6, f"slot {current} {level_id} did not open "
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

        # A REVISIT IS A VISIT, NOT A RE-SOLVE.
        #
        # Coming back to a beaten level exists to collect checks that were
        # gated last time, and the mod does that by itself: the level
        # restores its solved controllers on load and SweepAlreadySolved
        # files whatever has since become reachable. Forcing them again is
        # not just redundant, it actively breaks the run - the game raises
        # "already solved as far as the level is concerned" for every one,
        # which trips "no solve threw inside the game", and the harness reads
        # the throw as "cannot be force-solved" and spends a Skip on a level
        # that never needed one. That cost two Skips and three assertions.
        # UNLESS IT WAS PICKED TO HAVE A SKIP SPENT ON IT. The stuck path
        # above deliberately chooses an already-beaten slot so solve_level
        # will buy a Skip and release an item stranded on a location the
        # harness cannot force - a pack behind a drawer, typically. Short
        # -circuiting every beaten slot swallowed that: the Skip was
        # announced, solve_level never ran, nothing was spent, and the DLC
        # gate cycled "spending a Skip to release it" for nineteen rounds
        # at 3 of 8 without ever spending one.
        if current in beaten and current not in skip_anyway:
            say(6, f"revisiting {level_id} for checks that "
                   f"are reachable now - not re-solving it")
            before = len(collected_locations(transcript))
            transcript += log.wait(["check:", "checks:", "beaten:"], 12, 6,
                                   "the gated checks")
            last_done = False

            # A VISIT THAT COLLECTS NOTHING MUST NOT REPEAT. slot_has_work
            # believing there is something here, and the visit finding
            # nothing, means the two disagree - and without this the loop
            # simply asks again forever. Parking the slot until the run
            # holds something new turns an infinite spin into one wasted
            # round, and the count below makes the disagreement visible
            # rather than silent.
            if len(collected_locations(transcript)) == before:
                barren[current] = frozenset(held)
                say(6, f"{level_id} had nothing to collect "
                       f"after all - parking it until an item arrives")

            # RELEASE THE SLOT. The top of the loop only picks a new one
            # when `current is None`; leaving it set means the next round
            # skips selection entirely and comes straight back here, which
            # is why parking Stamps (Randomized) did nothing and the run
            # revisited it from round 9 to round 52. The barren guard was
            # never even consulted.
            current = None
            continue

        collected_before = len(collected_locations(transcript))
        # A spent Skip leaves a finished level behind (or, where the mod fell
        # back, the track) even when it banks no new token: see last_done.
        skip_spent = False
        if current in beaten and current in skip_anyway:
            # A SKIP BOUGHT FOR A BEATEN SLOT IS SPENT WITHOUT FORCING FIRST,
            # the way a player releases what is left: open it, press Skip.
            # Forcing re-completed beaten Pencils on 2026-09-24, the mod moved
            # on to Fruit Stickers, and the Skip landed there. 13 of 15.
            done, chunk, tail = False, "", ""
        else:
            refuse = table_gated(slots[current][1], plan, held)
            done, chunk = solve_level(
                log, refuse=refuse,
                wait=completion_wait(slots[current][1]))
            transcript += chunk
            tail = log.wait(["beaten:", "check:", "credits:"], 10, 6, "the check")
            transcript += tail

        # AN ATTEMPT THAT ACHIEVED NOTHING MUST NOT REPEAT - the unbeaten
        # half of the `barren` guard; the beaten half is in the revisit
        # branch above.
        #
        # slot_has_work reads the seed's table, which says what a PLAYER
        # could earn. The harness is weaker: it forces controller flags and
        # cannot pull a drawer. Where the two disagree a level looks
        # permanently worth visiting.
        #
        # REVERTED ONCE, AND RE-ADDED ON EVIDENCE. The first attempt cost a
        # DLC gate 23/25 down to 19/25, because the Skip path then fired on
        # "no candidates left" and parking emptied that list sooner,
        # changing which levels got Skips. That trigger is now elapsed idle
        # rounds, so parking cannot move Skip timing. Simulated both ways
        # over a whole run (see TestAWholeRun): with Skips available the
        # results are identical - 3 of 3, worst 2 attempts either way -
        # and with Skips exhausted parking is the difference between 2
        # attempts and 199.
        if not done and len(collected_locations(transcript)) == collected_before:
            fruitless[current] = fruitless.get(current, 0) + 1
            # TWICE IN A ROW, NOT ONCE. Parking on a single empty attempt
            # assumes "collected nothing" means "nothing collectable until
            # an item arrives", and the harness breaks that assumption by
            # itself: a cat trap resets a puzzle mid-solve, and a phased
            # level reveals controllers only on a later visit. Both yield
            # nothing once and something next time with no item in between.
            #
            # Parking on the first miss ended a DLC gate at 2 of 8 where
            # the same seed reached 6 of 8 without parking - worse than the
            # grinding it was added to prevent. The offline simulation had
            # approved it because it models no transient failures at all.
            if fruitless[current] >= 2:
                barren[current] = frozenset(held)
                say(6, f"{level_id} yielded nothing twice "
                       f"running - parking it until an item arrives")
        else:
            fruitless[current] = 0

        # WHAT THIS LEVEL ACTUALLY NEEDED. solve_level records which
        # controllers had been forced when the completion fired; only here is
        # the level's NAME known, so the two are joined up now. A level that
        # completes after fewer controllers than it declares abilities for is
        # declaring a requirement it does not have - the TupperwareTower
        # question, answered by measurement.
        if SOLVED_MARK in chunk:
            forced = chunk.split(SOLVED_MARK, 1)[1].split("\n")[0].strip()
            needed.append((level_id, forced))

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
        if (forced and not done) or (not done and exhausted and current not in skipped):
            # READ THE GATING BEFORE SPENDING, because spending destroys the
            # evidence: a Skip grants every location on the slot, so once it
            # lands there is no way to tell whether the level was unfinishable
            # or merely still waiting on an ability the mod had not granted.
            # Both arrive here as EXHAUSTED. Only one of them is acceptable.
            gated = False
            for line in chunk.splitlines():
                if LOCKED_MARK in line:
                    gated = line.split(LOCKED_MARK, 1)[1].strip() != "none"

            # A SKIP NOBODY EXPECTED IS A FAILURE, AND IT DOES NOT GET TO
            # PAY FOR ITSELF FIRST.
            #
            # This used to spend the Skip and report the surprise at the
            # END, as an aggregate. On the 2026-09-21 DLC gate that meant
            # four surprise Skips drained a supply of five, the run
            # starved at 5 of 8 in round 15, and FIVE of the six failures
            # - every puzzle beaten, the mod agreeing, the credits, the
            # goal, the server's goal - were downstream of the drain
            # rather than defects of their own. One bug read as six, and
            # the real one was fifteen minutes from the top of the log.
            #
            # So: refuse, say so where it happens, and keep the supply
            # for the slots that legitimately need it. The run continues
            # rather than aborting, because the remaining assertions
            # still have something true to say - but it has already
            # failed and the report will say why.
            # REFUSING ON `gated` WAS TRIED AND REVERTED, 2026-09-21.
            #
            # It looks right - "never buy past a gate" is what the
            # assertion below says - and it took the base gate from
            # 23/25 to 18/25, stalled at 1 of 8. `gated` is
            # any("waiting on ") over the chunk, which is ALSO true for
            # a level that is only PARTLY gated: some controllers
            # locked, others solvable, the level unable to complete
            # either way. Refusing there leaves the level unfinishable
            # for the rest of the run instead of merely reporting a
            # problem at the end.
            #
            # A Skip spent on a gated level is still a failure - the
            # verdict below still fails on it. The place to stop it is
            # choose_slot, which should not offer a slot whose
            # remaining work is ability-gated; that is arithmetic over
            # sets and belongs in test_scheduler, not here.
            surprise = (not forced
                        and level_id not in UNFORCEABLE
                        and not only_a_skip_can_finish(
                            current, where, collected_locations(transcript)))
            # DEFER A GATED SKIP WHILE THE RUN IS STILL MOVING.
            #
            # A level that is unforceable AND still ability-gated does
            # not need its Skip yet. Spending now grants every gated
            # location on the card outright, which is the papering the
            # verdict below exists to catch; waiting costs nothing
            # while other slots are still yielding, and by the time the
            # run is stuck the ability has usually arrived and only the
            # genuinely unforceable part needs buying.
            #
            # THE ESCAPE MATTERS. Refusing a gated Skip outright was
            # tried and deadlocked the run at 1 of 8: the ability can
            # be stranded behind the very level being refused. Gating
            # on `idle >= STUCK_AFTER` keeps that exit open - once
            # nothing else is making progress, the Skip is spent
            # regardless and the verdict reports it.
            defer = gated and not forced and idle < STUCK_AFTER
            if defer:
                say(6, f"slot {current} {level_id} is unfinishable but "
                       f"STILL ABILITY-GATED - holding the Skip while "
                       f"other slots are still moving "
                       f"(idle {idle}/{STUCK_AFTER})")

            refuse = surprise
            if refuse:
                if current not in surprised:
                    surprised.add(current)
                    surprises.append((level_id, gated))
                    why = ("the mod is STILL ABILITY-GATING it, and a Skip "
                           "here would paper over exactly the bug this run "
                           "exists to catch"
                           if gated else
                           "it is not a level only a Skip finishes, and spending "
                           "here starves the slots that legitimately need "
                           "one - which turned one defect into six "
                           "unrelated-looking failures")
                    say(6, f"REFUSING a Skip on slot {current} "
                           f"{level_id}: {why}.")

            # NOT `continue`. The end of this round resets `current`, and
            # skipping that is what once span the same slot for 43 rounds.
            if not refuse and not defer:
                # THE SKIP LANDS ON WHATEVER IS RUNNING, so look first. A
                # wrong level here is not something to work around: stop,
                # say what was expected and what was found, and fail.
                here = await_skip_target(log, index)
                transcript += here
                if not skip_target_ok(chunk + tail + here, index):
                    say(6, f"UNEXPECTED: a Skip bought for slot {current} "
                           f"{level_id} (index {index}) would land on "
                           f"{active_level(here) or 'no level at all'}. "
                           f"Stopping the run - unit test this, then fix it.")
                    transcript += "\n" + WRONG_SKIP_MARK + "\n"
                    break
                log.new()
                dev("skip", 1.5)
                more = log.wait(list(SKIP_VERDICTS), 12, 6, "the skip")
                if skip_outcome(more) == "waiting":
                    # The game may still skip late; the mod charges when it
                    # does (Skips.cs).
                    more += log.wait(["skip: spent one"], 12, 6, "a late skip")
                transcript += more
                chunk += more
                tail += more
            else:
                more = ""
            outcome = skip_outcome(more)
            skip_spent = outcome == "spent"
            if outcome in ("not used", "waiting"):
                # The Skip did nothing: the game never skipped and the mod did
                # not release the slot. Every level should get one or the
                # other since 2026-09-25, so this is a bug - stop.
                say(6, f"UNEXPECTED: a Skip on slot {current} {level_id} did "
                       f"nothing ({outcome}). Stopping the run - unit test "
                       f"this, then fix it.")
                transcript += "\n" + NOT_USED_MARK + "\n"
                break
            if outcome == "spent":
                # LATCHED ONLY ON A SPEND. This used to mark the slot before
                # asking, so a level that reached the skip path before any
                # Skip item had arrived was written off permanently - and
                # Skips are items, so early in a run there are none. Desktop
                # Computer asked once in round 2, was told there was nothing
                # to spend, and was never offered another chance across the
                # next seventeen rounds while two Skips sat in the inventory.
                skipped.add(current)
                spent.append((level_id, gated,
                              "last-resort" if current in last_resort
                              else "unreachable-check" if forced
                              else "unforceable"))
                say(6, f"slot {current} {level_id} cannot be force-solved; "
                       f"spent a Skip"
                       + (" WHILE STILL ABILITY-GATED" if gated else ""))
            elif outcome == "refused":
                # THE SUPPLY IS OUT until an item says otherwise. Without
                # this the stall recovery keeps choosing a slot for a Skip
                # that cannot be bought, the slot is never marked skipped,
                # and the round repeats forever - a DLC gate cycled from
                # round 20 at five of eight doing exactly that.
                skips_out = True
                say(6, f"slot {current} {level_id} cannot be force-solved "
                       f"and there is no Skip to spend yet")
            done = "beaten:" in more

        # A Skip arriving refills the supply the refusal above emptied.
        if "received item: Skip" in (chunk + tail):
            skips_out = False

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

        # Unwind before the next boot after any finished level, not only one
        # that banked a token: a Skip on an already-beaten DLC1 Boss (DLC
        # gate, 2026-09-25) completed it with no new token, the next boot went
        # straight over it, and StartLevel did not take.
        last_done = done or skip_spent
        if done:
            beaten[current] = level_id
            idle = 0
            last_progress = time.time()
        else:
            idle += 1

        # RECONCILE AGAINST EVERY TOKEN SEEN SO FAR, not just this
        # round's slot. A Beaten token can land while a DIFFERENT slot
        # is current - a Skip payout, or a revisit that completes a
        # level the harness was not asking about - and recording only
        # `beaten[current]` loses it. The base gate on 2026-09-21 ended
        # with the mod having banked 8 and the harness having counted
        # 7, which failed "all 8 puzzles beaten" while "the mod agrees
        # every puzzle was beaten" passed in the same report.
        for slot_index, token in beaten_slots(transcript, where).items():
            if slot_index not in beaten:
                beaten[slot_index] = slots[slot_index][1]
                say(6, f"slot {slot_index} {slots[slot_index][1]} banked "
                       f"its token ({token}) while another slot was open")

        if "credits: unlocked" in transcript:
            credits = True

        say(6, round_line(current, level_id,
                          'beaten' if done else 'not finishable yet' + blocked,
                          open_slots, len(slots)))

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
    if needed:
        say(6, "what each level actually needed to complete:")
        for level_id, forced in needed:
            say(6, f"  {level_id}: forced {forced}")

    return (list(beaten.values()), credits, transcript, open_slots, first,
            spent, surprises)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default="release-test")
    parser.add_argument("--clean-only", action="store_true")
    parser.add_argument("--steady", action="store_true",
                        help="Turn cat traps off. The seed is already fixed, "
                             "so this makes a run fully repeatable - for "
                             "comparing harness changes. NOT for a release: "
                             "cat traps are real and the gate must face them.")
    parser.add_argument("--only-arrow", action="store_true",
                        help="steps 1-5 only: install, pick the seed, run the "
                             "arrow session, report its two checks, stop.")
    parser.add_argument("--quick", action="store_true",
                        help="no arrow session, ability locks off, cat traps "
                             "off - for iterating. Still fifteen puzzles, "
                             "like the full run. The full run is the gate.")
    parser.add_argument("--dlc", action="store_true",
                        help="draw every puzzle from the two DLCs. Needs "
                             "both installed. Run this AS WELL AS the "
                             "ordinary gate, never instead of it - the "
                             "ordinary one is the regression run.")
    args = parser.parse_args()
    assets = os.path.join(REPO, args.assets)

    global QUICK, STEADY, DLC, ONLY_ARROW
    if args.only_arrow:
        if args.quick:
            print("--only-arrow needs the arrow session, which --quick skips",
                  flush=True)
            return 2
        ONLY_ARROW = True
        print("ONLY ARROW: steps 1-5, then the arrow session's two verdicts.",
              flush=True)
    if args.dlc:
        DLC = True
        print("DLC MODE: every puzzle drawn from Cupboards and Drawers "
              "or Seeing Stars. Both must be installed.", flush=True)
        # WHAT THIS RUN DOES NOT COVER, said out loud in the report.
        #
        # The yaml used to exclude container locations from carrying
        # progression, because the harness solves by setting a controller's
        # solved flag and a drawer is an interaction rather than an
        # arrangement. Since 2026-09-23 the drawer and cupboard controllers
        # are notALocation (solved at load, or never), so the list is empty.
        # The report stays for one that comes back: a green DLC gate reads
        # as "Cupboards and Drawers works" unless it says what was not asked.
        excluded = sorted(CONTAINER_EXCLUDES)
        if excluded:
            print(f"DLC MODE: {len(excluded)} container location(s) excluded "
                  f"from progression - the harness cannot pull a drawer open, "
                  f"so these are covered by droha's hand tests and NOT by this "
                  f"run. See docs/manual-container-test.md", flush=True)
        for name in excluded:
            print(f"    excluded: {name}", flush=True)
    if args.steady:
        STEADY = True
        print("STEADY MODE: cat traps off. The seed walk is deterministic "
              f"(from {GATE_SEED}), so this run is repeatable and two runs can "
              "be compared. Use it to test a harness change; run WITHOUT it "
              "before a release, because cat traps are real.", flush=True)
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
    # --fresh: also refuse a zip older than the source it was built from.
    #
    # --expect compares VERSIONS, and on 2026-09-20 the version had not
    # changed - release-test/ held a 0.4.0 zip that agreed with the
    # checkout perfectly while AbilityLocks.cs, Checks.cs and Plugin.cs
    # had all moved past it. Two dozen DLC gate runs measured a mod
    # containing neither the reachability gate nor the ability-lock
    # register hook, which were the two things being tested.
    argv += ["--fresh"]
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
    # PLAY IT ON PAPER FIRST, and only a seed that clears. generate() runs
    # the harness's own scheduler over each seed's spoiler and walks on from
    # a seed it cannot clear, saying why - in seconds, not twenty rounds.
    out_dir, seed_zip, plan = generate()
    say(4, "planned order: " + " -> ".join(plan["order"]))
    say(4, "planned visits: " + " -> ".join(
        v["level"] + (" (Skip)" if v["skip"] else "")
        + (" (cat trap resets it)" if v["reset"] else "")
        for v in plan["visits"]))
    need, have = preflight_skips(out_dir, seed_zip, plan)
    say(4, f"skips: {need} level(s) only a Skip can finish, {have} in the seed")
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
            if ONLY_ARROW:
                arrow_results = [
                    ("the pause menu Exit leaves the level",
                     "harness: exit check redirect=True closed=True" in arrow_text),
                    ("the next-level arrow opens the run's next puzzle",
                     got is not None and got == expected)]
                print(flush=True)
                for line in check_lines(arrow_results):
                    print(line, flush=True)
                report_pauses(arrow_text)
                return 0 if all(ok for _, ok in arrow_results) else 1
            # play() deletes the log when it launches, so keep this one now.
            keep_log("arrow")

        say(5, "launching a clean game and playing the run")
        (beaten, credits, transcript, open_slots, text, spent,
         surprises) = play(log, plan, arrow_text)
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

        # HOW OFTEN THE RUN ACTUALLY MET A LOCKED PUZZLE. Reported rather than
        # asserted, because a run can legitimately never meet one - it depends
        # what the draw put in the opening. What would be worth alarm is the
        # opposite of a number: before this existed the harness forced its way
        # through every gate in the run and the count was invisible, so a mod
        # that stopped locking anything at all would have read exactly the same
        # as one that locked correctly.
        refusals = whole.count(GATED_MARK)
        say(6, f"ability gates met and refused: {refusals}"
               + ("" if refusals else
                  " (this run never opened a puzzle it could not play)"))

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
                    why = "" if level_id in UNFORCEABLE else " UNEXPECTED"
                print(f"         {level_id}{why}{note}", flush=True)

        # 1. Only levels already known to need one. A NEW name here is the
        #    signal worth having: the harness could force that level last
        #    release and cannot now, which points at solve routing or at the
        #    controller table, not at the level.
        # REFUSED, not spent. play() no longer buys a Skip for a level
        # that is not on the allowlist - it records the level here and
        # says so at the point it happens. A Skip that was actually
        # spent outside the allowlist would still show up in `spent`,
        # so both are checked and neither can hide the other.
        bought = [l for l, _, reason in spent
                  if reason == "unforceable" and l not in UNFORCEABLE]
        for level_id, was_gated in surprises:
            say(2, f"  SURPRISE Skip refused: {level_id}"
                   + (" - STILL ABILITY-GATED" if was_gated else ""))
        results.append(("a Skip was spent only where one is known to be "
                        "needed", not surprises and not bought))
        results.append(("every Skip landed on the level it was bought for",
                        WRONG_SKIP_MARK not in transcript))
        results.append(("every Skip did something (the game skipped or the "
                        "mod released the slot)",
                        NOT_USED_MARK not in transcript))
        results.append(("the run followed its paper plan",
                        OFF_PLAN_MARK not in transcript))

        # 2. And none of them was still waiting on an ability. This is the
        #    check with teeth. An ability-gated level reaches the skip path
        #    looking exactly like an unfinishable one - every controller the
        #    player can reach is solved - so without this, a mod that wrongly
        #    withheld an ability would be PAPERED OVER by the Skip and the run
        #    would pass. The harness must never buy its way past a gating bug.
        # REFUSED AND SPENT, both. play() now refuses a Skip on a gated
        # level rather than spending it, so `spent` alone would go green
        # the moment the refusal landed - the fix quietly deleting the
        # assertion that motivated it. A level that reached the skip
        # path while gated is a defect either way: solve_level should
        # have returned GATED_MARK and never offered it.
        # A GATED SKIP THE HARNESS COULD HAVE AVOIDED. One it could
        # NOT - nothing playable anywhere and every candidate gated,
        # with the missing ability behind the very slot being
        # skipped - is reported rather than failed, because the only
        # alternative there is to deadlock.
        papered = [l for l, gated, reason in spent
                   if gated and reason != "last-resort"]
        for level_id, gated, reason in spent:
            if gated and reason == "last-resort":
                say(2, f"  a Skip went to {level_id} while it was still "
                       f"gated because NOTHING was playable and every "
                       f"candidate was gated - the only alternative was "
                       f"to deadlock")
        refused_gated = [l for l, gated in surprises if gated]
        for level_id in refused_gated:
            say(2, f"  a Skip was REFUSED on {level_id} because the mod "
                   f"was still gating it - solve_level should have "
                   f"recognised that and never offered the level")
        results.append(("no Skip covered for a level the mod was still "
                        "gating", not papered and not refused_gated))

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
    for line in check_lines(results):
        print(line, flush=True)
    pauses = report_pauses(whole)
    print(f"Done: {passed}/{len(results)} checks passed, "
          f"{len(beaten)} puzzle(s) beaten"
          + (f", {pauses} pause warning(s)" if pauses else ""), flush=True)
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
    if [l for l, _ in sample if l not in UNFORCEABLE]:
        sys.exit("self-test: the known-unforceable levels are not allowlisted")
    if [l for l, gated in sample if gated]:
        sys.exit("self-test: an ungated skip was read as gated")
    bad = [("TupperwareTower", True), ("Pasta", False)]
    if not [l for l, _ in bad if l not in UNFORCEABLE]:
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


def keep_log(tag=None):
    """Copy the game log to KEPT_LOGS as e2e-<stamp>[-<tag>].log.

    Returns the copy's path, or None when there was nothing to copy. Never
    raises: it runs in a finally and between sessions.
    """
    name = time.strftime("e2e-%Y%m%d-%H%M%S") + (f"-{tag}" if tag else "")
    try:
        os.makedirs(KEPT_LOGS, exist_ok=True)
        kept = os.path.join(KEPT_LOGS, name + ".log")
        shutil.copyfile(LOG, kept)
        print(f"game log kept at {os.path.relpath(kept, REPO)}", flush=True)
    except Exception as e:
        print(f"could not keep the game log: {e}", flush=True)
        return None
    return kept


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
        keep_log()

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
