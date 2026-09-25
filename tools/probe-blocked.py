"""What does the GAME itself refuse to let you touch, with no ability locks?

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-blocked.py [levelIndex ...]

THE DETECTOR THIS PROJECT NEEDED AND KEPT NOT USING. Three instruments in a row
gave confident wrong answers about reachability on 2026-09-22 - a collider
census, a DevTools freeze that walked the wrong object list, and the sweep's
drawer containment - because all three measured the ABILITY LOCK while the
thing actually stopping the player was the game.

`locks` already reports two different numbers and the distinction is the whole
point. `dimmed` is what the mod tinted. `blocked` is `!Interactable ||
PreventSelection`, which SceneCommands records the game causing on its own:
"plain Draggables objects inside a closed drawer read non-interactive with no
ability lock anywhere near them".

So run with ability_locks OFF. The mod then dims nothing, and every blocked
object left is the GAME refusing - a shut drawer, an unrevealed phase, one
group's objects buried under another's. That is exactly the class of gate that
`dependsOn` does not record and that ended a run on 2026-09-21.

WHAT IT PROVES AND WHAT IT DOES NOT. blocked > 0 at boot means something in the
level gates that group and the ability table probably does not say so - a
finding. blocked == 0 does NOT clear a group: the chalk jigsaws read
blocked=0 dimmed=0 while being physically behind a drawer, because occlusion is
not non-interactivity. This narrows the queue for a human; it does not empty it.
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

YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-blocked")
OUT_DIR = os.path.join(e2e.REPO, "testserver", "out-blocked")
REPORT = os.path.join(e2e.REPO, "testserver", "logs", "blocked-report.json")
CANDIDATES = os.path.join(e2e.REPO, "testserver", "logs", "candidates.json")

YAML = f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 40
  levels_to_beat: 40
  pack_size: 10
  # OFF. With the mod dimming nothing, every blocked object is the GAME's.
  ability_locks: false
  starting_abilities: 0
  cat_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  cupboards_and_drawers: true
  seeing_stars: true
"""

#: The `reachable` table, whose columns are
#: controller, type, total, touchable, inactive, noCollider, colliderOff,
#: stuck, done. STUCK is the one that matters and `locks` cannot supply it.
ROW = re.compile(
    r"reachable:\s+(.+?)	(\S+)	(\d+)	(\d+)	(\d+)	(\d+)	(\d+)	"
    r"(\d+)	(\d+)")

#: Generous, because the failure this guards against is a level read too early
#: and the cost of waiting is only time.
BOOT_SETTLE = 8.0
SETTLE_STEP = 3.0
SETTLE_TRIES = 6


#: FOUR LEVELS WHOSE ANSWER IS ALREADY KNOWN, because droha played them.
#: This is the fixture a full sweep has to pass first.
#:
#: WHY IT EXISTS. On 2026-09-22 three instruments in a row reported confident
#: wrong answers across dozens of levels before anyone checked them against a
#: case with a known answer: a collider census, a freeze walking the wrong
#: object list, and a `blocked` count that fused "gated" with "already placed".
#: Each was caught only after its numbers had been reported. droha: "only do
#: full runs once you prove your tools work. that's just basic coding/testing."
#:
#: Expectations are the PLAY RESULTS, not previous probe output - a fixture
#: built from an instrument's own history only proves it is consistently wrong.
SELFTEST = [
    # droha finished it holding NOTHING: "there's no container/drawer in that
    # level". Any detector that flags this is over-reporting. The `blocked`
    # version did, which is how it was caught.
    (50, "Workbench", 0, 0),
    # droha could not finish it with Drawer withheld, 2026-09-18.
    (1021, "NeatStreak_Tool Drawer", 1, None),
    # The 2026-09-21 softlock. Lids and Stack 1 both unreachable at boot.
    (82, "TupperwareNesting", 1, None),
    # droha, 2026-09-22: the chalk pieces are behind the drawer.
    (1020, "NeatStreak_Paper Plane Supplies", 1, None),
]


