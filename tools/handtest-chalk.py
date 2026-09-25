"""Set up the ONE open question: do the chalk jigsaws need the drawer open?

    PYTHONUNBUFFERED=1 py -3.13 -u tools/handtest-chalk.py

WHAT IS BEING ASKED, and why it is the only thing left for a person. Each of
NeatStreak_Paper Plane Supplies' seven chalk jigsaws declares ['Jigsaw'] alone.
apworld/alttl/test/test_generation.py deliberately excludes them from the
drawer's contents, its comment saying they are "assembled on the desk" - and
docs/verification-log.md records that the test "was corrected to match the
implementation, not the other way round", so that comment has NO observation
behind it. The 2026-09-22 sweep, joining Drawer.ContainedObjects to the
controllers that manage them while the instance ids were live, puts FIVE of the
seven inside the drawer: Blue, Green, Mint, Yellow, Pink. Purple and Red are
not. If the five are right, five part locations understate their requirement,
which is the direction that ends runs.

The 5/2 split is what makes this worth playing: a harvest error would not
separate them, and it hands the test its own control.

THE SESSION. Ability locks ON, holding Jigsaw and nothing else - so the mod's
real lock is doing the gating, not a DevTools freeze. That matters: freeze only
disables colliders, and on 2026-09-22 it answered "unreachable" for the wrong
reason because it kills the chalk's own colliders too. The real lock also
writes Interactable / PreventSelection, which is the signal
probe-toothless-gates.py decided is authoritative.

DO NOT open the drawer. The whole question is what is reachable while it is
shut.
"""
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402
from harness_env import close_game                         # noqa: E402

YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-chalk")
OUT_DIR = os.path.join(e2e.REPO, "testserver", "out-chalk")

YAML = f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 40
  levels_to_beat: 40
  pack_size: 10
  # ON. The mod's own lock must be the thing gating, not a DevTools freeze.
  ability_locks: true
  # None at random - start_inventory below grants exactly one, so what is held
  # is known rather than drawn.
  starting_abilities: 0
  cat_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  cupboards_and_drawers: true
  seeing_stars: true
  # INSIDE the game section. Archipelago rejects it at the document level:
  # "Option start_inventory has to be in a game's section, not on its own."
  start_inventory:
    Jigsaw: 1
"""


def main():
    print("[1/6] clearing the way", flush=True)
    close_game()
    pid = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"Get-NetTCPConnection -LocalPort {e2e.PORT} -State Listen "
         "-ErrorAction SilentlyContinue | Select-Object -First 1 "
         "-ExpandProperty OwningProcess"],
        capture_output=True, text=True).stdout.strip()
    if pid.isdigit():
        subprocess.run(["powershell", "-NoProfile", "-Command",
                        f"Stop-Process -Id {pid} -Force"], capture_output=True)
        time.sleep(2)
    for name in list(os.listdir(e2e.SAVE_DIR)):
        if name.startswith("save_ap_") or name == "alttl-last-session.json":
            os.remove(os.path.join(e2e.SAVE_DIR, name))

    print("[2/6] generating: locks ON, holding Jigsaw and nothing else", flush=True)
    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "chalk.yaml"), "w", encoding="utf-8") as fh:
        fh.write(YAML)
    result = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT_DIR, "--seed", "20260922"],
        cwd=e2e.AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if result.returncode:
        print(result.stdout[-1500:], flush=True)
        print(result.stderr[-1500:], flush=True)
        return 1
    seed = os.path.join(OUT_DIR,
                        [f for f in os.listdir(OUT_DIR) if f.endswith(".zip")][0])
    print(f"      {os.path.basename(seed)}", flush=True)

    print("[3/6] serving it", flush=True)
    logs = os.path.join(e2e.REPO, "testserver", "logs")
    os.makedirs(logs, exist_ok=True)
    flags = 0
    if os.name == "nt":
        flags = (subprocess.DETACHED_PROCESS
                 | subprocess.CREATE_NEW_PROCESS_GROUP)
    subprocess.Popen(
        [sys.executable, "MultiServer.py", "--port", str(e2e.PORT), seed],
        cwd=e2e.AP,
        stdout=open(os.path.join(logs, "chalk-server.log"), "w"),
        stderr=open(os.path.join(logs, "chalk-server.err"), "w"),
        stdin=subprocess.DEVNULL, creationflags=flags,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    for _ in range(60):
        if e2e.port_open():
            break
        time.sleep(1)
    e2e.write_config()
    e2e.write_devtools_config()

    print("[4/6] launching and waiting for the mod to connect", flush=True)
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
    line = next((l.split("] ")[-1] for l in got.splitlines()
                 if "connected." in l), "NOT CONNECTED")
    print(f"      {line}", flush=True)
    time.sleep(8)

    print("[5/6] booting Paper Plane Supplies", flush=True)
    log.new()
    e2e.dev("boot:1020", settle=9.0)
    e2e.dev("locks", settle=3.0)
    text = log.new()
    for l in text.splitlines():
        if "abilities:" in l and ("locked," in l or "waiting on" in l):
            print("      " + l.split("] ", 1)[-1], flush=True)
        if "locks: " in l:
            print("      " + l.split("] ", 1)[-1], flush=True)

    print("", flush=True)
    print("=" * 70, flush=True)
    print("  DO NOT OPEN THE DRAWER.", flush=True)
    print("=" * 70, flush=True)
    print("", flush=True)
    print("  You hold Jigsaw and nothing else. The drawer should refuse to", flush=True)
    print("  open. It may NOT look greyed out - that is expected and already", flush=True)
    print("  named in the notes as the invisible gate, not a contradiction.", flush=True)
    print("", flush=True)
    print("  1. Assemble the BLUE chalk jigsaw.   sweep says: IN the drawer", flush=True)
    print("  2. Assemble the PURPLE chalk jigsaw. sweep says: NOT in it", flush=True)
    print("", flush=True)
    print("  Purple is the control. Decided in advance:", flush=True)
    print("    both work        -> ['Jigsaw'] is correct, nothing changes", flush=True)
    print("    purple only      -> the sweep is right, 5 locations understate", flush=True)
    print("    neither works    -> the drawer gates the whole desk, all 7 wrong", flush=True)
    print("    blue only        -> the sweep has it backwards; stop trusting it", flush=True)
    print("", flush=True)
    print("Done: game is running and waiting. Tell me which assembled.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
