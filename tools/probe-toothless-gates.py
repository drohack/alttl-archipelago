"""Which ability gates do not actually gate anything?

THE SHAPE, found by hand on 2026-09-19. Books 3 has a Shuffleables group of
17 books (Swapping) and a Draggables group over the SAME 17 books. Draggables
is baseline - no ability gates it - and AbilityLocks merges per object with
UNLOCKED WINNING, so the baseline group frees every object the gated group was
meant to hold. Holding no abilities at all, nothing on the level was dimmed,
and force-solving it filed `Books 3 - Design (Shuffle)` - a location the logic
says needs Swapping.

That is a live leak rather than a harness artifact: nothing was locked, so a
player could have done it by hand. Spoons has the same shape and was recorded
as a one-off. This counts how many more there are.

THE MEASUREMENT. Connect a run holding ZERO abilities, so every gated class is
locked, then open each level and ask the game what the mod actually did.

DECIDED ON `blocked`, NOT `dimmed`, and the first run of this probe got that
wrong. `dimmed` is read back off the renderer colour, so an object with no
renderer can never count as dimmed however perfectly it is locked - Mirror's
CandleStateController (1 object, shared=0) and PawPrints' three Clearables
groups were all reported as toothless on that basis and none of them is.
`blocked` counts !Interactable || PreventSelection, which is precisely what
the merge leak undoes: when a baseline controller shares an object, the dimmer
sets Interactable back to TRUE and blocked falls to zero. Books 3, the case
this probe was written for, has blocked=0 as well as dimmed=0.

`dimmed` is still recorded, because a gate that blocks without dimming is a
different bug - a locked object with nothing on screen to say so.

Asked of the running game rather than computed from the tables - the tables are
what is under suspicion, and levels.json records object COUNTS per controller,
not which objects, so the sharing cannot be derived offline at all.

DLC levels are skipped unless --dlc is passed and Steam is running; without the
entitlement they will not open and would be recorded as false negatives.

    py -3.13 tools/probe-toothless-gates.py [--dlc]
"""
import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import close_game, ensure_no_steam_relaunch

REPO = e2e.REPO
OUT = os.path.join(REPO, "testserver", "out-abilitytest")
CMDS = os.path.join(REPO, "testserver", "abilitytest-cmds.txt")
SERVER_LOG = os.path.join(REPO, "testserver", "abilitytest-server.log")
REPORT = os.path.join(REPO, "docs", "toothless-gates.md")

WANT_DLC = "--dlc" in sys.argv

ROW = re.compile(
    r"\[(\d+)\] (.*?) type=(\S+) objects=(\d+) blocked=(\d+) dimmed=(\d+) "
    r"shared=(\d+) norenderer=(\d+) solved=(\S+)")


def gated_classes():
    """Controller class -> the ability that unlocks it."""
    with open(os.path.join(REPO, "apworld", "alttl", "data", "abilities.json"),
              encoding="utf-8") as f:
        table = json.load(f)

    owner = {}
    for group in ("abilities", "dlcAbilities"):
        for ability, classes in (table.get(group) or {}).items():
            for cls in classes:
                owner[cls] = ability
    return owner


