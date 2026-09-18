"""Where does a DLC puzzle send you when you finish it?

THE QUESTION droha ASKED, MORE THAN ONCE, and which nothing answered: after a
DLC level, do the Continue arrow, the post-level Level Select, the pause-menu
Level Select and a level-select card click all land on the RUN'S OWN TRACK and
on the CORRECT next level - and does the game never bounce back into the DLC's
own menu?

What was measured before this existed was narrower than that. DlcGuard.Tick was
shown to catch all fourteen entries into DLCLevels_GameState in a run and to
open the run's track, which proves it RECOVERS. It says nothing about where the
game goes next, and "recovers from the wrong screen" and "advances to the right
puzzle" are different claims.

WHY THE GAME GETS THIS WRONG WITHOUT HELP: the game routes a finished level by
asking what it was, and a DLC puzzle belongs to its DLC, so
LevelManager.GoToLevelSelectForLevel sends the player to the Seeing Stars menu
with the finished level still loaded behind it. The mod then paints the run's
cards onto a track it does not own and logs "track: card at position N but the
plan covers 0". That warning is the failure signature, and this probe requires
it to be absent.

TWO SESSIONS, and the split is not optional. The replay Next arrow poisons
everything after it - measured as a pair, a single press gave 2 of 8 beaten and
103 exceptions where no press gave 8 of 8 and none (release_e2e.py:1411). So
the arrow gets a throwaway game of its own and the menu routes get a clean one.

THREE WAYS THE FIRST VERSION OF THIS PROBE DROVE THE GAME INTO A BROKEN SCREEN,
all fixed here and all worth naming, because each is a way to write a harness
that reports nonsense rather than a failure:

  - It carried on after a level did not force-solve, so it asked for a
    post-level screen that did not exist. Levels that will not yield to a
    forced solve are normal; playable_card() looks for one that does.
  - It fired one clicktrack and assumed it landed. After a completion the game
    reports Levels_GameState while every LevelsTrack is still inactive, and a
    click then resolves the level name correctly and starts nothing - DevTools
    says "clicktrack: the track is not up yet", which is a retry signal.
    open_card() retries.
  - It drove the pause menu with no level running. The pause route now opens a
    card and deliberately does NOT solve it, because a pause menu needs a level
    in progress, not a finished one.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-dlc-nav.py 2>/dev/null

Generates its own Seeing Stars seed. Exits 0 only if every route lands on the
run's track, on a level the plan agrees with, with no DLC state entered and no
card-position warning.
"""
import glob
import os
import shutil
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game

import release_e2e as e2e

OUT = os.path.join(e2e.REPO, "testserver", "out-dlcnav")
YAML = os.path.join(e2e.REPO, "testserver", "yaml-dlcnav")
#: Where the stage frames land, for a human to look at afterwards.
SHOTS = os.path.join(e2e.REPO, "testserver", "logs", "dlcnav-frames")

#: Seeing Stars only, so every slot is a DLC puzzle and no route can pass by
#: landing on base-game content. Everything open from the start: this probe is
#: about navigation, and a locked slot would make "the arrow went nowhere" an
#: ambiguous result.
YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 8
  levels_to_beat: 8
  pack_size: 8
  guaranteed_open_slots: 8
  seeing_stars: true
  stars_weight: 100
  cupboards_and_drawers: false
  generator_weight: 0
  archive_weight: 0
  base_weight: 0
  archive_packs: []
  mechanic_coverage: 0
  ability_locks: false
  skip_count: 0
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""

#: The screen the run's own track lives in, and the one it must never be.
TRACK_STATE = "Levels_GameState"
DLC_STATE = "DLCLevels_GameState"

#: Track.cs logs this when it is asked about a card that is not in the plan -
#: the signature of the mod painting onto a DLC's level select.
WRONG_TRACK = "but the plan covers"


def generate():
    for d in (OUT, YAML):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(YAML, "s.yaml"), "w", newline="\n") as f:
        f.write(YML.format(slot=e2e.SLOT))

    for n in range(20):
        for f in glob.glob(os.path.join(OUT, "*")):
            os.remove(f)
        subprocess.run([sys.executable, "Generate.py",
                        "--player_files_path", YAML, "--outputpath", OUT,
                        "--seed", str(9100 + n)],
                       cwd=e2e.AP, capture_output=True, text=True)
        zips = glob.glob(os.path.join(OUT, "*.zip"))
        if zips:
            seed = os.path.basename(zips[0])
            plan = e2e.read_plan(OUT, seed)
            if all(name.startswith("DLC") for _i, name in plan["slots"]):
                return seed, plan
    return None, None


