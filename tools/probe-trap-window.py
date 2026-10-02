"""Measure when an arrived Cat Trap goes off: a settled puzzle, a level load,
a real finish, the retry panel, and the credits.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-trap-window.py

Prints one line per step and a verdict per case (PASS/FAIL against what the
mod should do), ends with a summary line, and writes every trap line to
testserver/logs/trap-window.txt. About 5 minutes. The game is closed first and
at the end; the player's files are snapshotted and put back (harness_env).

THE SEED IS ITS OWN. Base game, 10 puzzles, generators only, every ability
and the Credits item held from the start, levels_to_beat 1 and no traps in
the pool, so the probe decides when each trap lands: a straight-on
generator's finish makes the goal, and the credits are then playable. The
generation seed is searched until the opening holds a one-solution generator
(it moves on without showing the retry panel; "straight-on" below) and a
two-solution one (it stops on the retry panel).

THE CASES
- A: a trap in a settled puzzle springs.
- B: a trap sent just before a level reload is HELD through the load and
  springs on the far side (the 0.3.1 freeze guard).
- C: a REAL finish (`solve:`, which raises the game's own event; `complete`
  never gave a real post-level state) of a straight-on generator, a trap sent
  the moment LevelComplete is logged. It must miss: in the second player's
  0.4.2 playtest such traps were held through the exit, came out after the
  3 s grace, and reset the finished puzzle.
- D: a trap sent 6 s after a finish that left the retry panel up. It must
  miss: nothing is played until a restart launches the puzzle again.
- F: a trap on each level with its own cat that takes things (CatGrab:
  Stamps, Place Setting, Shells, MerryMess_Crackers). Every piece is first
  shoved off its spot (DevTools `shove`, the player's work stood in for).
  The game's cat must reach in, the reset must follow once its paw has gone,
  and the layout after it must be the OPENING layout (DevTools `layout:`,
  parent, placed flag and position per piece): not the shove, not whatever
  the paw knocked about. Unity's Player.log must stay free of exceptions for
  8 s after (Place Setting's cat comes back every 5 s on its own). Shells,
  not a slot of this run, launches on its fixed leaf layout, whose cat never
  comes: ours then, and the same reset. Screens per level, shoved / paw /
  after: %TEMP%/claude-trapcat-<index>-<stage>.png; delete after reading.
- E: a trap sent while the credits play. It must miss: the goal's release
  delivered a run's own traps there and reset the credits (the second
  player's 0.4.3 ending, which then threw). The camera repaints during the
  credits are recorded too; with the fix there should be none.
"""
import importlib.util
import os
import re
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import harness_env                                         # noqa: E402
import release_e2e as e2e                                  # noqa: E402
from harness_env import Environment, close_game            # noqa: E402

_spec = importlib.util.spec_from_file_location(
    "handtest_level", os.path.join(TOOLS, "handtest-level.py"))
handtest = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(handtest)

ROOT = e2e.REPO
# A run artifact, kept out of the tree's docs: the verdict lines above are
# what a run is judged by, and docs/history/trap-window.md is the 2026-09-13
# record of the first version.
OUT = os.path.join(ROOT, "testserver", "logs", "trap-window.txt")
YAML_DIR = os.path.join(ROOT, "testserver", "yaml-trapwindow")
OUT_DIR = os.path.join(ROOT, "testserver", "out-trapwindow")
SERVER_LOG = os.path.join(ROOT, "testserver", "logs", "trapwindow-server.log")

TRAP = "Cat Trap"
BACKGROUND = "Background Change Trap"
CREDITS_INDEX = 84

#: One solution: they move on without the panel, by the panel's route since
#: 2026-10-01 (Core AfterPuzzleRoute; the game's own straight-on before).
ONE_SOLUTION_GENERATORS = {997, 998, 996, 12, 70, 58, 73, 69, 62, 75, 1000, 28}
#: Two solutions: the retry panel after the first. Not Books (Randomized),
#: whose controllers change with the seed.
TWO_SOLUTION_GENERATORS = {999, 46, 26}

ABILITIES = ["Swapping", "Stacking", "Ordering", "Gadgets", "Rotating", "Grids",
             "Tidying", "Containers", "Drawer", "Sticking", "Symmetry", "Jigsaw"]

RESOLVED = ["cat(s) reset the puzzle", "cat(s) found nothing", "cat(s) arrived"]

#: Levels with a CatGrab, the cat the trap sends in (Traps.StartLevelCat).
CAT_GRAB_LEVELS = {9: "Stamps", 32: "Place Setting", 62: "Shells", 1016: "MerryMess_Crackers"}

