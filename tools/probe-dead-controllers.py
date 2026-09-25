"""Which of a level's controllers actually report solved, and which are dead?

The question the level table keeps getting wrong. `levels.json` is a runtime
sweep of what REGISTERS, and a registered controller is assumed to be a
location the player can earn. SomethingEggstra Fridge disproved that: it
registers three, all three are in the table, the level completes - and only
one ever fires. The other two are locations no seed can ever award.

Found from a real multiworld's room log, by the rule "a Solution fired but a
part did not". This probe is the same question asked directly, so it can be
answered for any level in a couple of minutes instead of waiting for someone
to finish that puzzle in a run.

It also reports how many objects the ability system DIMS, because that decides
the other half: a controller that never pays a check but whose objects are
still gated is an ability the level genuinely requires and must survive the
table edit as an `extraAbility`.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-dead-controllers.py [levelIndex ...]
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-dead-controllers.py --only NAME

`--only` answers a different and sharper question: is an ability REQUIRED to
finish the level? It solves that one controller and nothing else, then reports
whether the game raises LevelComplete. If it does, every other controller -
and every ability gating one - is optional decoration, and declaring those
abilities on the solution would gate a check that does not need them.

Defaults to the fridge (109). Generates its own seed, because the level MUST
be in it: the mod files a check only against a slot the run contains, so
booting a level the seed does not hold reports every controller dead. The
first version of this probe did exactly that and called EggsContainable dead,
which the room log disproves.
"""
import glob
import os
import re
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment

import release_e2e as e2e

#: SomethingEggstra Fridge. The level this probe was written for.
DEFAULT_LEVELS = [109]

#: The event pack a probed level belongs to, so the generated seed contains
#: it. Only the packs this probe has needed; add as required.
LEVEL_PACK = {109: "something_eggstra"}


def generate_for(level_index):
    """A seed guaranteed to contain the level under test.

    Everything is tilted at one event pack: generators and campaign off,
    mechanic coverage off so nothing else is dragged in. Ability locks stay ON
    - the dimming figure this probe reports is half the answer.
    """
    pack = LEVEL_PACK.get(level_index)
    if pack is None:
        raise SystemExit(f"no pack recorded for level {level_index}; add one "
                         "to LEVEL_PACK")

    yaml_dir = os.path.join(e2e.REPO, "testserver", "yaml-deadprobe")
    out = os.path.join(e2e.REPO, "testserver", "out-deadprobe")
    for d in (yaml_dir, out):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))

    lines = [
        f"name: {e2e.SLOT}",
        "game: A Little to the Left",
        "requires:",
        "  version: 0.6.7",
        "A Little to the Left:",
        "  puzzle_count: 15",
        "  levels_to_beat: 15",
        "  pack_size: 8",
        # Everything tilted at the one event pack, so the level under
        # test cannot be crowded out by a generator or a campaign draw.
        "  generator_weight: 0",
        "  base_weight: 0",
        "  archive_weight: 100",
        "  mechanic_coverage: 0",
        # Locks stay ON: the dimming figure is half of what this asks.
        "  ability_locks: true",
        "  starting_abilities: 0",
        "  archive_packs:",
        f"    - {pack}",
    ]
    with open(os.path.join(yaml_dir, "probe.yaml"), "w",
              encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", yaml_dir,
         "--outputpath", out, "--seed", "20260910"],
        cwd=e2e.AP, capture_output=True, text=True)
    zips = glob.glob(os.path.join(out, "*.zip"))
    if not zips:
        print(r.stdout[-2000:], flush=True)
        print(r.stderr[-2000:], flush=True)
        raise SystemExit("generation produced no seed")
    return out, os.path.basename(zips[0])


def controllers_in(text):
    """(name, solved) for the last controller listing in `text`."""
    found = []
    for line in text.splitlines():
        m = re.search(r"\[\d+\]\s+(.*?)\s+type=(\S+)\s+solved=(True|False)", line)
        if m:
            found.append((m.group(1).strip(), m.group(2), m.group(3) == "True"))
    return found


def probe_level(log, index, only=None):
    print(f"\n=== level index {index} ===", flush=True)

    opened, out = e2e.boot_level(log, index)
    if not opened:
        print(f"  FAIL: level {index} did not open", flush=True)
        return None

    # What the ability system did to it. The mod logs this on every level
    # start; it is the only statement of how many objects are gated.
    dim = "?"
    for line in out.splitlines():
        m = re.search(r"abilities: (\d+) locked, (\d+) open, (\d+) objects", line)
        if m:
            dim = f"{m.group(1)} locked / {m.group(2)} open, {m.group(3)} objects"
    print(f"  abilities: {dim}", flush=True)

    log.new()
    e2e.dev("controllers", 1.0)
    listing = log.wait(["controllers: "], 8, 3, "the controller list")
    time.sleep(1.5)
    listing += log.new()
    before = controllers_in(listing)
    if not before:
        print("  FAIL: no controller listing", flush=True)
        return None
    print(f"  registers {len(before)}: "
          + ", ".join(n for n, _, _ in before), flush=True)

    if only is not None:
        return solve_one(log, before, only)

    # Solve every one and record which the MOD routes to a check.
    fired, text = set(), ""
    for i, (name, _type, solved) in enumerate(before):
        log.new()
        e2e.dev(f"solve:{i}", 0.9)
        chunk = log.new()
        text += chunk
        for line in chunk.splitlines():
            if "check:" in line or "no location for it" in line:
                fired.add(name)
    time.sleep(1.0)
    text += log.new()

    print("  %-34s %-22s %s" % ("controller", "type", "verdict"), flush=True)
    dead = []
    for name, ctype, _ in before:
        # The mod names the location, not the controller, so match on either.
        hit = (name in fired
               or any(name in l for l in text.splitlines() if "check:" in l))
        verdict = "fires a check" if hit else "DEAD - never reports"
        if not hit:
            dead.append((name, ctype))
        print("  %-34s %-22s %s" % (name[:34], ctype[:22], verdict), flush=True)

    if "LevelComplete " in text:
        print("  the level COMPLETED from forced solves", flush=True)
    return dead


