"""Which controller, when solved, stops the game blocking the others?

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-unblock.py [levelIndex ...]

WHAT probe-blocked.py LEAVES OPEN. It boots a level with ability locks OFF and
reports which groups the GAME still refuses to let you touch - a shut drawer,
an unrevealed phase. That names the victims. It does not name the CAUSE, and
the cause is the dependsOn edge the table is missing.

So this solves one controller at a time and re-measures after each. A group
whose blocked count falls when controller C is solved was being gated by C.
That is the edge, measured, rather than inferred from a containment guess -
and the containment guess has already been wrong in both directions once
(2026-09-22, checked against the one hand audit available).

CUMULATIVE, NOT ISOLATED, and the difference matters. Solving C then D and
measuring after each costs one boot per level; isolating every controller
would cost one boot per controller and take an hour. The cost is that a group
freed at step three might have been freed by step two as well. Where that
matters, re-run with a single index and read the order.

NOT A SUBSTITUTE FOR PLAY. `solve:` forces a controller's solved state, which
is not the same as a player opening a drawer - the game may reveal content on
the solved EVENT rather than on the interaction. A group that stays blocked
here might still be reachable by hand, and one that unblocks might still be
unfinishable. It narrows, and it names candidates.
"""
import json
import os
import re
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402
from harness_env import close_game                         # noqa: E402

LOGS = os.path.join(e2e.REPO, "testserver", "logs")
QUEUE = os.path.join(LOGS, "handtest-queue.json")
REPORT = os.path.join(LOGS, "unblock-report.json")

ROW = re.compile(
    r"\[(\d+)\] (.+?) type=(\S+) objects=(\d+) blocked=(\d+) dimmed=(\d+) ")


def measure(log):
    e2e.dev("locks", settle=2.5)
    text = log.new()
    out = {}
    for line in text.splitlines():
        m = ROW.search(line)
        if m:
            out[m.group(2).strip()] = int(m.group(5))
    return out


def main():
    wanted = [int(a) for a in sys.argv[1:] if a.isdigit()]
    if not wanted:
        with open(QUEUE, encoding="utf-8") as fh:
            wanted = [r["levelIndex"] for r in json.load(fh)
                      if r["verdict"] == "pending" and r["gameBlocks"]]

    print(f"[1/2] launching; {len(wanted)} level(s) to probe", flush=True)
    log = e2e.Log()
    log.before_launch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    got = ""
    end = time.time() + 240
    while time.time() < end:
        got += log.new()
        if "connected." in got:
            break
        time.sleep(1)
    if "connected." not in got:
        print("FAIL: never connected", flush=True)
        return 1
    time.sleep(10)

    report = {}
    for n, index in enumerate(wanted, start=1):
        log.new()
        e2e.dev(f"boot:{index}", settle=7.0)
        before = measure(log)
        level = next((l.split("locks: ")[1].split(" ")[0]
                      for l in log.new().splitlines() if "] locks: " in l), "?")

        blocked_now = {k for k, v in before.items() if v}
        print(f"[2/2] {n}/{len(wanted)} index {index}: "
              f"{len(blocked_now)} group(s) blocked at boot", flush=True)

        freed_by = {}
        state = dict(before)
        # Solve each controller in turn and watch what stops being blocked.
        for controller in list(before):
            if not any(state.values()):
                break
            e2e.dev(f"solve:{controller}", settle=2.5)
            log.new()
            after = measure(log)
            freed = [k for k, v in state.items()
                     if v and after.get(k, v) < v]
            if freed:
                freed_by[controller] = freed
                print(f"        solving {controller!r} freed: "
                      f"{', '.join(freed)}", flush=True)
            state = after

        if not freed_by and blocked_now:
            print(f"        nothing freed anything - the gate is not a "
                  f"controller solve", flush=True)
        report[str(index)] = {
            "level": level, "blockedAtBoot": sorted(blocked_now),
            "freedBy": freed_by,
        }

    close_game()
    with open(REPORT, "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)
    named = sum(1 for v in report.values() if v["freedBy"])
    print("", flush=True)
    print(f"Done: {len(report)} level(s) probed, {named} had a controller "
          f"whose solve freed others -> {REPORT}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
