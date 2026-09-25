"""Put each scheduler bug back and check test_scheduler.py notices.

A TEST NOBODY HAS SEEN FAIL IS A TEST NOBODY HAS TESTED. test_scheduler.py
passed the moment it was written, which proves nothing on its own - the
assertions could be vacuous, or the anchor they pin could have moved. This
re-introduces each real defect, one at a time, and fails if the suite stays
green.

Every mutation below is a bug that actually shipped into a gate run on
2026-09-20 and cost a fifteen-minute cycle to find. An anchor that no longer
matches is reported as SKIP and counts as a failure: a mutation that cannot
be applied is a test whose subject has moved, which is exactly when the
suite quietly stops covering it.

    py -3.13 tools/mutate-scheduler.py
"""
import io
import shutil
import subprocess
import sys
import tempfile

import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "release_e2e.py")

MUTATIONS = [
    ("the barren guard removed",
     "    if barren.get(slot) == frozenset(held):\n        return False",
     "    if False:\n        return False"),
    ("planned order overriding attempt rotation",
     "    return (attempts.get(slot, 0), order.get(slots[slot][1], 10 ** 6), slot)",
     "    return (order.get(slots[slot][1], 10 ** 6), attempts.get(slot, 0), slot)"),
    ("stuck means slots still closed",
     "    if len(beaten) < len(slots) and not skips_out:",
     "    if open_slots < len(slots) and not skips_out:"),
    ("the skip flag never reported",
     '            return (min(stalled, key=rank),\n'
     '                    "buy a skip to release an item", True)',
     '            return (min(stalled, key=rank),\n'
     '                    "buy a skip to release an item", False)'),
    ("dlcAbilities read flat again",
     '    for per_dlc in (table.get("dlcAbilities") or {}).values():\n'
     '        for ability, classes in (per_dlc or {}).items():',
     '    for per_dlc in [(table.get("dlcAbilities") or {})]:\n'
     '        for ability, classes in (per_dlc or {}).items():'),
    ("the stall recovery never fires",
     "    if idle >= STUCK_AFTER and len(beaten) < len(slots) and not skips_out:",
     "    if False and len(beaten) < len(slots) and not skips_out:"),
    ("the stall recovery fires while the run is still moving",
     "STUCK_AFTER = 3",
     "STUCK_AFTER = 0"),
    ("idle not passed, so the recovery can never trigger",
     "credits=credits, idle=idle,",
     "credits=credits, idle=0,"),
    ("the arrow check back to always starting on slot 0",
     "        if slot_has_work(i, plan, where, held, set()):",
     "        if False:"),
    ("the stall path chasing a Skip it cannot buy",
     "    if idle >= STUCK_AFTER and len(beaten) < len(slots) and not skips_out:",
     "    if idle >= STUCK_AFTER and len(beaten) < len(slots):"),
    ("the no-candidates path chasing a Skip it cannot buy",
     "    if len(beaten) < len(slots) and not skips_out:",
     "    if len(beaten) < len(slots):"),
    ("the refusal never recorded, so the supply looks endless",
     "                skips_out = True",
     "                skips_out = False"),
    ("unbeaten slots no longer parked, so a stubborn level grinds",
     "        if not done and len(collected_locations(transcript)) == collected_before:",
     "        if False:"),
    ("parking on the FIRST miss, which abandons a cat-trapped level",
     "            if fruitless[current] >= 2:",
     "            if fruitless[current] >= 1:"),
    ("the fruitless counter never reset, so any two misses ever park it",
     "            fruitless[current] = 0",
     "            fruitless[current] = fruitless.get(current, 0)"),
    ("beaten tokens not counted as collected",
     '        for prefix in ("check: ", "beaten: "):',
     '        for prefix in ("check: ",):'),

    # --- the three the 2026-09-20 audit found by reading, not running ---
    ("--steady dead again, so a 'repeatable' run still gets cat traps",
     "cat_trap_chance: {0 if quick or steady else 25}",
     "cat_trap_chance: {0 if quick else 25}"),
    ("steady also switching the gates off, making it --quick by another name",
     "ability_locks: {'false' if quick else 'true'}",
     "ability_locks: {'false' if quick or steady else 'true'}"),
    ("the never-connected path back to five values, crashing its caller",
     "        return [], False, first, 0, first, []",
     "        return [], False, first, 0, first"),
    ("--quick advertised as a shorter run it never was",
     "Still fifteen puzzles, ",
     "three puzzles, "),
    ("quick 'shortening' the run with a local that changes nothing",
     "    if args.quick:\n        QUICK = True",
     "    if args.quick:\n        PUZZLES = 3\n        QUICK = True"),
    ("the whole DLC block dropped from the yaml",
     ') if dlc_on else ""',
     ') if False else ""'),

    # --- the surprise-Skip refusal, 2026-09-21 ---
    ("a surprise Skip bought again, draining the supply",
     "            surprise = (not forced",
     "            surprise = False and (not forced"),
    ("the stall path's legitimate Skip refused too, stranding the run",
     "            surprise = (not forced",
     "            surprise = (True or not forced"),
    ("the refusal ignoring what only a Skip can finish, as it did at 18/25",
     "                        and not only_a_skip_can_finish(\n"
     "                            current, where, collected_locations(transcript)))",
     "                        )"),
    ("the verdict reading only spends, so refusals go unreported",
     "not surprises and not bought",
     "not bought"),
    ("the surprise announced every round until nobody reads the report",
     "                if current not in surprised:",
     "                if True:"),

    # --- restored slots read from a count again, 2026-09-21 ---
    ("restored guessed from the count, so the wrong slot is marked",
     "    for i in beaten_slots(earlier, where):\n        restored.add(i)",
     "    for i in range(1):\n        restored.add(i)"),
    ("the earlier transcript never passed, so nothing is ever restored",
     "         surprises) = play(log, plan, arrow_text)",
     "         surprises) = play(log, plan)"),
    ("the count disagreement swallowed",
     "            if n != len(restored):",
     "            if False:"),
    # --- the base gate's two, 2026-09-21 ---
    ("refusing on gated again, which stalled the base gate at 1 of 8",
     "            refuse = surprise",
     "            refuse = gated or surprise"),
    ("filtering the stall path on gating again, which cost the credits",
     "        stalled = unlocked or stalled",
     "        stalled = unlocked"),
    ("the stall preference turned back into a filter",
     "        stalled = unlocked or stalled",
     "        stalled = unlocked"),
    ("the gated Skip spent immediately again, papering over the gate",
     "            defer = gated and not forced and idle < STUCK_AFTER",
     "            defer = False"),
    ("the deferral losing its escape, so a stranded ability deadlocks",
     "            defer = gated and not forced and idle < STUCK_AFTER",
     "            defer = gated and not forced"),
    ("the deferral recorded but not honoured at the spend site",
     "            if not refuse and not defer:",
     "            if not refuse:"),
    ("the no-candidates path no longer preferring an unlocked slot",
     "        if unlocked:\n"
     "            return min(unlocked, key=rank), "
     '"buy a skip to release an item", True',
     "        if False:\n"
     "            return min(unlocked, key=rank), "
     '"buy a skip to release an item", True'),
    ("every gated Skip excused, so a real paper is never reported",
     '                   if gated and reason != "last-resort"]',
     '                   if False]'),
    ("gated scanning for 'waiting on' again, which another level writes",
     '            gated = False\n'
     '            for line in chunk.splitlines():\n'
     '                if LOCKED_MARK in line:',
     '            gated = any("waiting on " in l for l in chunk.splitlines())\n'
     '            for line in []:\n'
     '                if LOCKED_MARK in line:'),
    ("the per-level marker never published, so nothing is ever gated",
     '    text += (f"\\n{LOCKED_MARK}"',
     '    text += (f"\\n{LOCKED_MARK}none" or f"\\n{LOCKED_MARK}"'),
    ("the stall path skipping a card with nothing left to find",
     "        stalled = [i for i in sorted(beaten)\n"
     "                   if i not in skipped\n"
     "                   and slots[i][1] not in skipless\n"
     "                   and has_uncollected(i, where, collected)]",
     "        stalled = [i for i in sorted(beaten) if i not in skipped\n"
     "                   and slots[i][1] not in skipless]"),
    ("the no-candidates path skipping a card with nothing left to find",
     "        stuck = [i for i in sorted(beaten)\n"
     "                 if i not in skipped\n"
     "                 and slots[i][1] not in skipless\n"
     "                 and has_uncollected(i, where, collected)]",
     "        stuck = [i for i in sorted(beaten) if i not in skipped\n"
     "                 and slots[i][1] not in skipless]"),
    ("has_uncollected always true, so an exhausted card absorbs the Skip",
     "    return any(location not in collected for location in where.get(slot, ()))",
     "    return True"),
    ("the last-resort reason claimed for every Skip",
     '                              "last-resort" if current in last_resort',
     '                              "last-resort" if True'),

    ("the verdict blind to refused gated Skips",
     "not papered and not refused_gated",
     "not papered"),
    ("tokens no longer reconciled, so a token for another slot is lost",
     "        for slot_index, token in beaten_slots(transcript, where).items():",
     "        for slot_index, token in {}.items():"),
    ("beaten_slots matching any token to every slot",
     '            if not location.endswith(" - Beaten"):\n                continue',
     "            if False:\n                continue"),

    ("the mod's word no longer final for slots it did not carry",
     '        if done and "beaten:" not in (chunk + tail) and current not in restored:',
     "        if False:"),

    # 2026-09-24: the full gate stalled at 13/15. Each row is one fact the
    # paper plan or the Skip path now depends on.
    ("paper run: an unforceable level never gives up its item to a Skip",
     "        unforced = (not buy and not got and slot not in beaten\n"
     "                    and spent < stock[\"supply\"])",
     "        unforced = False"),
    ("paper run: idle reset by any collection, not by finishing",
     "            idle = 0 if done else idle + 1\n",
     "            idle = 0 if (forced or granted) else idle + 1\n"),
    ("paper run: a revisit resetting idle",
     "                if not got and park:\n"
     "                    barren[slot] = frozenset(held)\n",
     "                if not got and park:\n"
     "                    barren[slot] = frozenset(held)\n"
     "                idle = 0\n"),
    ("paper run: a revisit forcing like an attempt",
     "            if slot in beaten and not buy:\n",
     "            if False:\n"),
    ("the round loop stopping on a stale credits flag",
     "        if \"credits: unlocked\" in transcript:\n"
     "            credits = True\n"
     "        if len(beaten) >= len(slots) and (credits or revisited >= beaten.keys()):\n",
     "        if len(beaten) >= len(slots) and (credits or revisited >= beaten.keys()):\n"),
    ("the round loop not waiting for the last token's credits",
     "            transcript += log.wait([\"credits: unlocked\"], 15, 6, \"the credits\")\n",
     "            pass\n"),
    ("the plan counting an early-completion part as forced",
     "                    or loc in CONTAINER_EXCLUDES\n"
     "                    or loc in KNOWN_EARLY_COMPLETE):",
     "                    or loc in CONTAINER_EXCLUDES):"),
    ("the Skip policy not knowing only a Skip releases that part",
     "        if location in CONTAINER_EXCLUDES or location in KNOWN_EARLY_COMPLETE:",
     "        if location in CONTAINER_EXCLUDES:"),
    ("the table audit misreading `(at N controller(s) so far)` as the level",
     "        level = level.split(\" (at \", 1)[0].strip()\n",
     ""),
    ("a pass that revealed the next phase spent from the budget",
     "        if attempt and pass_progress(last_listing, out) \\\n",
     "        if False and pass_progress(last_listing, out) \\\n"),
    ("a new controller not counted as progress",
     "    return reg_after > reg_before or solved_after > solved_before\n",
     "    return solved_after > solved_before\n"),
    ("paper run: the arrow session's slot counted beaten before any visit",
     "        restored.add(slot)\n",
     "        restored.add(slot)\n        beaten.add(slot)\n"),
    ("paper run: a replayed restored slot demanding a second token",
     "            if (slot in restored and not done and not locked_part\n",
     "            if (False and slot in restored and not done and not locked_part\n"),
    ("paper run: no end-of-attempt reconcile of tokens seen elsewhere",
     "                if tok is not None and tok in collected:\n"
     "                    beaten.add(i)\n",
     "                if False:\n"
     "                    beaten.add(i)\n"),
    ("the arrow check starting on a level forcing cannot finish",
     "        if slots[i][1] in UNFORCEABLE or slots[i][1] in KNOWN_COMPLETES_ON:\n"
     "            continue\n",
     ""),
    ("the arrow check starting on a level whose token needs an ability",
     "        if token is not None and not set(requirements.get(token) or ()) <= held:\n"
     "            continue\n",
     ""),
    ("the arrow check starting on a slot no pack has opened",
     "    for i in range(min(opening, len(slots))):\n",
     "    for i in range(len(slots)):\n"),
    ("the arrow check assuming nothing is held",
     "    held = set(plan.get(\"starting_abilities\") or ())\n",
     "    held = set()\n"),
    ("with locks off, the run choosing as if abilities still gated",
     "    if not plan.get(\"ability_locks\", True):\n        _load_ability_tables()\n        return set(_ABILITY_NAMES)\n",
     ""),
    ("the run allowed to leave its paper plan",
     "            if planned and astray:",
     "            if False:"),
    ("a visit past the plan's end let through",
     "    if visit > len(planned):\n",
     "    if visit > len(planned) + 99:\n"),
    ("paper run (production): an exhausted unforceable level never skipped",
     "            exhausted = (not done and not locked_part\n",
     "            exhausted = False and (not done and not locked_part\n"),
    ("paper run (production): a locked part no longer holds the Skip back",
     "            exhausted = (not done and not locked_part\n",
     "            exhausted = (not done\n"),
    ("paper run: a collected pack opens nothing",
     "        stock[\"packs\"] += 1\n    elif item == \"Skip\":",
     "        pass\n    elif item == \"Skip\":"),
    ("paper run: a collected Skip adds nothing to supply",
     "    elif item == \"Skip\":\n        stock[\"supply\"] += 1",
     "    elif item == \"Skip\":\n        pass"),
    ("paper run: the arrow session's slot played again as visit one",
     "    for slot in opening:\n",
     "    for slot in ():\n"),
    ("paper run: a trap on a part never resets the visit",
     "                 and any(is_part_location(loc) and placements.get(loc) == \"Cat Trap\"",
     "                 and any(is_part_location(loc) and placements.get(loc) == \"never\""),
    ("paper run: planning past the last token, when the credits open",
     "        if stop_when_all_beaten and len(beaten) >= len(slots):\n"
     "            break",
     "        pass"),
    ("a Skip sent after this visit's completion, onto the next level",
     '    if "LevelComplete " in text or "navigation: next" in text:\n'
     "        return False",
     "    if False:\n        return False"),
    ("a Skip sent without looking at which level is running",
     "    return now is not None and now[1] == index and now[2]",
     "    return True"),
    ("a bought Skip forcing the beaten level first, as on 2026-09-24",
     "        if current in beaten and current in skip_anyway:",
     "        if False:"),
    ("a Skip the game did not perform read as a spent one",
     '    if "skip: not used" in text:\n        return "not used"\n',
     ""),
    ("the harness waiting for DevTools' skip line instead of the mod's verdict",
     '                more = log.wait(list(SKIP_VERDICTS), 12, 6, "the skip")',
     '                more = log.wait(["skip:"], 12, 6, "the skip")'),
    ("the arrow session's checks not carried into the run",
     "    transcript = carried_checks(earlier) + first",
     "    transcript = first"),
    ("a Skip bought on a level the game cannot skip",
     "                   and slots[i][1] not in skipless\n"
     "                   and has_uncollected(i, where, collected)]",
     "                   and has_uncollected(i, where, collected)]"),
    ("the arrow session's log lost to play()'s launch",
     '            keep_log("arrow")\n',
     ""),
    ("a solve sent while the game was paused not warned about",
     "            if stopped:\n",
     "            if False:\n"),
    ("a late skip not waited for",
     '                if skip_outcome(more) == "waiting":',
     '                if False:'),
    ("a Skip that did nothing not stopping the run",
     '            if outcome in ("not used", "waiting"):',
     '            if outcome == "not used":'),
    ("a spent Skip on a beaten level booting straight over it",
     "        last_done = done or skip_spent\n",
     "        last_done = done\n"),
]

