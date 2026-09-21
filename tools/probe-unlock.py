"""Lock a level, grant the ability, then try to solve it.

WHY THIS EXISTS. The 2026-09-21 DLC gate could not force-solve five
levels and burned five Skips. Three probes ruled out three explanations,
each by changing one variable:

    seed          session      locks   result
    out-dlc-open  fresh each   off     8/8 beaten
    out-dlc-open  ONE          off     8/8 beaten
    out-dlc-held  fresh each   ON      (control, abilities held)

So it is not the level, not session reuse, and not ability locks merely
being switched on. What none of those reproduce is the thing a real run
does constantly: an object is LOCKED, the ability arrives, and the
object is UNLOCKED again. In out-dlc-held the abilities are held from
the start, so nothing ever transitions.

AbilityLocks treats the two directions differently. Freeze records each
collider and rigidbody before touching it and restores only what it
recorded, so an object it never froze is left alone. The flag path has
no such guard: ApplyWanted writes SetInteractable, SetPreventSelection,
the own-class flags and a tint to EVERY object on every pass. This asks
the game whether a level survives that round trip.

    py -3.13 tools/probe-unlock.py [--slots 0,6,7]

Reads testserver/out-dlc - locks ON, one starting ability - so the
levels really are gated when they open.
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import Environment, close_game, ensure_no_steam_relaunch

OUT = os.path.join(e2e.REPO, "testserver",
                   os.environ.get("ALTTL_SEED_DIR_NAME", "out-dlc"))
CMDS = os.path.join(e2e.REPO, "testserver", "unlock-cmds.txt")
SERVER_LOG = os.path.join(e2e.REPO, "testserver", "probe-unlock-server.log")

WANT = None
for i, a in enumerate(sys.argv):
    if a == "--slots" and i + 1 < len(sys.argv):
        WANT = {int(x) for x in sys.argv[i + 1].split(",")}


def wipe():
    gone = 0
    for folder, suffix in ((OUT, ".apsave"), (e2e.SAVE_DIR, None)):
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            if (suffix and name.endswith(suffix)) or \
                    (suffix is None and name.startswith("save_ap_")):
                os.remove(os.path.join(folder, name))
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


def send(item):
    """Grant one item through the server's own console."""
    with open(CMDS, "a", encoding="utf-8") as fh:
        fh.write(f"/send {e2e.SLOT} {item}\n")


#: Verbatim lines from a real run, for parse_dimmed's self-test.
SAMPLE_LOCKS = """\
[Info   :ALTTL Dev Tools] locks: DLC2 Pizza 1 controller(s), 0 of 48 object(s) dimmed, 0 not interactive
[Info   :ALTTL Dev Tools] locks: DLC1 Clock Cupboard 2 controller(s), 4 of 13 object(s) dimmed, 4 not interactive
[Warning:ALTTL Dev Tools] locks: no level running
"""


def parse_dimmed(text):
    """How many objects are DIMMED, from the game's own lock report.

    The line reads "<dimmed> of <total> object(s) dimmed", and the first
    version of this took the number AFTER " of " - the total. Every
    reading was therefore the object count, which never changes, so
    granting an ability showed "48 -> 48" and looked exactly like a
    unlock that had failed. Three slots were reported broken on that
    basis before the format was checked against a real line.
    """
    total = 0
    for line in text.splitlines():
        if " object(s) dimmed" not in line or " of " not in line:
            continue
        head = line.split(" of ")[0]
        try:
            total += int(head.split(",")[-1].strip())
        except (IndexError, ValueError):
            pass
    return total


def self_test():
    got = parse_dimmed(SAMPLE_LOCKS)
    if got != 4:
        sys.exit(f"self-test: parse_dimmed gave {got}, expected 4 "
                 f"(0 from Pizza + 4 from Clock Cupboard)")
    if parse_dimmed("") != 0:
        sys.exit("self-test: an empty report is not dimmed")


def dimmed_now(log):
    """Ask the game, once."""
    log.new()
    e2e.dev("locks", 1.0)
    out = log.wait(["locks: "], 15, 6, "the lock report")
    time.sleep(1.0)
    out += log.new()
    return parse_dimmed(out), out


