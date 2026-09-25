"""Does every level behave as the gate's paper plan assumes? One level at a time.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-forceable.py --all [--resume]
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-forceable.py [--dlc] [--only LEVELID]
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-forceable.py --recheck
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-forceable.py --groups-rest
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-forceable.py --skip-all [--resume] [--only LEVELID]

`--all` probes every level in the table; without it, the levels of the seed
tools/make-seed.py wrote (testserver/out-base, or out-dlc with --dlc). Each
level, alone, with DevTools and no server:

  1. boots it from the title and lists its controllers;
  2. forces every controller the harness would (not Pannables) and times
     LevelComplete, waiting up to FULL_WAIT;
  3. for each group asking for less than the whole level, boots again and
     forces that group alone: completing there is a level that finishes on
     one group (release_e2e.KNOWN_COMPLETES_ON).

It prints each place the level disagrees with the paper plan's model:
forcing should complete it unless it is UNFORCEABLE or KNOWN_NOTHING_TO_FORCE,
within COMPLETION_WAIT; no single group should complete it unless
KNOWN_COMPLETES_ON says so; fewer controllers than the table at load should
be a KNOWN_TABLE_GAPS level.

--skip-all calls the game's own SkipLevel on each level alone, with no run up,
and adds a `skip` field: whether the completion came inside the call, late or
never, whether LevelSkipped fired, and the loaded level's Skippable flag.

One JSON object per level goes to testserver/logs/forceability.jsonl, so a
sweep that stops resumes with --resume, and the results are the data the
harness's per-level lists are built from.
"""
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402
from harness_env import close_game                         # noqa: E402

ALL = "--all" in sys.argv
DLC = "--dlc" in sys.argv
RESUME = "--resume" in sys.argv
ONLY = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
OUT = os.path.join(e2e.REPO, "testserver", "out-dlc" if DLC else "out-base")
RESULTS = os.path.join(e2e.REPO, "testserver", "logs", "forceability.jsonl")

#: How long step 2 waits, so a completion slower than COMPLETION_WAIT is
#: measured (and reported) rather than read as none at all.
FULL_WAIT = 45

#: Restart the game this often; a session of a hundred boots drifts.
RESTART_EVERY = 20


def listing(log):
    """[(index, name, type, solved)] of the running level's controllers."""
    log.new()
    e2e.dev("controllers", 1.0)
    out = log.wait(["controllers: "], 8, 6, "the controller list")
    time.sleep(1.5)
    out += log.new()
    rows = []
    for line in out.splitlines():
        body = line.split("] ", 1)[-1].strip()
        if not body.startswith("[") or " type=" not in body:
            continue
        idx = int(body[1:body.index("]")])
        name = body[body.index("]") + 2:body.index(" type=")]
        ctype = body.split(" type=", 1)[1].split()[0]
        rows.append((idx, name, ctype, body.endswith("solved=True")))
    return rows


#: DevTools' note when the game's own win check throws. Seen on every level
#: of a sweep after a bare menu:title had followed a completion 14 times -
#: the unwind release_e2e.to_title warns about. The game is then broken, not
#: the level.
BROKEN = "win check had nothing to do"


def boot(log, index, completed):
    """Leave the last level the way the gate does, then boot this one."""
    if completed:
        e2e.to_title(log)
    else:
        e2e.dev("menu:title", settle=3.0)
    log.new()
    e2e.dev(f"boot:{index}", settle=9.0)
    return listing(log)


def force(log, names, rows, wait):
    """Force the named controllers: (seconds to LevelComplete or None,
    whether the game's win check threw)."""
    log.new()
    start = time.time()
    for idx, name, _t, solved in rows:
        if name in names and not solved:
            e2e.dev(f"solve:{idx}", 0.9)
    out = ""
    while time.time() - start < wait:
        out += log.new()
        if "LevelComplete  id=" in out:
            return round(time.time() - start), BROKEN in out
        time.sleep(0.5)
    return None, BROKEN in out


def launch(log, tag):
    log.before_launch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    got = ""
    for waited in range(0, 120, 5):
        time.sleep(5)
        got += log.new()
        if "features live" in got:
            break
        print(f"{tag} game still starting, waited {waited + 5}s", flush=True)
    time.sleep(8)
    e2e.dev("setres:1280x720", settle=3.0)