#: A generator: launched seeded it can draw its leaf layout, where its cat
#: never comes (2026-09-30), so ours going instead passes there too.
CAT_MAY_NOT_COME = {62}

#: Unity's own log: the game's exceptions land here, never in LogOutput.log.
PLAYER_LOG = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                          "maxinferno", "A Little To The Left", "Player.log")


def player_log_size():
    try:
        return os.path.getsize(PLAYER_LOG)
    except OSError:
        return 0


def layout(log, tag):
    """DevTools `layout:<tag>`, read back as {(idx, name): [parent, world,
    local, rot, placed, active]} and the file deleted."""
    path = os.path.join(harness_env.GAME, "BepInEx", f"alttl-layout-{tag}.tsv")
    if os.path.exists(path):
        os.remove(path)
    e2e.dev(f"layout:{tag}", settle=0.5)
    wait(log, [f"for '{tag}'"], 15, f"the {tag} layout")
    rows = {}
    with open(path, encoding="utf-8") as fh:
        next(fh, None)
        for line in fh:
            cols = line.rstrip("\r\n").split("\t")
            if len(cols) >= 8:
                rows[(cols[0], cols[1])] = cols[2:8]
    os.remove(path)
    return rows


def settled_layout(log, tag, seconds=12.0):
    """The layout once it stops changing: read every second until two readings
    agree. Shells' leaves are still flying into their rows 2.5 s after a reset,
    which read as 5 pieces not put back (2026-09-30; droha saw them back)."""
    last = layout(log, f"{tag}-0")
    end = time.time() + seconds
    n = 0
    while time.time() < end:
        time.sleep(1.0)
        n += 1
        now = layout(log, f"{tag}-{n}")
        if not moved(last, now):
            return now
        last = now
    say(f"the {tag} layout was still moving after {seconds:g}s")
    return last


def _xyz(text):
    return [float(v) for v in re.findall(r"-?\d+(?:\.\d+)?", text)[:3]]


def moved(a, b, tol=0.05):
    """Pieces of layout `a` that are not the same in `b`: another parent,
    placed or active flag, a turn of more than a degree, a world position more
    than `tol` away, or gone."""
    out = []
    for key, ra in a.items():
        rb = b.get(key)
        if rb is None:
            out.append(f"{key[1]} gone")
            continue
        far = max((abs(x - y) for x, y in zip(_xyz(ra[1]), _xyz(rb[1]))), default=0.0)
        turn = abs(float(ra[3] or 0) - float(rb[3] or 0)) % 360
        turn = min(turn, 360 - turn)
        if ra[0] != rb[0] or ra[4] != rb[4] or ra[5] != rb[5] or far > tol or turn > 1.0:
            out.append(f"{key[1]} ({far:.2f} away)" if far > tol else f"{key[1]}")
    return out


def screen(index, stage):
    """A screenshot of the level at this stage; read and delete after the run."""
    path = os.path.join(os.environ["TEMP"], f"claude-trapcat-{index}-{stage}.png")
    e2e.dev(f"shot:{path}", settle=0.3)
    return path


def player_log_since(offset):
    try:
        with open(PLAYER_LOG, "rb") as fh:
            fh.seek(offset)
            return fh.read().decode("utf-8", "replace")
    except OSError:
        return ""


#: Cases A to D, one F per CatGrab level, then E.
TOTAL = 5 + 4

#: The case running now, as every line's prefix: `[6/9 F Place Setting]`.
CASE = "[setup]"


def start(n, name):
    global CASE
    CASE = f"[{n}/{TOTAL} {name}]"


def say(what):
    print(f"{CASE} {what}", flush=True)