def state(log):
    """The live gameState, asked rather than assumed."""
    log.new()
    e2e.dev("state", 0.8)
    out = log.wait(["state: gameState"], 12, 1, "the game state")
    line = e2e.line_with(out, "state: gameState")
    if "gameState=" not in line:
        return "?", out
    return line.split("gameState=", 1)[1].split()[0].strip(), out


def start(label, seed):
    """Bring up a server and a game, connected."""
    server = subprocess.Popen(
        f'py -3.13 -u MultiServer.py --port {e2e.PORT} '
        f'"{os.path.join(OUT, seed)}"',
        shell=True, cwd=e2e.AP, stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(40):
        if e2e.port_open():
            break
        time.sleep(1)
    log = e2e.Log()
    log.before_launch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    text = log.wait(["connected. "], 150, 1, f"the {label} connection")
    if "connected. " not in text:
        return None, None, server
    # The title rebuilds after the connection lands, and a navigation issued
    # into the middle of that is undone when the rebuild finishes. Checked in
    # the text ALREADY READ first - on a fast connect the line arrives in the
    # same chunk, and waiting again then blocks for the full timeout on a line
    # that has gone past.
    if "toasts: overlay ready" not in text:
        log.wait(["toasts: overlay ready"], 60, 1, "the title to settle")
    time.sleep(3.0)

    # EVERY LAUNCH SAYS WHETHER ITS OWN FEATURES INSTALLED, and reading that
    # line is how the DlcGuard-was-never-patched bug should have been caught
    # on day one instead of from a screenshot.
    problem = e2e.patch_problem(e2e.whole_log())
    if problem:
        print(f"      WARNING, the mod did not fully install: {problem}",
              flush=True)
    return log, text, server


def stop(server):
    close_game()
    if server:
        subprocess.run(["powershell", "-NoProfile", "-Command",
                        "Get-NetTCPConnection -LocalPort 38281 -State Listen "
                        "-ErrorAction SilentlyContinue | ForEach-Object "
                        "{ Stop-Process -Id $_.OwningProcess -Force }"],
                       capture_output=True)


def shoot(name):
    """Photograph the screen at one stage of the run.

    THE WHOLE REASON THIS PROBE EXISTS IN THIS FORM. Every log assertion it
    makes passed while the game was visibly showing TWO LEVEL SELECTS on top
    of each other - the DLC's title and its "1/17 (6%)" header over the run's
    track. "state: gameState=Levels_GameState" was true and useless: the state
    was right and the screen was wrong, because switching state does not close
    a menu the game has already built. droha caught it from a screenshot after
    the automated checks had reported themselves green.

    So the stages are photographed and the frames are kept for a human to look
    at. A harness that can only read the log cannot see this class of bug at
    all.
    """
    os.makedirs(SHOTS, exist_ok=True)
    tmp = os.path.join(e2e.GAME, f"ap-nav-{name}.png")
    if os.path.exists(tmp):
        os.remove(tmp)

    e2e.dev(f"shot:{tmp}", 0.5)
    size = -1
    for _ in range(40):
        time.sleep(0.5)
        try:
            now = os.path.getsize(tmp)
        except OSError:
            continue
        if now > 0 and now == size:
            break
        size = now
    else:
        print(f"      (no frame captured for {name})", flush=True)
        return None

    dest = os.path.join(SHOTS, f"{name}.png")
    shutil.move(tmp, dest)
    print(f"      frame: {os.path.relpath(dest, e2e.REPO)} "
          f"({size // 1024} KB)", flush=True)
    return dest


def to_track(log):
    """Reach the run's track, leaving any running level the way a player does.

    NEVER FORCE menu:levels OUT OF A LEVEL, and this cost two runs to learn.
    `boot:` tears the previous level down itself; a forced state change does
    not. So forcing the level select while a puzzle is still loaded builds the
    track on top of a live level, and the result is a half-drawn screen where
    `state` cheerfully answers Levels_GameState, DevTools answers "already in
    Levels_GameState, nothing to do", and a click on a card RESOLVES ITS NAME
    AND STARTS NOTHING - "clicktrack: position 1 is DLC2 Leftovers" with no
    launch after it. No exception is thrown anywhere; it just sits there.

    A player leaves through the pause menu, so that is what this does.
    """
    for _ in range(8):
        now, _out = state(log)
        if now == TRACK_STATE:
            time.sleep(2.0)
            return True

        if "Gameplay" in now:
            # The player's own route out of a puzzle in progress.
            e2e.dev("pause", 2.0)
            e2e.dev("leave", 4.0)
        elif "Retry" in now or "Replay" in now:
            # A FINISHED level is still loaded behind its own end screen, so
            # this has the same problem as forcing it out of gameplay. The
            # post-level Level Select button is the route the game provides.
            e2e.dev("replayselect", 4.0)
        else:
            e2e.dev("menu:levels", 2.5)
        time.sleep(2.0)
    return False


def slots_entered(text):
    return [int(l.split("now playing slot", 1)[1].strip())
            for l in text.splitlines() if "now playing slot" in l]


def open_card(log, position):
    """Click one card and wait until a slot is actually entered."""
    for attempt in range(1, 6):
        if not to_track(log):
            time.sleep(2.0)
            continue
        log.new()
        e2e.dev(f"clicktrack:{position}", 6.0)
        out = log.wait(["now playing slot", "not up yet"], 20, 3,
                       f"card {position}")
        entered = slots_entered(out)
        if entered:
            return entered[-1], out
        print(f"      card {position}: the track was not up yet "
              f"(attempt {attempt}), waiting", flush=True)
        time.sleep(3.0)
    return -1, ""


def beat(log, what):
    """Force-solve the running level, with one retry."""
    text = ""
    for attempt in (1, 2):
        log.new()
        done, text = e2e.solve_level(log)
        if done:
            return True, text
        print(f"      {what}: attempt {attempt} did not complete"
              + (" (exhausted)" if e2e.EXHAUSTED_MARK in text else ""),
              flush=True)
    return False, text


def playable_card(log, by_slot, what):
    """The first card that both opens AND can be force-solved.

    Not every DLC puzzle yields to a forced solve - a phased level runs its own
    machine and simply never completes - and carrying on past one that did not
    finish is what sent the first version of this probe into the menus with no
    post-level screen to act on.
    """
    for position in range(min(4, len(by_slot))):
        slot, _out = open_card(log, position)
        if slot < 0:
            continue
        print(f"      card {position} opened slot {slot} "
              f"({by_slot.get(slot, ('?', '?'))[1]})", flush=True)
        done, _text = beat(log, what)
        if done:
            return slot, position
        print(f"      slot {slot} would not force-solve; trying the next card",
              flush=True)
    return -1, -1


def main():
    print("[1/6] generating a Seeing Stars seed", flush=True)
    seed, plan = generate()
    if seed is None:
        print("FAIL: no all-DLC seed generated", flush=True)
        return 1
    by_slot = {i: (idx, name) for i, (idx, name) in enumerate(plan["slots"])}
    print(f"      {seed}, {len(by_slot)} DLC slots", flush=True)

    for p in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(p)

    results = []
    transcript = []
    close_game()

    with Environment("dlc-nav-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        server = None
        try:
            print("[2/6] session one: the menu routes", flush=True)
            log, text, server = start("first", seed)
            if log is None:
                print("FAIL: never connected", flush=True)
                return 1
            transcript.append(text)

            print("[3/6] finishing a DLC puzzle launched from the track",
                  flush=True)
            slot, position = playable_card(log, by_slot, "the first puzzle")
            if slot < 0:
                print("FAIL: no DLC card both opened and force-solved, so "
                      "there is no post-level screen to test the routes on",
                      flush=True)
                return 1
            results.append(("a card click opens a slot in the plan",
                            slot in by_slot))
            shoot("1-after-finishing-a-dlc-puzzle")

            print("[4/6] post-level Level Select, then the pause menu",
                  flush=True)
            # WAIT FOR THE POST-LEVEL SCREEN TO EXIST FIRST. Pressing its
            # Level Select while the game is still in Gameplay_GameState does
            # nothing at all - measured, and it is what made this assertion
            # fail three runs running while the frame showed the completion
            # animation still playing. A solve does not put you on the end
            # screen immediately.
            for _ in range(10):
                now, _out = state(log)
                if "Retry" in now or "Replay" in now:
                    break
                time.sleep(2.0)
            print(f"      post-level screen: {now}", flush=True)

            log.new()
            e2e.dev("replayselect", 4.0)
            # POLLED, not asked once. The first version checked the state a
            # moment after pressing the button and photographed the completion
            # screen - cats, vases and the three post-level buttons - then
            # reported that the route had not reached the track. The transition
            # simply had not finished.
            now = "?"
            for _ in range(8):
                now, out = state(log)
                if now == TRACK_STATE:
                    break
                time.sleep(2.0)
            transcript.append(out)
            print(f"      post-level Level Select: gameState={now}", flush=True)
            shoot("2-post-level-level-select")
            results.append(("post-level Level Select reaches the run's track",
                            now == TRACK_STATE))

            # The pause route needs a RUNNING level, not a finished one, so
            # this opens one and stops there rather than solving it.
            #
            # OPENED WITH boot:, NOT A CARD CLICK, and the difference is the
            # teardown. The level just completed is still loaded behind the
            # track (state reports "level=present"), and a click on a card
            # then resolves its name and starts nothing - five attempts, all
            # "the track was not up yet". boot: tears the previous level down
            # itself, which is why release-testing.md calls it "the route that
            # works from anywhere". The card-click route is already covered by
            # the first assertion above; what is under test here is where the
            # PAUSE MENU goes, not how the level was launched.
            other = next((s for s in by_slot if s != slot), None)
            running = -1
            if other is not None:
                opened, out = e2e.boot_level(log, by_slot[other][0])
                transcript.append(out)
                if opened:
                    running = other
            if running < 0:
                print("      could not open a second card for the pause route",
                      flush=True)
                results.append(("the pause menu reaches the run's track",
                                False))
            else:
                e2e.dev("pause", 2.5)
                log.new()
                e2e.dev("leave", 4.0)
                now, out = state(log)
                transcript.append(out)
                print(f"      pause-menu Level Select: gameState={now}",
                      flush=True)
                shoot("3-pause-menu-level-select")
                results.append(("the pause menu reaches the run's track",
                                now == TRACK_STATE))
            stop(server)
            server = None

            print("[5/6] session two: the replay Next arrow, in a game of its "
                  "own", flush=True)
            # NEVER in the same session as the run above: one press of this
            # arrow has been measured to poison every launch after it.
            log, text, server = start("second", seed)
            if log is None:
                print("FAIL: the second session never connected", flush=True)
                return 1
            transcript.append(text)

            slot, _position = playable_card(log, by_slot, "the arrow's puzzle")
            if slot < 0:
                print("FAIL: no DLC card force-solved in the second session, "
                      "so the arrow has nothing to advance from", flush=True)
                return 1

            log.new()
            e2e.dev("next", 6.0)
            out = log.wait(["navigation: next ->", "now playing slot"], 25, 5,
                           "the arrow")
            time.sleep(2.5)
            out += log.new()
            transcript.append(out)

            decided = -1
            decided_level = -1
            for line in out.splitlines():
                if "navigation: next ->" in line:
                    tail = line.split("navigation: next ->", 1)[1]
                    decided = int(tail.split("slot", 1)[1].split("(")[0].strip())
                    decided_level = int(tail.split("level", 1)[1]
                                        .strip().rstrip(")").strip())
            print(f"      the arrow chose slot {decided} "
                  f"(level {decided_level})", flush=True)

            results.append(("the arrow chose a slot in the plan",
                            decided in by_slot))
            results.append(("the arrow's level matches that slot's level",
                            decided in by_slot
                            and by_slot[decided][0] == decided_level))
            results.append(("the arrow did not return to the puzzle just "
                            "finished", decided != slot))
            landed = slots_entered(out)
            results.append(("the arrow opened the slot it chose",
                            bool(landed) and landed[-1] == decided))
            now, out = state(log)
            transcript.append(out)
            print(f"      after the arrow: gameState={now}", flush=True)
            shoot("4-after-the-arrow")
        finally:
            stop(server)

    whole = "\n".join(transcript)
    results.append(("the mod never painted onto a DLC level select",
                    WRONG_TRACK not in whole))
    results.append(("no route ever entered the DLC state",
                    f"gameState={DLC_STATE}" not in whole))

    # THE SCREEN, not just the state. The state backstop firing means the DLC
    # menu was already BUILT and is still on screen underneath the run's track
    # - the two-level-selects-at-once bug, which every log assertion above
    # happily passes through. If the routing redirect is working, this line
    # never appears, because the DLC level select is never opened at all.
    results.append(("the DLC level select was never opened in the first place",
                    "dlc guard: opening the run's track" not in whole))


    print("[6/6] results", flush=True)
    for name, ok in results:
        print(f"  {'PASS' if ok else 'FAIL'}  {name}", flush=True)
    passed = sum(1 for _n, ok in results if ok)
    print(f"{'PASS' if passed == len(results) else 'FAIL'}: "
          f"{passed}/{len(results)} navigation checks", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