def force_passes(log, index, completed):
    """Boot and force pass after pass, the way solve_level does, so a level
    that reveals its next phase only once the current one is solved gets
    every phase: (seconds to LevelComplete or None, passes used)."""
    boot(log, index, completed)
    start = time.time()
    for n in range(1, 13):
        rows = listing(log)
        todo = [r for r in rows if r[2] != "Pannables" and not r[3]]
        if not todo:
            break
        log.new()
        for idx, _name, _t, _s in todo:
            e2e.dev(f"solve:{idx}", 0.9)
        out = ""
        for _ in range(10):
            out += log.new()
            if "LevelComplete  id=" in out:
                return round(time.time() - start), n
            time.sleep(0.5)
    out = ""
    while time.time() - start < FULL_WAIT + 30:
        out += log.new()
        if "LevelComplete  id=" in out:
            return round(time.time() - start), n
        time.sleep(0.5)
    return None, n


def recheck():
    """Re-probe every level whose forcing did not finish it, each in a
    fresh game, pass after pass. Rewrites its row with the answer."""
    with open(RESULTS, encoding="utf-8") as f:
        rows = [json.loads(line) for line in f if line.strip()]
    targets = [r for r in rows if r["all"] is None and r["forceable"] > 0]
    total = len(targets)
    log = e2e.Log()
    for n, row in enumerate(targets, 1):
        tag = f"[{n}/{total} {row['levelId']}]"
        close_game()
        launch(log, tag)
        took, passes = force_passes(log, row["index"], False)
        print(f"{tag} recheck: forcing pass after pass "
              + (f"completed in {took}s after {passes} pass(es)"
                 if took is not None else f"did NOT complete in {passes} pass(es)"),
              flush=True)
        row.update(all=took, passes=passes, recheck=True)
        expect = (row["levelId"] not in e2e.UNFORCEABLE
                  and row["levelId"] not in e2e.KNOWN_NOTHING_TO_FORCE)
        row["mismatches"] = [m for m in row["mismatches"]
                             if not m.startswith("forcing everything")]
        if (took is not None) != expect:
            row["mismatches"].append(
                f"forcing pass after pass "
                f"{'completed' if took is not None else 'did not complete'}"
                f", the plan expects the opposite")
        with open(RESULTS, "w", encoding="utf-8") as fh:
            fh.write("".join(json.dumps(r) + "\n" for r in rows))
    close_game()
    left = [r for r in targets if r["mismatches"]]
    print(f"Done: {total} level(s) rechecked, {len(left)} still disagree with "
          f"the plan", flush=True)
    return 1 if left else 0


def groups_rest():
    """For every level with more than one group: force each group not yet
    tested alone, and record the controllers' order at load - the order the
    harness forces them in. Both are what the paper plan needs to know which
    parts a level that finishes on one group leaves behind."""
    e2e._load_names()
    names = e2e._NAMES["levels"]
    with open(RESULTS, encoding="utf-8") as f:
        rows = [json.loads(line) for line in f if line.strip()]
    targets = [r for r in rows
               if len((names.get(r["levelId"]) or {}).get("parts") or {}) > 1
               and "order" not in r]
    total = len(targets)
    log = e2e.Log()
    launch(log, f"[0/{total}]")
    completed = False
    for n, row in enumerate(targets, 1):
        tag = f"[{n}/{total} {row['levelId']}]"
        if n > 1 and n % RESTART_EVERY == 1:
            close_game()
            launch(log, tag)
            completed = False
        parts = names[row["levelId"]]["parts"]
        rest = [p for p in parts.values() if p["display"] not in row["groups"]]
        order = None
        for k, part in enumerate(rest or [None], 1):
            listed = boot(log, row["index"], completed)
            if order is None:
                order = [name for _i, name, _t, _s in sorted(listed)]
            if part is None:
                completed = False
                break
            alone, _broken = force(log, set(part.get("members") or []), listed,
                                   e2e.COMPLETION_WAIT + 4)
            completed = alone is not None
            row["groups"][part["display"]] = alone
            print(f"{tag} group {k}/{len(rest)} {part['display']} alone "
                  + (f"COMPLETED it in {alone}s" if alone is not None
                     else "did not complete it"), flush=True)
        row["order"] = order
        with open(RESULTS, "w", encoding="utf-8") as fh:
            fh.write("".join(json.dumps(r) + "\n" for r in rows))
    close_game()
    print(f"Done: {total} level(s) completed group by group", flush=True)
    return 0


#: How long --skip-all watches for a completion after calling SkipLevel.
SKIP_WAIT = 15