def yaml_text():
    held = "\n".join(f"    {a}: 1" for a in ABILITIES + ["Credits"])
    return f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 10
  pack_size: 5
  generator_weight: 100
  archive_weight: 0
  base_weight: 0
  mechanic_coverage: 0
  levels_to_beat: 1
  ability_locks: true
  starting_abilities: 0
  cat_trap_chance: 0
  background_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  start_inventory:
{held}
"""


def generate():
    """A seed whose opening holds one of each generator shape, and the two."""
    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "t.yaml"), "w", encoding="utf-8") as fh:
        fh.write(yaml_text())

    for attempt in range(40):
        for name in os.listdir(OUT_DIR):
            os.remove(os.path.join(OUT_DIR, name))
        r = subprocess.run(
            [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
             "--outputpath", OUT_DIR, "--seed", str(20260929 + attempt)],
            cwd=e2e.AP, capture_output=True, text=True,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
        if r.returncode:
            print(r.stdout[-1500:], flush=True)
            raise SystemExit("FAIL: generation failed")
        seed = os.path.join(OUT_DIR, next(f for f in os.listdir(OUT_DIR) if f.endswith(".zip")))
        sd = handtest.slot_data(seed)
        opening = sd.get("slots", [])[:(sd.get("pack_boundaries") or [5])[0]]
        indices = [s.get("levelIndex") for s in opening if isinstance(s, dict)]
        one = next((i for i in indices if i in ONE_SOLUTION_GENERATORS), None)
        two = next((i for i in indices if i in TWO_SOLUTION_GENERATORS), None)
        say(f"seed {20260929 + attempt}: opening {indices}")
        if one is not None and two is not None:
            return seed, one, two
    raise SystemExit("FAIL: 40 seeds and no opening with both generator shapes")


def traps(text):
    """Every trap line in `text`, once each, in order."""
    seen = []
    for m in re.finditer(r"trap: [^\r\n]*", text):
        if m.group(0) not in seen:
            seen.append(m.group(0))
    return seen


def lines(text, pattern):
    return [m.group(0).split("] ", 1)[-1]
            for m in re.finditer(r"[^\r\n]*(" + pattern + r")[^\r\n]*", text)]


#: The launched game, so every wait can say at once that it died.
GAME = None


def wait(log, needles, seconds, what):
    """The log until one of `needles` lands, saying every 8 s what it waits
    for under the case's own prefix; stops the run if the game has died."""
    got, end, last = "", time.time() + seconds, time.time()
    while time.time() < end:
        if GAME is not None and GAME.poll() is not None:
            raise SystemExit(f"{CASE} FAIL: the game exited (code {GAME.returncode}) "
                             f"while waiting for {what}")
        got += log.new()
        if any(n in got for n in needles):
            return got
        if time.time() - last > 8:
            last = time.time()
            say(f"waiting for {what} ({int(end - time.time())}s left)")
        time.sleep(0.1)
    say(f"gave up waiting for {what}")
    return got


def in_a_puzzle(log, seconds=45):
    """Block until a real, playable level is up, and say which.

    Asked with `controllers`, because "N registered on <level>" is only
    printed when a level is genuinely interactive.
    """
    end = time.time() + seconds
    while time.time() < end:
        log.new()
        e2e.dev("controllers", settle=1.2)
        text = wait(log, ["controllers: "], 10, "the controller list")
        for line in text.splitlines():
            if "registered on " in line:
                which = line.split("registered on ", 1)[1].split(" levelInstance")[0]
                say(f"in a puzzle: {which}")
                time.sleep(2.0)
                return which
        time.sleep(1.5)
    raise SystemExit("FAIL: never landed in a playable level")


def open_card(log, index):
    """The run's track, then the card for that level index."""
    e2e.dev("menu:title", settle=3.0)
    for _ in range(6):
        e2e.dev("menu:levels", settle=3.0)
        log.new()
        e2e.dev(f"clickcard:{index}", settle=1.0)
        text = wait(log, ["clickcard: returned", "clickcard: open menu"], 10, "the card")
        if "clickcard: returned" in text:
            return
        time.sleep(2.0)
    raise SystemExit(f"FAIL: could not click the card for level {index}")


def send(server, item):
    server.stdin.write(f"/send {e2e.SLOT} {item}\n")
    server.stdin.flush()


def verdict(text, want, forbid):
    """PASS when a trap line holds one of `want` and none holds `forbid`.
    The line printed names only the deciding trap line; every trap line is
    in the results file."""
    got = traps(text)
    wants = [want] if isinstance(want, str) else want
    hit = next((t for t in got for w in wants if w in t), None)
    bad = next((t for t in got if forbid in t), None)
    ok = hit is not None and bad is None
    why = bad or hit or (got[-1] if got else "no trap line")
    say(f"{'PASS' if ok else 'FAIL'}: {why[len('trap: '):] if why.startswith('trap: ') else why}")
    return ok