original = io.open(SRC, encoding="utf-8").read()

# BASELINE FIRST, or every result is a lie.
#
# A mutation counts as CAUGHT when the suite exits non-zero - and a suite
# with a SYNTAX ERROR exits non-zero for every mutation, reporting a
# perfect score while testing nothing. That happened: a stray newline in a
# string literal broke test_scheduler.py and this printed 9 of 9.
_baseline = subprocess.run(
    [sys.executable, os.path.join(HERE, "test_scheduler.py")],
    capture_output=True, text=True,
    env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"))
if _baseline.returncode != 0:
    print("the suite does not pass on UNMUTATED code - fix that first:",
          flush=True)
    print(_baseline.stdout[-800:] or _baseline.stderr[-800:], flush=True)
    sys.exit(2)
print("baseline: the suite passes unmutated", flush=True)
backup = tempfile.mktemp(suffix=".py")
shutil.copy(SRC, backup)

failures = 0
try:
    for name, old, new in MUTATIONS:
        if old not in original:
            print(f"SKIP  {name}: anchor not found - the test cannot be "
                  f"trusted until this is fixed", flush=True)
            failures += 1
            continue
        io.open(SRC, "w", encoding="utf-8", newline="\n").write(
            original.replace(old, new, 1))
        # PYTHONDONTWRITEBYTECODE, and it is not paranoia. This loop
        # rewrites release_e2e.py several times a second, and CPython
        # invalidates a cached .pyc by mtime to the SECOND - so a mutation
        # applied within the same second as the previous one can be tested
        # against stale bytecode and reported as MISSED when the suite would
        # have caught it. Seen exactly that: a mutation caught on one run and
        # missed on the next with nothing else changed.
        env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")
        r = subprocess.run(
            [sys.executable, os.path.join(HERE, "test_scheduler.py")],
            capture_output=True, text=True, env=env)
        caught = r.returncode != 0
        print(f"{'CAUGHT' if caught else 'MISSED'}  {name}", flush=True)
        if not caught:
            failures += 1
finally:
    shutil.copy(backup, SRC)

print(f"Done: {len(MUTATIONS) - failures}/{len(MUTATIONS)} mutations caught",
      flush=True)
sys.exit(1 if failures else 0)
