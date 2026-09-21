"""The five gate assertions nothing ever checked on their own.

WHY THIS EXISTS. Phase 5 of the DLC plan says every part gets tested by
itself and the gate runs LAST, once, to prove the parts compose. Nine of
the twenty-five assertions had never been exercised outside a full
fifteen-minute run. Three had probes sitting unused; these five had
nothing at all:

    the game logged no unexplained errors
    the controller table matches every level played
    the campaign progress is untouched
    the run credited no real daily
    no DLC puzzle was written to the campaign save

Every one of them is a measurement over A SESSION, not over a run. None
needs eight slots, a full item economy or a goal. Play one DLC level and
they are all answerable - which is the whole point: a fifteen-minute gate
was being used to answer a ninety-second question.

    py -3.13 tools/probe-isolation.py [--slots 0,3]

Reads testserver/out-dlc. The campaign save is NEVER written by this
probe; it is read before and after and compared, which is the assertion.
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
CMDS = os.path.join(e2e.REPO, "testserver", "isolation-cmds.txt")
SERVER_LOG = os.path.join(e2e.REPO, "testserver", "probe-isolation-server.log")

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


def main():
    seeds = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not seeds:
        sys.exit(f"no seed in {os.path.relpath(OUT, e2e.REPO)} - make one "
                 f"with tools/make-seed.py --dlc")
    seed = seeds[0]
    plan = e2e.read_plan(OUT, seed)
    slots = plan["slots"]
    todo = [(i, idx, name) for i, (idx, name) in enumerate(slots)
            if WANT is None or i in WANT]

    close_game()
    stop_server()
    print(f"cleared {wipe()} saved-progress file(s)", flush=True)
    serve(seed)

    # READ THE CAMPAIGN BEFORE ANYTHING LAUNCHES. These three assertions
    # are differences, so the "before" has to be taken with the game shut
    # - the game rewrites its save on exit, and a baseline taken while it
    # is running measures the wrong moment.
    before = e2e.campaign_progress(e2e.CAMPAIGN)
    dailies_before = e2e.daily_completions(e2e.CAMPAIGN)
    dlc_before = e2e.dlc_completions(e2e.CAMPAIGN)
    print(f"[1/4] campaign baseline: {len(dlc_before)} DLC completion(s), "
          f"daily count {dailies_before}", flush=True)

    log = e2e.Log()
    log.before_launch()
    ensure_no_steam_relaunch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    if "connected. " not in log.wait(["connected. "], 150, 1, "the connection"):
        close_game()
        stop_server()
        sys.exit("FAILED TO CONNECT")
    time.sleep(3.0)

    print(f"[2/4] playing {len(todo)} DLC level(s) in one session", flush=True)
    transcript = ""
    for n, (slot, index, name) in enumerate(todo, 1):
        if n > 1:
            e2e.to_title(log)
        opened, out = e2e.boot_level(log, index)
        transcript += out
        if not opened:
            print(f"   [{n}/{len(todo)}] {name}: DID NOT OPEN", flush=True)
            continue
        _done, chunk = e2e.solve_level(log)
        transcript += chunk
        transcript += log.wait(["beaten:", "check:"], 8, 4, "the check")
        print(f"   [{n}/{len(todo)}] {name}: played", flush=True)

    close_game()
    time.sleep(2.0)
    transcript += log.new()
    print(f"server stopped: {stop_server()}", flush=True)

    print("[3/4] reading the campaign back", flush=True)
    after = e2e.campaign_progress(e2e.CAMPAIGN)
    dailies_after = e2e.daily_completions(e2e.CAMPAIGN)
    dlc_after = e2e.dlc_completions(e2e.CAMPAIGN)

    errors = e2e.error_census(transcript)
    surprises = e2e.unexplained(errors)
    table = e2e.table_audit(transcript)

    print("[4/4] results", flush=True)
    results = [
        ("the game logged no unexplained errors", not surprises,
         "; ".join(surprises[:3])),
        ("the controller table matches every level played", not table,
         "; ".join(table[:3]) if isinstance(table, list) else str(table)),
        ("the campaign progress is untouched", before == after,
         e2e.campaign_diff(before, after)),
        ("the run credited no real daily", dailies_after == dailies_before,
         f"{dailies_before} -> {dailies_after}"),
        ("no DLC puzzle was written to the campaign save",
         dlc_after == dlc_before,
         f"{len(dlc_before)} -> {len(dlc_after)}"),
    ]
    bad = 0
    for name, ok, detail in results:
        bad += not ok
        print(f"  {'PASS' if ok else 'FAIL'}  {name}"
              + (f"  [{detail}]" if detail and not ok else ""), flush=True)

    print(f"Done: {len(results) - bad}/{len(results)} isolation checks",
          flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    with Environment("probe-isolation") as _env:
        _env.configure(Host="localhost", Port=str(e2e.PORT),
                       SlotName=e2e.SLOT, AutoConnect="true")
        try:
            sys.exit(main())
        finally:
            stop_server()