def main():
    global GAME
    if e2e.port_open():
        raise SystemExit(f"FAIL: something already listens on {e2e.PORT}; stop it first")

    close_game()
    say("generating the probe's seed")
    seed, one, two = generate()
    say(f"seed {os.path.basename(seed)}: straight-on generator {one}, panel generator {two}")

    findings, results = {}, {}
    server = subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(e2e.PORT), seed],
        cwd=e2e.AP, stdin=subprocess.PIPE, stdout=open(SERVER_LOG, "w"),
        stderr=subprocess.DEVNULL, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    try:
        for _ in range(60):
            if e2e.port_open():
                break
            time.sleep(1)
        with Environment("probe-trap-window"):
            e2e.write_config()
            e2e.write_devtools_config(mute=True)
            log = e2e.Log()
            log.before_launch()
            say(f"server on {e2e.PORT}, launching (muted)")
            GAME = e2e.launch_game()
            wait(log, ["connected. ", "Archipelago refused"], 180, "the connection")
            wait(log, ["toasts: overlay ready"], 60, "the title")
            time.sleep(3.0)
            e2e.dev("setres:1280x720", settle=3.0)
            say("connected")

            # --- A: a settled puzzle -------------------------------------
            start(1, "A settled puzzle")
            e2e.dev("menu:title", settle=3.0)
            e2e.dev("play", settle=9.0)
            in_a_puzzle(log)
            send(server, BACKGROUND)
            wait(log, [f"received item: {BACKGROUND}"], 30, "the background trap")
            time.sleep(2.0)
            log.new()
            send(server, TRAP)
            text = wait(log, RESOLVED, 40, "the trap to resolve")
            findings["A: trap in a settled puzzle"] = traps(text)
            results["A"] = verdict(text, "reset the puzzle", "arrived")

            # --- B: a trap into a level load -----------------------------
            start(2, "B into a load")
            time.sleep(4.0)
            log.new()
            e2e.dev("state", settle=1.0)
            got = wait(log, ["state: gameState="], 15, "the index")
            index = (re.findall(r"state: .*? index=(-?\d+)", got) or ["-1"])[-1]
            log.new()
            send(server, TRAP)
            time.sleep(0.15)
            e2e.dev(f"loadlevel:{index}")
            text = wait(log, RESOLVED, 45, "the trap to resolve")
            findings["B: trap into a load"] = traps(text)
            results["B"] = verdict(text, "reset the puzzle", "found nothing")

            # --- C: a real finish of a straight-on generator --------------
            start(3, "C straight-on finish")
            time.sleep(4.0)
            open_card(log, one)
            in_a_puzzle(log)
            log.new()
            e2e.dev("solve:0")
            text = wait(log, ["LevelComplete  id="], 30, "the finish")
            send(server, TRAP)
            say("finished a straight-on generator; sent a trap at its LevelComplete")
            text += wait(log, RESOLVED, 40, "the trap to resolve")
            time.sleep(10.0)
            text += log.new()
            findings["C: trap at a straight-on finish"] = traps(text)
            findings["C: where the run went"] = lines(
                text, r"navigation: |daily guard|checks: now playing|track: slot ")
            # A miss either way: on 2026-09-29 the pre-fix build found no level
            # here (the exit had torn it down); the playtest's resets did not
            # reproduce against a local server, so Core TrapTimingTests holds
            # the ordering that caused them.
            results["C"] = verdict(text, ["too late", "found nothing"], "reset the puzzle")

            # --- D: the retry panel, well after the grace ----------------
            start(4, "D on the panel")
            open_card(log, two)
            in_a_puzzle(log)
            log.new()
            e2e.dev("solve:0")
            text = wait(log, ["retry panel: slot"], 30, "the panel decision")
            time.sleep(6.0)
            text += log.new()
            send(server, TRAP)
            say("sat on the retry panel 6 s; sent a trap")
            text += wait(log, RESOLVED, 40, "the trap to resolve")
            findings["D: the panel's decision"] = lines(text, r"retry panel: ")
            findings["D: trap on the panel"] = traps(text)
            results["D"] = verdict(text, "too late", "reset the puzzle")

            # --- F: the level's own cat ----------------------------------
            run = {s.get("levelIndex") for s in handtest.slot_data(seed).get("slots", [])}
            for n, (index, name) in enumerate(CAT_GRAB_LEVELS.items(), 5):
                start(n, f"F {name}")
                if index in run:
                    open_card(log, index)      # a run slot loops a plain boot
                else:
                    e2e.dev("menu:title", settle=3.0)
                    e2e.dev(f"boot:{index}", settle=8.0)
                in_a_puzzle(log)

                # The player's work, stood in for: every piece shoved off its
                # spot. The trap must put the OPENING layout back, not leave
                # the shove, or whatever the paw knocked about.
                opening = settled_layout(log, f"cat{index}-open")
                e2e.dev("shove", settle=1.5)
                shoved = layout(log, f"cat{index}-shoved")
                shots = [screen(index, "1-shoved")]
                say(f"shoved {len(moved(opening, shoved))} of {len(opening)} piece(s); sending a trap")

                log.new()
                offset = player_log_size()
                send(server, TRAP)
                text = wait(log, ["own cat reaches in", "ours goes instead"] + RESOLVED,
                            40, "the level's cat")
                if "own cat reaches in" in text:
                    time.sleep(0.8)                  # the paw is furthest in at ~1.3 s
                    shots.append(screen(index, "2-paw"))
                ends = ["cat(s) reset the puzzle", "nothing to reset", "puzzle was not reset"]
                if not any(e in text for e in ends):   # ours resets at once
                    text += wait(log, ends, 20, "the reset")
                time.sleep(1.0)
                after_reset = settled_layout(log, f"cat{index}-after")
                shots.append(screen(index, "3-after"))
                time.sleep(5.0)
                text += log.new()

                errors = [l for l in player_log_since(offset).splitlines()
                          if re.search(r"Exception[:\s]", l)]
                took = moved(opening, shoved)
                left = moved(opening, after_reset)
                got = traps(text)
                findings[f"F: {name}'s own cat"] = (
                    got + [f"shoved: {len(took)} of {len(opening)} piece(s) moved",
                           f"after the reset, not at the opening layout: {left or 'none'}"]
                    + [f"Player.log: {l[:160]}" for l in errors[:5]]
                    + [f"screen: {s}" for s in shots])
                after = next((t for t in got if "reset the puzzle after the level's own cat" in t), None)
                theirs = after is not None and any("own cat reaches in" in t for t in got)
                ours = (index in CAT_MAY_NOT_COME
                        and any("ours goes instead" in t for t in got)
                        and any(t.endswith("cat(s) reset the puzzle") for t in got))
                ok = (theirs or ours) and took and not left and not errors
                if errors:
                    say(f"FAIL: {len(errors)} exception(s) in Player.log, first: {errors[0][:120]}")
                elif not took:
                    say("FAIL: the shove moved nothing, so the reset proves nothing")
                elif left:
                    say(f"FAIL: {len(left)} of {len(opening)} piece(s) not back at the opening "
                        f"layout: {', '.join(left[:4])}")
                elif theirs:
                    secs = (re.findall(r"\(([\d.]+)s", after) or ["?"])[0]
                    say(f"PASS: the game's cat reached in, the reset came {secs}s after, and all "
                        f"{len(took)} shoved piece(s) are back at the opening layout")
                elif ours:
                    say(f"PASS: this layout's cat never came; ours went, and all {len(took)} "
                        f"shoved piece(s) are back at the opening layout")
                else:
                    say(f"FAIL: {got[-1][len('trap: '):] if got else 'no trap line'}")
                results[f"F {name}"] = ok

            # --- E: the credits ------------------------------------------
            start(TOTAL, "E credits")
            e2e.dev("menu:title", settle=3.0)
            e2e.dev("menu:levels", settle=4.0)
            log.new()
            e2e.dev(f"clickcard:{CREDITS_INDEX}", settle=1.0)
            text = wait(log, ["credits: started"], 40, "the credits")
            time.sleep(3.0)
            text += log.new()
            send(server, TRAP)
            say("the credits are playing; sent a trap")
            text += wait(log, RESOLVED, 30, "the trap to resolve")
            time.sleep(6.0)
            text += log.new()
            findings["E: trap during the credits"] = traps(text)
            findings["E: camera repaints during the credits"] = lines(text, r"backgrounds: repainted")
            results["E"] = verdict(text, "during the credits", "reset the puzzle")

            # --- alive on the far side? ----------------------------------
            log.new()
            e2e.dev("state", settle=2.0)
            alive = wait(log, ["state: gameState="], 20, "a reply")
            findings["alive after"] = ["ALIVE" if "state: gameState=" in alive
                                       else "NO REPLY - the game is hung"]
    finally:
        try:
            server.stdin.write("/exit\n")
            server.stdin.flush()
            server.wait(timeout=10)
        except Exception:
            server.kill()
        close_game()

    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# When a Cat Trap goes off\n\n")
        f.write("Written by `tools/probe-trap-window.py`; its docstring says what "
                "each case is.\n\n```\n")
        for title, found in findings.items():
            f.write(f"== {title} ==\n")
            for line in found or ["(nothing recorded)"]:
                f.write(line + "\n")
            f.write("\n")
        f.write("```\n")

    passed = sum(results.values())
    print(f"Done: {passed} of {len(results)} cases pass "
          f"({', '.join(k + ('' if v else ' FAIL') for k, v in results.items())}), "
          f"written to {os.path.relpath(OUT, ROOT)}", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
