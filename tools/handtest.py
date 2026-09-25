"""Set up one hand-test of "can this actually be reached", and get out of the way.

    py -3.13 tools/handtest.py --serve         stand up the session, ONCE, first
    py -3.13 tools/handtest.py --auto          collider census, all cases (see below)
    py -3.13 tools/handtest.py                 list the cases
    py -3.13 tools/handtest.py <case>          set one up and leave the game running
    py -3.13 tools/handtest.py <case> --report re-print the question and read `locks`

RUN --serve BEFORE ANYTHING ELSE. The mod auto-connects on launch, and it will
happily reconnect to whatever server it used last - which on 2026-09-22 was
still the MultiServer holding the seed that had already softlocked. A level
booted underneath a dead run is not a clean measurement. --serve stops whatever
holds the port, generates a fresh seed, clears the old session's saves, starts
a detached MultiServer and points the mod at it, so the next launch connects to
something valid without anyone typing into the pane.

The test seed deliberately runs with ability_locks OFF. The mod then dims
nothing at all, which leaves `freeze:` as the only thing taking anything away -
and a measurement with one variable is the entire point. A seed where the mod
is also dimming would answer a different question badly.

WHAT IS BEING ASKED. Whether a player can PHYSICALLY reach a location. Three
sources have been tried and all three failed:

  * `dependsOn`, harvested from the game, which is silent about edges the game
    does not express that way - `Tupperware Nesting - Lids` declares
    ['Containers'], sits outside the level's own phase list, and cannot be
    touched without Stacking. That gap ended a run on 2026-09-21.
  * the sweep's drawer containment, checked on 2026-09-22 against the four
    levels the 0.3.0 hand audit settled and wrong in BOTH directions - it
    misses Bathroom Drawer's Bottle and Indexable, misses Workbench entirely,
    and adds five chalk jigsaws the audit excluded.
  * the release harness, which force-solves by setting a flag and therefore
    bypasses the physics it is supposed to measure.

--auto WAS AN ATTEMPT TO AUTOMATE THIS AND IT DOES NOT WORK. Measured
2026-09-22, and kept here because the negative result is worth more than the
attempt was. The reasoning was: a pointer can hit an object when the GameObject
is active and its collider is enabled, both are readable, a drawer is shut at
boot, so boot -> measure -> solve the opener -> measure again would name what
the drawer gates. It returns IDENTICAL numbers before and after on every case,
including the control where the answer is known:

    tooldrawer   Draggables    shut 47 -> open 47
                 Containables  shut  8 -> open  8

The game does not gate a drawer's contents by collider. The chain is: no Drawer
ability -> the MOD dims DrawerController -> the drawer cannot be OPENED -> the
contents cannot be ARRANGED. The contents stay clickable throughout. Freezing
the controller disables exactly ONE collider, the handle, and every content
object stays touchable - correctly, because touching them was never the thing
being prevented.

So the gate here is FUNCTIONAL, not physical, and no collider census can see
it. --auto remains useful as a census of what is touchable; it is not evidence
about gating and must not be read as any.

WHICH MEANS A HUMAN REALLY IS REQUIRED for these, and the earlier version of
this file was right before it was talked out of it. The question is whether an
arrangement can be COMPLETED with the opener unusable, and completing an
arrangement is gameplay. Also DLC2 Boss, whose `Locks` appears in no table
anywhere, so there is nothing to freeze or solve.

THE CONTROL IS NOT OPTIONAL. `tooldrawer` is the level where the hand audit and
the sweep agree and levels.json already carries the edges, so the answer is
known before the run starts. If the method cannot reproduce a known answer it
is not measuring what it claims to, and nothing else it reports counts.

Answers go in docs/verification-log.md with the date. "Looks fine" is not an
answer; "the blue chalk was touchable with the drawer shut" is.
"""
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                              # noqa: E402


class Case:
    def __init__(self, key, level_id, index, freeze, question, why,
                 expected=None):
        self.key = key
        self.level_id = level_id
        self.index = index
        #: Controllers to freeze, i.e. what an ability lock would take away.
        self.freeze = freeze
        self.question = question
        self.why = why
        #: Only set where the answer is already known. That is the point of a
        #: control: a method that cannot reproduce a known answer is not
        #: measuring what it claims to.
        self.expected = expected


