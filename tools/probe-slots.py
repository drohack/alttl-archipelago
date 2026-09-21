"""Can the harness handle each level of a seed, one at a time?

WHY THIS EXISTS. The release gate answers two questions at once - can the
harness solve these levels, and does it choose them in a sensible order -
and it takes fifteen minutes to answer either. On 2026-09-20 that cost
about two dozen runs, because every scheduling change meant re-proving the
solving as well.

They are separable:

  - CHOOSING is arithmetic over sets. tools/test_scheduler.py covers it
    offline in milliseconds, with tools/mutate-scheduler.py to keep the
    tests honest.
  - SOLVING needs the game, but it needs it ONE LEVEL AT A TIME. This
    walks the seed's slots, gives each a clean attempt, and reports what
    happened. No rounds, no revisits, no Skip economy.

Run this after a harness change; run the full gate only when every slot
behaves and you want to test the two halves TOGETHER.

Each slot gets a fresh session, because a level booted over another one
inherits its leftovers - droha saw two levels stacked twice in one day.
That costs about a minute a slot and buys results that mean something.

    py -3.13 tools/probe-slots.py [--dlc] [--slots 0,3,5]
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import (Environment, close_game, ensure_no_steam_relaunch)

#: WHICH SEED. --dlc appeared in this file's usage line from the day it
#: was written and was parsed NOWHERE: passing it probed whatever seed
#: happened to be sitting in out-e2e, which for most of 2026-09-20 was
#: the base-game one. Every "the DLC slots are fine" reading taken
#: through this probe was a reading of the wrong seed.
#:
#: tools/make-seed.py writes out-base and out-dlc without needing the
#: game, so the two can exist side by side and the probe can say which
#: it used.
SEED_DIRS = {True: "out-dlc", False: "out-e2e"}
DLC = "--dlc" in sys.argv
OUT = os.path.join(e2e.REPO, "testserver",
                   os.environ.get("ALTTL_SEED_DIR_NAME", SEED_DIRS[DLC]))
CMDS = os.path.join(e2e.REPO, "testserver", "e2e-cmds.txt")
SERVER_LOG = os.path.join(e2e.REPO, "testserver", "probe-slots-server.log")

WANT = None
for i, a in enumerate(sys.argv):
    if a == "--slots" and i + 1 < len(sys.argv):
        WANT = {int(x) for x in sys.argv[i + 1].split(",")}

#: --no-wipe keeps the randomizer save, so a second run meets levels it
#: already solved in the first.
#:
#: THE WIPE IS WHY THIS PROBE CANNOT SEE THE GATE'S BUG. Every run here
#: starts from nothing, so every level is met fresh - but the gate plays
#: TWO sessions, and the first one (the arrow check) calls solve_level
#: and really does solve a level. On 2026-09-21 it solved DLC1 Clock
#: Cupboard, and the main run then reported "solved, but the mod banked
#: no Beaten token" on its first visit and spent a Skip on its second.
#:
#: Run once to build the state, then again with this flag to meet it.
NO_WIPE = "--no-wipe" in sys.argv


def wipe():
    """Forget every check, on the server AND in the mod's own save.

    WITHOUT THIS THE PROBE MEASURES NOTHING. An .apsave beside the seed is
    MultiServer's record of what has been checked and sent; reuse it and
    the run comes up already complete, so a level solves, LevelComplete
    fires and the mod files nothing - which this then reports as "NOTHING"
    and blames on the harness. Seen exactly that on the first run of this
    tool: DLC2 Books Stacked solved cleanly and scored NOTHING.
    """
    gone = 0
    for name in os.listdir(OUT):
        if name.endswith(".apsave"):
            os.remove(os.path.join(OUT, name))
            gone += 1
    for name in os.listdir(e2e.SAVE_DIR):
        if name.startswith("save_ap_") or name == "alttl-last-session.json":
            os.remove(os.path.join(e2e.SAVE_DIR, name))
            gone += 1
    return gone


def serve(seed):
    if e2e.port_open():
        return
    open(CMDS, "w", encoding="utf-8").close()
    log_file = open(SERVER_LOG, "w", encoding="utf-8")
    subprocess.Popen(
        f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
        f'--port {e2e.PORT} "{os.path.join(OUT, seed)}"',
        shell=True, cwd=e2e.AP, stdout=log_file, stderr=subprocess.STDOUT)
    for _ in range(40):
        if e2e.port_open():
            break
        time.sleep(1)
    if not e2e.port_open():
        sys.exit("FAIL: the server never bound the port")


def stop_server():
    """Stop the MultiServer this probe started.

    It used to leave one running. The next thing to want port 38281 was
    the release gate, which correctly refuses to start rather than
    silently test against a leftover server holding a different seed -
    so the leak cost a gate run to diagnose rather than corrupting one.
    Tidy up after yourself and the refusal never has to fire.
    """
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Get-CimInstance Win32_Process -Filter \"Name like "
                    "'%python%'\" | Where-Object { $_.CommandLine -like "
                    "'*MultiServer*' } | ForEach-Object { Stop-Process "
                    "-Id $_.ProcessId -Force }"],
                   capture_output=True, text=True)
    for _ in range(10):
        if not e2e.port_open():
            return True
        time.sleep(1)
    return not e2e.port_open()


def gated(plan, where, slot):
    """Does this level need an ability a run holding NOTHING cannot have?

    A probe session collects no items, so a level whose every location is
    ability-gated must come back empty - that is the mod working, not the
    harness failing. Without this the probe reports DLC2 Pizza (behind
    Distributing) as a problem on every single run, and a report that
    always contains a false alarm gets skimmed.
    """
    return not e2e.slot_has_work(slot, plan, where, set(), set())


def attempt(log, index, plan, where, slot):
    """Boot one level, try to solve it, and say what the outcome MEANS.

    The vocabulary matters more than it looks. The first version had one
    bucket for "nothing happened", which conflated a level that is
    correctly gated with one that should have worked and did not - and
    then flagged both for attention.
    """
    opened, out = e2e.boot_level(log, index)
    if not opened:
        return "DID NOT OPEN", out

    done, chunk = e2e.solve_level(log)
    chunk += log.wait(["beaten:", "check:", "credits:"], 10, 6, "the check")

    if done and "beaten:" in chunk:
        return "beaten", chunk
    if "cannot be force-solved" in chunk or "skip:" in chunk:
        # Expected for a phased level; the harness cannot finish those and
        # the gate spends a Skip. Not a fault.
        return "unforceable (expected)", chunk
    if "check:" in chunk:
        return "partial", chunk
    if gated(plan, where, slot):
        # NOT A PASS. The mod correctly refused to let the harness touch
        # the level, so the gating works - and NOTHING WAS LEARNED about
        # whether the level can be solved, because it was never tried.
        #
        # On 2026-09-21 that read as success for three of eight DLC
        # slots. DLC2 Pizza came back "gated (expected)", and when the
        # release gate later reached it holding Distributing it could
        # not be force-solved at all: one controller, four solutions,
        # and setting the controller's flag completes nothing. The
        # probe had reported the slot fine without ever attempting it.
        #
        # Generate a locks-off seed to measure forceability:
        #   py -3.13 tools/make-seed.py --dlc --quick --tag dlc-open
        #   ALTTL_SEED_DIR_NAME=out-dlc-open py -3.13 tools/probe-slots.py --dlc
        return "gated, SOLVING UNTESTED", chunk
    return "NOTHING", chunk


def main():
    seeds = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not seeds:
        sys.exit(f"no seed in {os.path.relpath(OUT, e2e.REPO)} - make one "
                 f"with tools/make-seed.py{' --dlc' if DLC else ''}")
    seed = seeds[0]
    plan = e2e.read_plan(OUT, seed)
    slots = plan["slots"]
    where = e2e.locations_for_slots(plan)

    todo = [(i, idx, name) for i, (idx, name) in enumerate(slots)
            if WANT is None or i in WANT]
    # SAY WHICH SEED. The whole reason --dlc silently did nothing for a
    # day is that nothing printed what was being probed.
    print(f"-- {len(todo)} slot(s) from {seed} "
          f"({os.path.relpath(OUT, e2e.REPO)}, "
          f"{'DLC' if DLC else 'base'}) --", flush=True)
    for i, idx, name in todo:
        print(f"   slot {i}: {name} (level {idx})", flush=True)

    # Every slot gets a run that has collected nothing, so each verdict is
    # about that level alone and not about what an earlier one banked.
    close_game()
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Get-CimInstance Win32_Process -Filter \"Name like "
                    "'%python%'\" | Where-Object { $_.CommandLine -like "
                    "'*MultiServer*' } | ForEach-Object { Stop-Process "
                    "-Id $_.ProcessId -Force }"],
                   capture_output=True, text=True)
    time.sleep(2)
    if NO_WIPE:
        print("KEEPING the saved progress - levels solved by an earlier "
              "run will be met already solved, which is the gate's "
              "second-session condition", flush=True)
    else:
        print(f"cleared {wipe()} saved-progress file(s)", flush=True)

    serve(seed)

    verdicts = []
    for n, (slot, index, name) in enumerate(todo, 1):
        close_game()
        log = e2e.Log()
        log.before_launch()
        ensure_no_steam_relaunch()
        subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
        if "connected. " not in log.wait(["connected. "], 150, 1, "the connection"):
            print(f"[{n}/{len(todo)} slot {slot} {name}] FAILED TO CONNECT",
                  flush=True)
            verdicts.append((slot, name, "NO CONNECTION"))
            continue
        time.sleep(3.0)

        try:
            verdict, _out = attempt(log, index, plan, where, slot)
        except Exception as e:
            verdict = f"THREW: {e}"
        print(f"[{n}/{len(todo)} slot {slot} {name}] {verdict}", flush=True)
        verdicts.append((slot, name, verdict))

    close_game()
    print(f"server stopped: {stop_server()}", flush=True)
    print("", flush=True)
    # Only the unexplained ones. "gated" and "unforceable" are the harness
    # and the mod behaving correctly, and burying them in a failure count
    # is how a report stops being read.
    bad = [v for v in verdicts if v[2] in ("NOTHING", "DID NOT OPEN",
                                           "NO CONNECTION")
           or v[2].startswith("THREW")]
    for slot, name, verdict in verdicts:
        print(f"   slot {slot:>2} {name:34s} {verdict}", flush=True)

    # SAY WHAT WAS NOT MEASURED. A gated slot proves the gate and
    # nothing else; counting it as a pass is how three of eight DLC
    # slots were reported fine without ever being attempted.
    untested = [v for v in verdicts if v[2].startswith("gated")]
    if untested:
        print(f"\n{len(untested)} slot(s) were never attempted - gated, so "
              f"their SOLVING is unmeasured:", flush=True)
        for slot, name, _v in untested:
            print(f"   slot {slot:>2} {name}", flush=True)
        print("   measure them with a locks-off seed:\n"
              "     py -3.13 tools/make-seed.py --dlc --quick --tag dlc-open\n"
              "     ALTTL_SEED_DIR_NAME=out-dlc-open py -3.13 "
              "tools/probe-slots.py --dlc", flush=True)

    print(f"Done: {len(verdicts)} slot(s), {len(bad)} needing attention, "
          f"{len(untested)} never attempted", flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    # THE PLAYER'S CONFIG GOES BACK. This probe wrote Host=localhost,
    # AutoConnect=true and a fixed SlotName straight into the config the
    # player uses and never restored any of it - the exact failure
    # harness_env was written for, where cw4 left a rig's settings behind
    # and a real session auto-connected to a dead port while the mod
    # looked broken.
    #
    # configure() must run BEFORE the game launches: the plugin reads its
    # config once at startup.
    with Environment("probe-slots") as _env:
        _env.configure(Host="localhost", Port=str(e2e.PORT),
                       SlotName=e2e.SLOT, AutoConnect="true")
        try:
            sys.exit(main())
        finally:
            # Also on the early exits and on a throw. main() stops the
            # server on its normal path; this is the backstop, because
            # the failure mode is invisible until the NEXT thing wants
            # the port.
            stop_server()