def levels():
    with open(os.path.join(REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        data = json.load(f)

    rows = []
    for lv in data["levels"]:
        if len(lv["controllers"]) < 2:
            continue                      # nothing to share WITH
        if lv["source"].startswith("dlc") and not WANT_DLC:
            continue
        rows.append((lv["levelIndex"], lv["levelId"], lv["source"]))
    return rows


def read_locks(log):
    """Ask the running game what is dimmed on the open level."""
    log.new()
    e2e.dev("locks", 1.0)
    out = log.wait(["locks: "], 20, 6, "the lock report")
    time.sleep(1.5)                       # the header arrives before the rows
    out += log.new()

    found = []
    for line in out.splitlines():
        m = ROW.search(line)
        if m:
            found.append({
                "name": m.group(2).strip(),
                "type": m.group(3),
                "objects": int(m.group(4)),
                "blocked": int(m.group(5)),
                "dimmed": int(m.group(6)),
                "shared": int(m.group(7)),
            })
    return found


def main():
    owner = gated_classes()
    todo = levels()
    print(f"-- {len(todo)} multi-controller level(s) to check "
          f"({'with' if WANT_DLC else 'without'} DLC) --", flush=True)

    close_game()
    seeds = sorted((f for f in os.listdir(OUT) if f.endswith(".zip")),
                   key=lambda f: os.path.getmtime(os.path.join(OUT, f)))
    if not seeds:
        sys.exit("no seed in testserver/out-abilitytest; run dragbench first")
    seed = seeds[-1]

    if not e2e.port_open():
        open(CMDS, "w", encoding="utf-8").close()
        log_file = open(SERVER_LOG, "w", encoding="utf-8")
        print(f"step 0: starting the server on {e2e.PORT}", flush=True)
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

    log = e2e.Log()
    log.before_launch()
    ensure_no_steam_relaunch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    text = log.wait(["connected. "], 150, 1, "the connection")
    if "connected. " not in text:
        sys.exit("FAIL: " + (e2e.why_no_connection(e2e.whole_log()) or "no connect"))
    time.sleep(4.0)

    toothless, holed, clean, unopened = [], [], 0, []

    for n, (index, name, source) in enumerate(todo, 1):
        head = f"[{n}/{len(todo)} {name}]"
        try:
            e2e.dev(f"boot:{index}", 8.0)
            time.sleep(1.5)               # let an opening animation settle
            rows = read_locks(log)
        except Exception as e:
            print(f"{head} step 2/2: FAILED to read: {e}", flush=True)
            unopened.append(name)
            continue

        if not rows:
            print(f"{head} step 2/2: no controllers reported, skipped",
                  flush=True)
            unopened.append(name)
            continue

        gates = [r for r in rows if r["type"] in owner]
        if not gates:
            clean += 1
            print(f"{head} step 2/2: no gated controller, fine", flush=True)
            continue

        bad = [r for r in gates if r["blocked"] < r["objects"]]
        if not bad:
            clean += 1
            print(f"{head} step 2/2: {len(gates)} gate(s), all holding",
                  flush=True)
            continue

        for r in bad:
            entry = (name, source, r["name"], r["type"], owner[r["type"]],
                     r["objects"], r["blocked"], r["dimmed"], r["shared"])
            (toothless if r["blocked"] == 0 else holed).append(entry)
            state = "NO TEETH" if r["blocked"] == 0 else "partial"
            print(f"{head} step 2/2: {state} - {r['name']} ({r['type']}, "
                  f"{owner[r['type']]}) {r['blocked']}/{r['objects']} blocked, "
                  f"{r['dimmed']} dimmed, shared={r['shared']}", flush=True)

    write_report(toothless, holed, clean, unopened, len(todo))
    close_game()
    print(f"Done: {len(toothless)} gate(s) with NO teeth, {len(holed)} partial, "
          f"{clean} level(s) clean, {len(unopened)} unreadable "
          f"-> {os.path.relpath(REPORT, REPO)}", flush=True)


def write_report(toothless, holed, clean, unopened, total):
    lines = [
        "# Ability gates that do not gate anything",
        "",
        "Generated by `tools/probe-toothless-gates.py`, holding ZERO abilities,",
        "by asking the running game what is dimmed rather than by reading the",
        "tables that are themselves under suspicion.",
        "",
        "A gated controller with `dimmed=0` has no gate at all: every object it",
        "manages is freed by some other controller on the level, because the",
        "dimmer merges per object and lets UNLOCKED WIN. The locations behind",
        "that gate are earnable by a player who does not hold the ability.",
        "",
        f"Checked {total} multi-controller level(s).",
        "",
        "## No teeth - every object freed by another controller",
        "",
    ]
    lines += table(toothless) if toothless else ["None.", ""]
    lines += ["## Partial - some objects freed", ""]
    lines += table(holed) if holed else ["None.", ""]
    if unopened:
        lines += ["## Could not be read", "",
                  "These reported no controllers, or failed to open. Nothing is",
                  "known about them either way.", ""]
        lines += [f"- {n}" for n in unopened] + [""]
    with open(REPORT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def table(rows):
    out = ["| Level | Source | Controller | Class | Ability | Objects | Blocked | Dimmed | Shared |",
           "|---|---|---|---|---|---|---|---|---|"]
    for name, source, ctrl, cls, ability, objects, blocked, dimmed, shared in sorted(rows):
        out.append(f"| {name} | {source} | {ctrl} | {cls} | {ability} | "
                   f"{objects} | {blocked} | {dimmed} | {shared} |")
    return out + [""]


if __name__ == "__main__":
    main()