CASES = [
    Case(
        "tooldrawer", "NeatStreak_Tool Drawer", 1021, ["Drawer Controller"],
        "With the drawer frozen, can you place ANY of the 47 draggables or "
        "the 8 containables?",
        "THE CONTROL. The 0.3.0 hand audit and the 2026-09-22 sweep agree that "
        "both groups are inside this drawer, and levels.json already carries "
        "the dependsOn edges. So the answer is known before you start.",
        expected="No. If you can, this method is measuring the wrong thing "
                 "and every other case here is void."),
    Case(
        "chalk", "NeatStreak_Paper Plane Supplies", 1020, ["Drawer Controller"],
        "With the drawer frozen, can you complete the BLUE, GREEN, MINT, "
        "YELLOW and PINK chalk jigsaws? And separately, can you complete "
        "PURPLE and RED?",
        "THE REAL QUESTION. Two sources disagree and both are plausible. "
        "test_generation.test_a_drawer_cannot_be_emptied_before_it_opens "
        "deliberately EXCLUDES the chalk jigsaws, its comment saying they are "
        "'assembled on the desk'. The sweep says five of the seven start "
        "inside the drawer - and that it is five rather than all seven is "
        "itself evidence, because a blanket mistake would not split them. "
        "Each jigsaw declares ['Jigsaw'] only. If the five need the drawer "
        "open, five part locations understate their requirement."),
    Case(
        "boss", "DLC2 Boss", 1231, [],
        "Play past the gem puzzle to the locks and keys. Can you move the "
        "keys? Then send the `controllers` command and say what it lists.",
        "NOTHING IS FROZEN HERE, because there is nothing recorded to freeze. "
        "The level registers a controller called `Locks` that appears in "
        "NEITHER levels.json NOR docs/data/controller-classes.tsv - its only "
        "record anywhere is a CONTROLLER MISMATCH line in a play log. So this "
        "case is not a test, it is a measurement: what gates that stage, and "
        "what does the game call it."),
    Case(
        "bathroom", "NeatStreak_Bathroom Drawer", 1022, ["Drawer Controller"],
        "With the drawer frozen, can you place the bottle?",
        "CONFIRMATION ONLY, run it if there is time. levels.json already has "
        "the right edges here - Bottle Containables and Indexable both "
        "dependsOn Drawer Controller - so there is no bug to find. It is "
        "worth doing because the SWEEP missed both, and knowing the sweep "
        "under-reports is what stops anyone trusting it later."),
    Case(
        "workbench", "Workbench", 50, ["ToolsController"],
        "With the tools frozen, can you still hang the 21 draggables on "
        "their targets?",
        "CONFIRMATION ONLY. Workbench carries bypassedAbilities ['Drawer'], "
        "which is the table saying somebody already established this needs no "
        "Drawer. Worth re-checking because the sweep records no drawer here "
        "at all - its opener is a HangingToolsController, not a Drawer "
        "component, which is a whole class of opener the containment data "
        "cannot see."),
]

BY_KEY = {c.key: c for c in CASES}


#: Where the hand-test session lives. Its own folder so nothing here can be
#: confused with the gate's out-e2e or with a real playthrough.
YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-handtest")
OUT_DIR = os.path.join(e2e.REPO, "testserver", "out-handtest")

#: Fixed, so re-running --serve gives the same world and a half-finished
#: session can be resumed rather than restarted.
SEED = "20260922"

YAML = f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 40
  levels_to_beat: 40
  pack_size: 10
  # OFF ON PURPOSE. With locks off the mod dims nothing, so `freeze:` is the
  # only thing removing anything from the level and the measurement has one
  # variable. This is a diagnostic session, not a playthrough.
  ability_locks: false
  starting_abilities: 0
  # A cat resetting a puzzle mid-answer would be indistinguishable from the
  # thing being tested.
  cat_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  cupboards_and_drawers: true
  seeing_stars: true