def wait_unlocked(log, seconds=25):
    """Poll until nothing is dimmed, or give up.

    POLLED, NOT SAMPLED ONCE. AbilityLocks reapplies about once a
    second and an arriving item has to travel server -> mod -> next
    pass, so a single read two seconds after the grant cannot tell a
    slow unlock from a broken one.
    """
    deadline = time.time() + seconds
    last = None
    while time.time() < deadline:
        last, _out = dimmed_now(log)
        if last == 0:
            return 0
        time.sleep(1.0)
    return last


def missing_for(slot, plan, where, held):
    out = set()
    for loc in where.get(slot, []):
        out |= set(plan["requirements"].get(loc, ())) - held
    return sorted(out)


def main():
    seeds = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not seeds:
        sys.exit(f"no seed in {os.path.relpath(OUT, e2e.REPO)}")
    seed = seeds[0]
    plan = e2e.read_plan(OUT, seed)
    where = e2e.locations_for_slots(plan)
    starting, _placements = e2e.read_spoiler(OUT, seed)
    e2e._load_ability_tables()
    held = {i for i in starting if i in e2e._ABILITY_NAMES}

    todo = []
    for i, (index, name) in enumerate(plan["slots"]):
        need = missing_for(i, plan, where, held)
        if WANT is not None and i not in WANT:
            continue
        todo.append((i, index, name, need))

    close_game()
    stop_server()
    print(f"cleared {wipe()} saved-progress file(s)", flush=True)
    serve(seed)
    print(f"-- {len(todo)} slot(s) from {seed}, holding {sorted(held)} --",
          flush=True)

    verdicts = []
    for n, (slot, index, name, need) in enumerate(todo, 1):
        tag = f"[{n}/{len(todo)} slot {slot} {name}]"
        close_game()
        log = e2e.Log()
        log.before_launch()
        ensure_no_steam_relaunch()
        subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
        if "connected. " not in log.wait(["connected. "], 150, 1,
                                         "the connection"):
            print(f"{tag} NO CONNECTION", flush=True)
            verdicts.append((slot, name, "NO CONNECTION"))
            continue
        time.sleep(3.0)

        opened, _out = e2e.boot_level(log, index)
        if not opened:
            print(f"{tag} DID NOT OPEN", flush=True)
            verdicts.append((slot, name, "DID NOT OPEN"))
            continue

        before, _ = dimmed_now(log)
        print(f"{tag} step 1/3: {before} object(s) dimmed, needs {need}",
              flush=True)

        if not need:
            # Nothing to unlock - this slot is the CONTROL. It proves the
            # rig solves a level that never transitioned, in the same
            # session shape as the ones that did.
            print(f"{tag} step 2/3: control, nothing to grant", flush=True)
        else:
            for ability in need:
                send(ability)
            got = log.wait([f"received item: {need[-1]}"], 25, 5,
                           "the ability")
            if f"received item: {need[-1]}" not in got:
                print(f"{tag} THE ABILITY NEVER ARRIVED", flush=True)
                verdicts.append((slot, name, "ABILITY NEVER ARRIVED"))
                continue
            after = wait_unlocked(log)
            print(f"{tag} step 2/3: granted {need}, dimmed {before} -> "
                  f"{after}", flush=True)
            if after:
                print(f"{tag} STILL DIMMED AFTER 25s", flush=True)
                verdicts.append((slot, name, f"STILL DIMMED ({after})"))
                continue

        done, chunk = e2e.solve_level(log)
        chunk += log.wait(["beaten:", "check:", "credits:"], 10, 6,
                          "the check")
        if done and "beaten:" in chunk:
            verdict = "beaten"
        elif e2e.EXHAUSTED_MARK in chunk:
            verdict = "CANNOT FORCE AFTER UNLOCK"
        elif "check:" in chunk:
            verdict = "partial"
        else:
            verdict = "NOTHING"
        print(f"{tag} step 3/3: {verdict}", flush=True)
        verdicts.append((slot, name, verdict))

    close_game()
    print(f"server stopped: {stop_server()}", flush=True)
    print("", flush=True)
    for slot, name, verdict in verdicts:
        print(f"   slot {slot:>2} {name:34s} {verdict}", flush=True)
    bad = [v for v in verdicts if v[2] not in ("beaten", "partial")]
    print(f"Done: {len(verdicts)} slot(s), {len(bad)} needing attention",
          flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    self_test()
    with Environment("probe-unlock") as _env:
        _env.configure(Host="localhost", Port=str(e2e.PORT),
                       SlotName=e2e.SLOT, AutoConnect="true")
        try:
            sys.exit(main())
        finally:
            stop_server()