def solve_one(log, controllers, only):
    """Solve exactly one controller and see whether the LEVEL finishes.

    The point is the ability requirement. A level that completes with one
    controller satisfied does not need the abilities gating the others, and
    putting them on the solution location would hold a reachable check behind
    items the player never has to use.
    """
    match = [i for i, (n, _t, _s) in enumerate(controllers) if n == only]
    if not match:
        print(f"  FAIL: no controller named {only!r}; this level has "
              + ", ".join(repr(n) for n, _t, _s in controllers), flush=True)
        return None

    log.new()
    e2e.dev(f"solve:{match[0]}", 1.0)
    text = log.new()
    text += log.wait(["LevelComplete ", "no level running"], 8, 3,
                     "the completion")

    done = "LevelComplete " in text
    print(f"  solved ONLY {only!r}", flush=True)
    for name, _t, _s in controllers:
        if name != only:
            print(f"    left untouched: {name}", flush=True)
    if done:
        print("  RESULT: the level COMPLETED. The other controllers are "
              "optional, so the abilities gating them are NOT required to "
              "earn the solution.", flush=True)
    else:
        print("  RESULT: the level did NOT complete. Something else is "
              "needed, so those abilities ARE required.", flush=True)
    return None


def main():
    args = sys.argv[1:]
    only = None
    if "--only" in args:
        at = args.index("--only")
        only = args[at + 1]
        args = args[:at] + args[at + 2:]
    levels = [int(a) for a in args] or DEFAULT_LEVELS

    print(f"[0/3] generating a seed containing level {levels[0]}", flush=True)
    folder, seed = generate_for(levels[0])
    plan = e2e.read_plan(folder, seed)
    holds = [n for _i, n in plan["slots"]]
    print(f"      {seed}: {', '.join(holds)}", flush=True)
    for p in glob.glob(os.path.join(folder, "*.apsave")):
        os.remove(p)

    server = None
    dead_total = []
    with Environment("dead-controller-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        try:
            print("[1/3] starting the server", flush=True)
            # NOTE the level must be one of these slots - see the module
            # docstring for what happens when it is not.
            server = subprocess.Popen(
                f'py -3.13 -u MultiServer.py --port {e2e.PORT} '
                f'"{os.path.join(folder, seed)}"',
                shell=True, cwd=e2e.AP, stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)
            if not e2e.port_open():
                print("FAIL: the server never bound", flush=True)
                return 1

            print("[2/3] launching the game", flush=True)
            log = e2e.Log()
            what_display, windowed = e2e.describe_display()
            print(f"      {what_display}", flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            if "connected. " not in log.wait(["connected. "], 150, 2,
                                             "the connection"):
                print("FAIL: never connected", flush=True)
                return 1

            print("[3/3] probing", flush=True)
            for index in levels:
                dead = probe_level(log, index, only)
                if dead:
                    dead_total += [(index, n, t) for n, t in dead]
        finally:
            subprocess.run(["powershell", "-NoProfile", "-Command",
                            "Get-Process | Where-Object {$_.ProcessName -like "
                            "'*Little*'} | Stop-Process -Force"],
                           capture_output=True)
            subprocess.run(["powershell", "-NoProfile", "-Command",
                            "Get-NetTCPConnection -LocalPort 38281 -State "
                            "Listen -ErrorAction SilentlyContinue | "
                            "ForEach-Object { Stop-Process -Id "
                            "$_.OwningProcess -Force }"],
                           capture_output=True)

    if dead_total:
        print("\nDEAD CONTROLLERS - these mint locations nobody can earn:",
              flush=True)
        for index, name, ctype in dead_total:
            print(f"  level {index}: {name} ({ctype})", flush=True)
    print(f"Done: {len(dead_total)} dead controller(s) across "
          f"{len(levels)} level(s)", flush=True)
    return 0


# A GUARD, because these are importable now. Without it, `import
# probe_dead_controllers` launches the game, starts a MultiServer and plays a
# level - which is exactly what happened to a smoke test that only meant to
# check the module parsed.
if __name__ == "__main__":
    sys.exit(main())