"""


def free_the_port():
    """Stop whatever is serving on the AP port, and say what it was.

    NOT a nicety. e2e.Server documents the failure this prevents: a stale
    MultiServer keeps the port, the new one prints its usual hosting line
    BEFORE failing to bind, and everything downstream quietly talks to the old
    multiworld. On 2026-09-22 the old one was still serving the seed that had
    already softlocked, which is exactly the session this test must not use.
    """
    if not e2e.port_open():
        return

    pid = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"Get-NetTCPConnection -LocalPort {e2e.PORT} -State Listen "
         "-ErrorAction SilentlyContinue | Select-Object -First 1 "
         "-ExpandProperty OwningProcess"],
        capture_output=True, text=True).stdout.strip()
    if not pid.isdigit():
        raise SystemExit(f"port {e2e.PORT} is in use and the owner could not "
                         "be identified - stop it by hand")

    what = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"(Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}')"
         ".CommandLine"],
        capture_output=True, text=True).stdout.strip()
    print(f"      stopping pid {pid} on port {e2e.PORT}", flush=True)
    print(f"        {what[:160]}", flush=True)
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    f"Stop-Process -Id {pid} -Force"], capture_output=True)
    for _ in range(20):
        if not e2e.port_open():
            return
        time.sleep(0.5)
    raise SystemExit(f"port {e2e.PORT} is still held after stopping pid {pid}")


def forget_the_old_session():
    """Clear the saves that would restore the previous run.

    The mod writes save_ap_<slot>_<seed> and remembers the last session in
    alttl-last-session.json. Leaving those means the next launch reconnects
    with someone else's progress applied on top - which is the thing this
    whole setup exists to stop.

    The CAMPAIGN save is not touched. SaveRedirect keeps a run out of it by
    construction and nothing here goes near it.
    """
    removed = []
    for name in sorted(os.listdir(e2e.SAVE_DIR)):
        if name.startswith("save_ap_") or name == "alttl-last-session.json":
            os.remove(os.path.join(e2e.SAVE_DIR, name))
            removed.append(name)
    print(f"      cleared {len(removed)} old session file(s)"
          + (f": {', '.join(removed[:4])}" if removed else ""), flush=True)


def generate():
    """A fresh seed for the session, in its own folder."""
    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))

    with open(os.path.join(YAML_DIR, "handtest.yaml"), "w",
              encoding="utf-8") as fh:
        fh.write(YAML)

    result = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT_DIR, "--seed", SEED],
        cwd=e2e.AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if result.returncode != 0:
        print(result.stdout[-2000:], flush=True)
        print(result.stderr[-2000:], flush=True)
        raise SystemExit("generation failed")

    zips = [f for f in os.listdir(OUT_DIR) if f.endswith(".zip")]
    if not zips:
        raise SystemExit("generation produced no seed")
    return os.path.join(OUT_DIR, zips[0])


def serve(seed_path):
    """Start MultiServer DETACHED, so it outlives this process.

    The whole point is that the game connects on its NEXT launch, which is
    after this tool has exited. A child that dies with the parent would leave
    the mod retrying against nothing.
    """
    logs = os.path.join(e2e.REPO, "testserver", "logs")
    os.makedirs(logs, exist_ok=True)
    out = open(os.path.join(logs, "handtest-server.log"), "w")
    err = open(os.path.join(logs, "handtest-server.err"), "w")

    flags = 0
    if os.name == "nt":
        flags = (getattr(subprocess, "DETACHED_PROCESS", 0)
                 | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))

    proc = subprocess.Popen(
        [sys.executable, "MultiServer.py", "--port", str(e2e.PORT), seed_path],
        cwd=e2e.AP, stdout=out, stderr=err, stdin=subprocess.DEVNULL,
        creationflags=flags,
        # Without this MultiServer PROMPTS to install a drifted requirement
        # and dies on EOF, which looks exactly like a refused port.
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))

    for _ in range(60):
        if e2e.port_open():
            return proc.pid
        if proc.poll() is not None:
            raise SystemExit("MultiServer exited before binding; see "
                             "testserver/logs/handtest-server.err")
        time.sleep(1)
    raise SystemExit("MultiServer never opened the port")


def setup_session():
    print("[1/5] freeing the Archipelago port", flush=True)
    free_the_port()

    print("[2/5] closing the game if it is up", flush=True)
    from harness_env import close_game
    close_game()

    print("[3/5] forgetting the previous session", flush=True)
    forget_the_old_session()

    print("[4/5] generating the diagnostic seed", flush=True)
    seed_path = generate()
    print(f"      {os.path.relpath(seed_path, e2e.REPO)}", flush=True)

    print("[5/5] starting the server and pointing the mod at it", flush=True)
    pid = serve(seed_path)
    e2e.write_config()
    e2e.write_devtools_config()

    print("", flush=True)
    print(f"  server pid {pid} on localhost:{e2e.PORT}, slot {e2e.SLOT!r}",
          flush=True)
    print(f"  log: testserver/logs/handtest-server.log", flush=True)
    print("  the mod will auto-connect on the next launch - nothing to type",
          flush=True)
    print("  ability locks are OFF, so the mod dims nothing and `freeze:` is "
          "the only variable", flush=True)
    print("", flush=True)
    print(f"Done: session ready. Next: py -3.13 tools/handtest.py tooldrawer",
          flush=True)
    return 0


def read_reachable(log, settle=2.0):
    """Send `reachable` and parse the table it logs.

    Returns {controller: touchable}. Only the touchable column, because that
    is the question: how many of this group's objects could a pointer hit.
    """
    e2e.dev("reachable", settle=settle)
    text = log.new()
    for _ in range(10):
        if "reachable: done" in text:
            break
        time.sleep(0.5)
        text += log.new()

    out = {}
    for line in text.splitlines():
        if "reachable:   " not in line:
            continue
        body = line.split("reachable:   ", 1)[1]
        cells = body.split("\t")
        if len(cells) < 4:
            continue
        try:
            out[cells[0].strip()] = int(cells[3])
        except ValueError:
            continue
    return out


def auto():
    """Measure what the opener actually gates, without asking anyone.

    THE EXPERIMENT, and it is the whole reason this stopped being a chore for
    droha. A pointer can hit an object when the GameObject is active and its
    collider is enabled, and both are readable. A drawer is SHUT when the level
    boots. So:

        boot -> reachable        what you can touch with the drawer shut
        solve the opener         the drawer opens
        reachable                what you can touch now

    Anything that gains touchable objects between the two was gated by the
    opener. That is precisely the dependsOn edge levels.json is missing, and
    nobody had to drag anything.

    WHAT IT STILL CANNOT SAY: whether a group that IS touchable can be
    completed. Reaching a piece and finishing an arrangement are different
    questions, and only the first one is physics.
    """
    print("[1/2] making sure the game is up", flush=True)
    if not launch():
        return 1

    log = e2e.Log()
    log.new()
    results = []

    for n, case in enumerate(CASES, start=1):
        print(f"[2/2] case {n}/{len(CASES)}: {case.key} "
              f"({case.level_id})", flush=True)
        e2e.dev(f"boot:{case.index}", settle=7.0)
        log.new()
        shut = read_reachable(log)

        if not case.freeze:
            results.append((case, shut, None))
            print(f"        no opener recorded; touchable at boot: "
                  f"{sum(shut.values())} object(s) over {len(shut)} group(s)",
                  flush=True)
            continue

        for opener in case.freeze:
            e2e.dev(f"solve:{opener}", settle=4.0)
        log.new()
        opened = read_reachable(log)
        results.append((case, shut, opened))

        gained = {k: opened.get(k, 0) - v for k, v in shut.items()
                  if opened.get(k, 0) > v}
        gained.update({k: v for k, v in opened.items()
                       if k not in shut and v > 0})
        print(f"        shut: {sum(shut.values())} touchable; "
              f"open: {sum(opened.values())} touchable", flush=True)
        if gained:
            for k, v in sorted(gained.items()):
                print(f"          GATED BY {case.freeze[0]}: {k} (+{v})",
                      flush=True)
        else:
            print(f"          nothing gained - the opener gates nothing "
                  f"measurable here", flush=True)

    print("", flush=True)
    print("=" * 72, flush=True)
    print("  WHAT THE OPENER ACTUALLY GATES", flush=True)
    print("=" * 72, flush=True)
    for case, shut, opened in results:
        print(f"\n  {case.key}  {case.level_id}", flush=True)
        if opened is None:
            print("     no opener recorded - see the case notes", flush=True)
            for k, v in sorted(shut.items()):
                print(f"     {k:34} touchable {v}", flush=True)
            continue
        for k in sorted(set(shut) | set(opened)):
            a, b = shut.get(k, 0), opened.get(k, 0)
            mark = "  <== GATED" if b > a else ""
            print(f"     {k:34} shut {a:3} -> open {b:3}{mark}", flush=True)

    # Close it. The measurement is finished and a game left running on a
    # booted level is just a window in someone's way.
    from harness_env import close_game
    close_game()

    print("", flush=True)
    print("Done: measured every case, game closed. Read the GATED rows - each "
          "one is a dependsOn edge levels.json should have.", flush=True)
    return 0


def running():
    out = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "(Get-Process -Name 'A Little To The Left' "
         "-ErrorAction SilentlyContinue | Measure-Object).Count"],
        capture_output=True, text=True).stdout.strip()
    return out.isdigit() and int(out) > 0


def launch():
    """Start the game and wait for DevTools, if it is not already up."""
    if running():
        print("      the game is already running; reusing it", flush=True)
        return True

    log = e2e.Log()
    log.before_launch()
    what, _windowed = e2e.describe_display()
    print(f"      {what}", flush=True)
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)

    got = ""
    end = time.time() + 180
    while time.time() < end:
        got += log.new()
        if "ALTTL dev tools loaded" in got:
            print("      DevTools loaded", flush=True)
            time.sleep(15)          # the level manager is not up immediately
            return True
        time.sleep(1.0)
    print("FAIL: DevTools never loaded", flush=True)
    return False


def show(case):
    print("", flush=True)
    print("=" * 72, flush=True)
    print(f"  {case.key.upper()}  -  {case.level_id}  (index {case.index})",
          flush=True)
    print("=" * 72, flush=True)
    print("", flush=True)
    for line in case.why.split(". "):
        if line.strip():
            print(f"  {line.strip().rstrip('.')}.", flush=True)
    print("", flush=True)
    if case.freeze:
        print(f"  FROZEN (as an ability lock would): "
              f"{', '.join(case.freeze)}", flush=True)
    else:
        print("  FROZEN: nothing - see above", flush=True)
    print("", flush=True)
    print(f"  QUESTION: {case.question}", flush=True)
    if case.expected:
        print("", flush=True)
        print(f"  EXPECTED: {case.expected}", flush=True)
    print("", flush=True)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    report = "--report" in sys.argv

    if "--serve" in sys.argv:
        return setup_session()

    if "--auto" in sys.argv:
        return auto()

    if not args:
        print("Hand-tests, most important first. Run the CONTROL first.",
              flush=True)
        print("", flush=True)
        for case in CASES:
            tag = "  [CONTROL]" if case.expected else ""
            print(f"  {case.key:12} {case.level_id:32} index {case.index}{tag}",
                  flush=True)
        print("", flush=True)
        print("Done: py -3.13 tools/handtest.py <case>", flush=True)
        return 0

    key = args[0]
    if key not in BY_KEY:
        print(f"unknown case {key!r}; one of: {', '.join(BY_KEY)}", flush=True)
        return 2
    case = BY_KEY[key]

    if report:
        show(case)
        e2e.dev("controllers", settle=1.0)
        e2e.dev("locks", settle=1.0)
        print("  sent `controllers` and `locks` - read them in the BepInEx log",
              flush=True)
        print(f"Done: {case.key} question re-printed", flush=True)
        return 0

    print(f"[1/3] making sure the game is up", flush=True)
    if not launch():
        return 1

    print(f"[2/3] booting {case.level_id} (index {case.index})", flush=True)
    e2e.dev(f"boot:{case.index}", settle=6.0)

    print(f"[3/3] freezing {len(case.freeze)} controller(s)", flush=True)
    for controller in case.freeze:
        e2e.dev(f"freeze:{controller}", settle=1.5)
    e2e.dev("locks", settle=1.0)

    show(case)
    print("  The game is left RUNNING. Play it, then say what happened.",
          flush=True)
    print("  Nothing here writes to your campaign save - boot: loads a level "
          "directly and no run is active.", flush=True)
    print("  To undo the freeze without rebooting, send `freeze:off`. Booting "
          "another case reloads the level and clears it anyway.", flush=True)
    print("  If a frozen group still looks movable, check `locks` in the log "
          "first - Containables, Stickables and StackablesY keep objects "
          "OUTSIDE ManagedObjects and freeze only walks that list.",
          flush=True)
    print("", flush=True)
    print(f"Done: {case.key} is set up and waiting", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