def skip_once(log):
    """DevTools `skip` on the level that is up: {when: inside|late|never|threw,
    delay, levelSkipped, skippable, allowPause, randomizable}. DevTools logs a
    line before and after SkipLevel, so the order of LevelComplete against
    them says whether the game completed the skip inside the call."""
    log.new()
    e2e.dev("skip", 0.0)
    text, t_ret, t_done = "", None, None
    end = time.time() + SKIP_WAIT
    while time.time() < end:
        chunk = log.new()
        if chunk:
            text += chunk
            if t_ret is None and "skip: SkipLevel returned" in text:
                t_ret = time.time()
            if t_done is None and "LevelComplete  id=" in text:
                t_done = time.time()
        if t_done is not None and time.time() - t_done > 2:
            break
        time.sleep(0.25)
    ret = text.find("skip: SkipLevel returned")
    done = text.find("LevelComplete  id=")
    if done >= 0:
        when = "inside" if ret < 0 or done < ret else "late"
    else:
        when = "never" if ret >= 0 else "threw"
    flags = {}
    for line in text.splitlines():
        if " skippable=" in line and "skip: " in line:
            for part in line.split()[1:]:
                if "=" in part:
                    key, value = part.split("=", 1)
                    flags[key] = value
    return {"when": when,
            "delay": (round(t_done - t_ret, 1)
                      if when == "late" and t_ret and t_done else None),
            "levelSkipped": "LevelSkipped  id=" in text,
            "skippable": flags.get("skippable"),
            "allowPause": flags.get("allowPause"),
            "randomizable": flags.get("randomizable")}


def skip_all():
    """--skip-all: the game's own SkipLevel on every level alone, with no run
    up (AutoConnect off, so the mod's skip gate stands aside). Writes a
    `skip` field into each row; --resume skips rows that have one."""
    from harness_env import Environment
    with open(RESULTS, encoding="utf-8") as f:
        rows = [json.loads(line) for line in f if line.strip()]
    targets = [r for r in rows if not (RESUME and "skip" in r)]
    if ONLY:
        targets = [r for r in targets if r["levelId"] == ONLY]
    total = len(targets)
    counts = {}
    with Environment("skip-all") as env:
        env.configure(AutoConnect="false")
        log = e2e.Log()
        launch(log, f"[0/{total}]")
        completed, since = False, 0
        for n, row in enumerate(targets, 1):
            tag = f"[{n}/{total} {row['levelId']}]"
            if since >= RESTART_EVERY:
                print(f"{tag} restarting the game: {since} levels since the last launch",
                      flush=True)
                close_game()
                launch(log, tag)
                completed, since = False, 0
            since += 1
            boot(log, row["index"], completed)
            result = skip_once(log)
            completed = result["when"] in ("inside", "late")
            row["skip"] = result
            counts[result["when"]] = counts.get(result["when"], 0) + 1
            print(f"{tag} skip: {result['when']}"
                  + (f" after {result['delay']}s" if result["delay"] else "")
                  + f", LevelSkipped={result['levelSkipped']}"
                  + f", skippable={result['skippable']}"
                  + f", randomizable={result['randomizable']}", flush=True)
            with open(RESULTS, "w", encoding="utf-8") as fh:
                fh.write("".join(json.dumps(r) + "\n" for r in rows))
        close_game()
    summary = ", ".join(f"{v} {k}" for k, v in sorted(counts.items()))
    print(f"Done: {total} level(s) skipped alone: {summary}", flush=True)
    return 0