def selftest(log):
    """Prove the detector before trusting it on anything.

    Returns True when every known level answers as a human found it. `least`
    is a floor rather than an exact count: what is verified is the DIRECTION -
    free levels must read zero, gated levels must read something.
    """
    print("[selftest] four levels whose answer droha established by playing",
          flush=True)
    ok = True
    for index, name, least, most in SELFTEST:
        e2e.dev(f"boot:{index}", settle=BOOT_SETTLE)
        snap = read_locks(log)
        stuck = sum(1 for _objs, s in snap.values() if s)
        verdict = "ok"
        if stuck < least or (most is not None and stuck > most):
            verdict = "WRONG"
            ok = False
        want = f">={least}" if most is None else f"=={most}"
        print(f"   {verdict:5} {name:32} {stuck} stuck group(s), want {want}",
              flush=True)
    if not ok:
        print("[selftest] FAILED - the detector disagrees with a level a "
              "human already settled. Fix it before sweeping anything.",
              flush=True)
    return ok


def worth_probing():
    """Levels where an understatement is even possible.

    DO NOT SWEEP ALL 173. droha, 2026-09-22: "do we need to run all 173 every
    time? are there ones that we've proven and can make this re-running a huge
    test go faster?" - and the answer is that two thirds of them cannot carry
    this bug at all.

    The bug is a group asking for LESS than its level needs. A group that
    already asks for everything its level asks for cannot be asking for too
    little, whatever the game does to its objects at boot. Neither can a level
    that needs no abilities, nor a single-group level, which is its own level's
    whole requirement by definition.

    That filter was being applied AFTER the sweep, which is how a 173-level run
    spent twenty minutes measuring Bowls, Cat Frame, Calendar and Candles -
    every one of them genuinely stuck and every one already correct.

    51 of 173 survive. Re-running becomes cheap enough to do routinely, which
    matters more than the single run: an instrument nobody re-runs is one
    nobody checks.
    """
    sys.path.insert(0, os.path.join(e2e.REPO, "Archipelago"))
    from worlds.alttl import data

    out = []
    for level in data.LEVELS:
        whole = set(level.enforced_abilities)
        if not whole:
            continue
        if any(set(a) < whole
               for a in level.enforced_part_abilities.values()):
            out.append(level.level_index)
    print(f"      {len(out)} of {len(data.LEVELS)} levels can possibly "
          f"understate; skipping {len(data.LEVELS) - len(out)} that "
          f"structurally cannot", flush=True)
    return out


def read_locks(log, want_text=False):
    """The blocked count per controller, once.

    Returns a comparable snapshot so the caller can ask whether the scene has
    stopped changing. See the note at the call site: a fixed wait produced a
    dozen fabricated gates.
    """
    e2e.dev("reachable", settle=2.0)
    text = log.new()
    snapshot = {}
    for line in text.splitlines():
        m = ROW.search(line)
        if m:
            snapshot[m.group(1).strip()] = (int(m.group(3)), int(m.group(8)))
    return (snapshot, text) if want_text else snapshot


