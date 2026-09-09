"""Prove the release harness can get past a level it cannot force-solve.

Two minutes instead of the gate's fifteen, and it answers the one question the
gate takes fifteen minutes to reach.

WHY THIS EXISTS. `solve:` sets a controller's solved flag and dispatches the
event. That finishes most levels and does not finish a PHASED one: PawPrints
registers five controllers, all five solve, all five checks fire, and
PawPrintsPhaseLevel still never raises LevelComplete, because its phase machine
wants the real solve path. The mod is right to bank no Beaten token. The
harness simply cannot finish that puzzle, so it spends a Skip - which since
0.3.1 finishes the slot and counts toward the credits.

Phased campaign levels became drawable in 0.3.1, so a run that draws one used
to stall: nineteen rounds, 441 seconds, then stop one puzzle short and fail six
assertions that had nothing wrong with them.

WHY IT LOADS THE HARNESS INSTEAD OF COPYING IT. The bug being guarded against
WAS a copy that had drifted: play() searched the returned text for a message
that only ever went to stdout, so the skip path could not fire and nothing said
so. This calls e2e.solve_level and reads e2e.EXHAUSTED_MARK, so a future drift
fails here rather than fifteen minutes into a gate run.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-skip-path.py 2>/dev/null

Needs a seed in testserver/out-e2e (the gate leaves one) and the release
installed. Exits 0 only if the skip both spends and banks a token.
"""
import glob
import importlib.util
import os
import subprocess
import sys
import tempfile
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment

spec = importlib.util.spec_from_file_location(
    "e2e", os.path.join(TOOLS, "release-e2e.py"))
e2e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e2e)

CMDS = os.path.join(tempfile.gettempdir(), "alttl-skip-probe-cmds.txt")


def send(item):
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write("/send droha %s\n" % item)


def main():
    folder = os.path.join(e2e.REPO, "testserver", "out-e2e")
    zips = glob.glob(os.path.join(folder, "*.zip"))
    if not zips:
        print("FAIL: no seed in testserver/out-e2e - run the gate once first",
              flush=True)
        return 1
    seed = os.path.basename(zips[0])
    plan = e2e.read_plan(folder, seed)

    target = None
    for i, (index, name) in enumerate(plan["slots"]):
        if "PawPrints" in name:
            target = (i, index, name)
    if target is None:
        print("FAIL: this seed has no PawPrints; nothing to probe", flush=True)
        return 1
    slot, index, name = target
    print(f"[1/7] target: slot {slot} {name} at level index {index}", flush=True)

    open(CMDS, "w", encoding="utf-8").close()
    # MultiServer's record of what it has already sent. Left in place, a rerun
    # replays every item and the probe measures a different starting state.
    for p in glob.glob(os.path.join(folder, "*.apsave")):
        os.remove(p)

    rc = 1
    server = None
    with Environment("skip-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        try:
            print("[2/7] starting the server", flush=True)
            server = subprocess.Popen(
                f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
                f'--port {e2e.PORT} "{os.path.join(folder, seed)}"',
                shell=True, cwd=e2e.AP,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)
            if not e2e.port_open():
                print("FAIL: the server never bound 38281", flush=True)
                return 1

            print("[3/7] launching the game", flush=True)
            log = e2e.Log()
            if e2e.force_windowed():
                print("      windowed, verified against the registry", flush=True)
            else:
                print("      WARNING: could not confirm windowed", flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            out = log.wait(["connected. "], 150, 3, "the connection")
            if "connected. " not in out:
                print("FAIL: never connected", flush=True)
                return 1

            # Deterministic on purpose. The seed holds two Skips but nothing
            # says either arrives before this slot, and the probe is about the
            # skip PATH, not the luck of the placement.
            print("[4/7] sending a Skip so one is definitely held", flush=True)
            log.new()
            send("Skip")
            got = log.wait(["received item: Skip"], 30, 4, "the skip item")
            if "received item: Skip" not in got:
                print("FAIL: the Skip never arrived", flush=True)
                return 1

            print(f"[5/7] booting {name}", flush=True)
            opened, out = e2e.boot_level(log, index)
            if not opened:
                print(f"FAIL: {name} did not open", flush=True)
                return 1

            print("[6/7] force-solving every controller", flush=True)
            done, text = e2e.solve_level(log)
            exhausted = e2e.EXHAUSTED_MARK in text
            print(f"      solve_level: done={done} exhausted={exhausted}",
                  flush=True)

            if done:
                print("NOTE: it completed after all. The phased level finished "
                      "from forced solves, so the skip path was never needed "
                      "and this run proves nothing about it.", flush=True)
                return 1
            if not exhausted:
                print("FAIL: not beaten and NOT marked exhausted. The skip path "
                      "will not fire, and the level is stuck for some other "
                      "reason - read the controller listing in the log.",
                      flush=True)
                return 1

            print("[7/7] spending the Skip", flush=True)
            log.new()
            e2e.dev("skip", 1.5)
            more = log.wait(["beaten:", "skip:", "check:"], 15, 7, "the skip")
            spent = "skip: spent one" in more
            beaten = "beaten:" in more
            print(f"      skip spent={spent}  mod banked a Beaten token={beaten}",
                  flush=True)

            if spent and beaten:
                print("PASS: an unfinishable level costs a Skip and counts. "
                      "The play() skip path is proven.", flush=True)
                rc = 0
            elif spent:
                print("FAIL: the Skip was spent but no Beaten token followed - "
                      "play() would still call the slot unbeaten and loop.",
                      flush=True)
            else:
                print("FAIL: the skip was refused. Relevant lines:", flush=True)
                for line in more.splitlines():
                    if "skip" in line.lower():
                        print("   " + line.strip(), flush=True)
        finally:
            subprocess.run(["powershell", "-NoProfile", "-Command",
                            "Get-Process | Where-Object {$_.ProcessName -like "
                            "'*Little*'} | Stop-Process -Force"],
                           capture_output=True)
            if server:
                subprocess.run(["powershell", "-NoProfile", "-Command",
                                "Get-NetTCPConnection -LocalPort 38281 -State "
                                "Listen -ErrorAction SilentlyContinue | "
                                "ForEach-Object { Stop-Process -Id "
                                "$_.OwningProcess -Force }"],
                               capture_output=True)
            print("Done: game closed, server stopped", flush=True)
    return rc


sys.exit(main())