def main():
    if "--recheck" in sys.argv:
        return recheck()
    if "--groups-rest" in sys.argv:
        return groups_rest()
    if "--skip-all" in sys.argv:
        return skip_all()
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        raw = {l["levelId"]: l for l in json.load(f)["levels"]}
    e2e._load_names()
    names = e2e._NAMES["levels"]

    if ALL:
        levels = sorted((raw[lid]["levelIndex"], lid) for lid in names
                        if lid in raw)
        reqs = None
        print(f"[0/{len(levels)}] every level in the table", flush=True)
    else:
        zips = [f for f in os.listdir(OUT) if f.endswith(".zip")]
        if not zips:
            raise SystemExit(f"no seed in {OUT} - run tools/make-seed.py first")
        plan = e2e.read_plan(OUT, zips[0])
        levels = list(plan["slots"])
        reqs = plan["requirements"]
        print(f"[0/{len(levels)}] seed {zips[0]} in "
              f"{os.path.relpath(OUT, e2e.REPO)}", flush=True)
    if ONLY:
        levels = [(i, lid) for i, lid in levels if lid == ONLY]

    done = set()
    if RESUME and os.path.isfile(RESULTS):
        with open(RESULTS, encoding="utf-8") as f:
            done = {json.loads(line)["levelId"] for line in f if line.strip()}
    elif ALL and not ONLY:
        open(RESULTS, "w").close()
    todo = [(i, lid) for i, lid in levels if lid not in done]
    total = len(levels)

    log = e2e.Log()
    launch(log, f"[{len(done)}/{total}]")
    state = {"completed": False, "since": 0}

    def restart(tag, why):
        print(f"{tag} restarting the game: {why}", flush=True)
        close_game()
        launch(log, tag)
        state.update(completed=False, since=0)

    def trial(tag, index, pick, wait):
        """Boot, force what `pick(rows)` names, and time it. A throwing win
        check means the GAME is broken: restart and try once more."""
        for attempt in (1, 2):
            rows = boot(log, index, state["completed"])
            names = pick(rows)
            took, broken = (force(log, names, rows, wait) if names
                            else (None, False))
            state["completed"] = took is not None
            if not broken or attempt == 2:
                return rows, names, took, broken
            restart(tag, "the game's win check threw")
        return rows, names, took, broken

    problems = []
    for n, (index, lid) in enumerate(todo, len(done) + 1):
        tag = f"[{n}/{total} {lid}]"
        if state["since"] >= RESTART_EVERY:
            restart(tag, f"{state['since']} levels since the last launch")
        state["since"] += 1

        entry = names.get(lid) or {}
        display = entry.get("display", lid)
        parts = entry.get("parts") or {}
        level = raw.get(lid) or {}
        bypassed = set(level.get("bypassedAbilities") or [])
        own = {p["display"]: set(p.get("abilities") or []) - bypassed
               for p in parts.values()}
        if reqs is not None:
            whole = set(reqs.get(f"{display} - Beaten") or [])
            own = {d: set(reqs.get(f"{display} - {d}") or a)
                   for d, a in own.items()}
        else:
            whole = set().union(*own.values()) if own else set()
            whole |= set(level.get("extraAbilities") or [])
            whole -= bypassed
        found = []

        rows, forceable, took, broken = trial(
            tag, index,
            lambda rs: {r[1] for r in rs if r[2] != "Pannables"}, FULL_WAIT)
        at_load = len(rows)
        print(f"{tag} step 1/3: {at_load} controller(s), "
              f"{len(forceable)} forceable", flush=True)
        expect_done = (lid not in e2e.UNFORCEABLE
                       and lid not in e2e.KNOWN_NOTHING_TO_FORCE)
        print(f"{tag} step 2/3: forcing everything "
              + (f"completed in {took}s" if took is not None
                 else "did NOT complete")
              + (" (the win check threw twice)" if broken else "")
              + f"; the plan expects it to "
              + ("complete" if expect_done else "need a Skip"), flush=True)
        if (took is not None) != expect_done:
            found.append(f"forcing everything "
                         f"{'completed' if took is not None else 'did not complete'}"
                         f", the plan expects the opposite")
        elif took is not None and took > e2e.completion_wait(lid):
            found.append(f"completes {took}s after forcing, past its wait "
                         f"({e2e.completion_wait(lid)}s)")
        table = [c for c in level.get("controllers", [])
                 if not c.get("notALocation")]
        if at_load < len(table) and lid not in e2e.KNOWN_TABLE_GAPS:
            found.append(f"{at_load} controller(s) at load, the table lists "
                         f"{len(table)} - not on KNOWN_TABLE_GAPS")

        groups = {}
        if len(parts) > 1:
            smaller = [p for p in parts.values() if own[p["display"]] < whole]
            for k, part in enumerate(smaller, 1):
                members = set(part.get("members") or [])
                _rows, _n, alone, _b = trial(
                    tag, index, lambda rs, m=members: m,
                    e2e.COMPLETION_WAIT + 4)
                groups[part["display"]] = alone
                expected = part["display"] in e2e.KNOWN_COMPLETES_ON.get(lid, ())
                print(f"{tag} step 3/3: group {k}/{len(smaller)} "
                      f"{part['display']} alone "
                      + (f"COMPLETED it in {alone}s" if alone is not None
                         else "did not complete it"), flush=True)
                if (alone is not None) != expected:
                    found.append(f"forcing {part['display']} alone "
                                 f"{'completes' if alone is not None else 'does not complete'}"
                                 f" the level; KNOWN_COMPLETES_ON says "
                                 f"{'it does' if expected else 'it does not'}")
            if not smaller:
                print(f"{tag} step 3/3: every group needs the whole level",
                      flush=True)
        else:
            print(f"{tag} step 3/3: one group, nothing to split", flush=True)

        for f in found:
            print(f"{tag} MISMATCH {f}", flush=True)
        problems += [f"{lid}: {f}" for f in found]
        with open(RESULTS, "a", encoding="utf-8") as fh:
            fh.write(json.dumps({"levelId": lid, "index": index,
                                 "atLoad": at_load,
                                 "table": len(table), "forceable": len(forceable),
                                 "all": took, "broken": broken, "groups": groups,
                                 "mismatches": found}) + "\n")

    close_game()
    print("", flush=True)
    for p in problems:
        print(f"  MISMATCH {p}", flush=True)
    print(f"Done: {len(todo)} level(s) probed, {len(problems)} mismatch(es) "
          f"with the paper plan", flush=True)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