def serve():
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

    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "b.yaml"), "w", encoding="utf-8") as fh:
        fh.write(YAML)
    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT_DIR, "--seed", "20260922"], cwd=e2e.AP,
        capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode:
        print(r.stderr[-1200:], flush=True)
        raise SystemExit("generation failed")
    seed = os.path.join(OUT_DIR,
                        [f for f in os.listdir(OUT_DIR) if f.endswith(".zip")][0])
    logs = os.path.join(e2e.REPO, "testserver", "logs")
    flags = (subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP) \
        if os.name == "nt" else 0
    subprocess.Popen(
        [sys.executable, "MultiServer.py", "--port", str(e2e.PORT), seed],
        cwd=e2e.AP, stdout=open(os.path.join(logs, "blocked-server.log"), "w"),
        stderr=open(os.path.join(logs, "blocked-server.err"), "w"),
        stdin=subprocess.DEVNULL, creationflags=flags,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    for _ in range(60):
        if e2e.port_open():
            break
        time.sleep(1)
    e2e.write_config()
    e2e.write_devtools_config()


def main():
    wanted = [int(a) for a in sys.argv[1:] if a.isdigit()]
    if not wanted:
        wanted = worth_probing()

    print(f"[1/3] serving a locks-OFF seed", flush=True)
    serve()

    print("[2/3] launching", flush=True)
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

    # THE GATE. A sweep is only worth running on an instrument that reproduces
    # answers a human already established; otherwise it produces findings at
    # scale and they all have to be thrown away afterwards, which is what
    # happened twice on 2026-09-22. --skip-selftest exists for debugging the
    # selftest itself and should not be used to get a sweep out the door.
    if "--skip-selftest" not in sys.argv:
        if not selftest(log):
            close_game()
            return 1
        print("[selftest] passed", flush=True)

    out = {}
    total = len(wanted)
    for n, index in enumerate(wanted, start=1):
        log.new()
        # Each level gets a FRESH BOOT from a clean state. boot: has been seen
        # to report "tore down 0 live level(s)" after a level-select round
        # trip, stacking two copies of a level - which is how droha ended up
        # playing a scene with two sets of drop targets fighting. Nothing here
        # touches the level select, so the teardown has a live level to find.
        e2e.dev(f"boot:{index}", settle=BOOT_SETTLE)

        # MEASURE TWICE AND ONLY BELIEVE A STABLE ANSWER.
        #
        # A fixed settle was wrong and the 173-level sweep of 2026-09-22 proved
        # it in the worst way: the first eighty-five levels agreed exactly with
        # an earlier thirty-one-level run, and from index 86 onward EVERY level
        # came back 100 per cent blocked. DLC1 Bathroom Cupboard went from 0 of
        # 3 to 3 of 3; Pantry 0 of 6 to 6 of 6; Paper Plane Supplies 6 of 11 to
        # 11 of 11. Nothing about those levels changed. The session had booted
        # a hundred levels and initialisation had simply grown slower than the
        # seven seconds it was given, so `locks` was reading half-built scenes
        # where every object is still inactive - which counts as blocked.
        #
        # That would have written a dozen fabricated gates into levels.json.
        # So: read, wait, read again, and accept only when two consecutive
        # reads agree. A level that never settles is reported rather than
        # guessed at, because "still loading" and "genuinely blocked" produce
        # identical numbers and only time separates them.
        first = read_locks(log)
        text = ""
        for _ in range(SETTLE_TRIES):
            time.sleep(SETTLE_STEP)
            second, text = read_locks(log, want_text=True)
            if second == first:
                break
            first = second
        else:
            print(f"      WARNING: index {index} never settled; its numbers "
                  f"are not trustworthy", flush=True)

        level = next((l.split("reachable: ")[1].split(" ")[0]
                      for l in text.splitlines()
                      if "] reachable: " in l and "--" in l), "?")
        groups = []
        for line in text.splitlines():
            m = ROW.search(line)
            if m:
                groups.append({
                    "controller": m.group(1).strip(),
                    "type": m.group(2),
                    "objects": int(m.group(3)),
                    "touchable": int(m.group(4)),
                    # Untouchable AND unplaced: the player cannot get at it and
                    # it is not finished either. This is the gate.
                    "stuck": int(m.group(8)),
                    # Untouchable because it is already where it belongs. NOT a
                    # gate, and counting it as one is what made Workbench - a
                    # level droha finished holding nothing - report as fully
                    # blocked.
                    "done": int(m.group(9)),
                })
        gated = [g for g in groups if g["stuck"]]
        out[str(index)] = {"level": level, "groups": groups}
        flag = ("  <== %d group(s) STUCK" % len(gated)) if gated else ""
        print(f"[3/3] {n}/{total} index {index} {level}: "
              f"{len(groups)} group(s){flag}", flush=True)

    close_game()
    with open(REPORT, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=1)
    blocked_levels = sum(1 for v in out.values()
                         if any(g["stuck"] for g in v["groups"]))
    print("", flush=True)
    print(f"Done: {len(out)} level(s) probed, {blocked_levels} have at least "
          f"one group the game itself blocks -> {REPORT}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
