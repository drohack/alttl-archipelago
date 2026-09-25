"""Record what unlocks what while a person plays one level.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/record-unlocks.py <levelIndex> [--boot]

Run with the game open on a seed with ability locks OFF, so the only thing
gating any group is the game itself. It polls DevTools `reachable` and
`controllers` about once a second and prints every change as it happens:

    [  12.3s] APPEARS  Food (17 objects, 17 stuck)
    [  40.1s] SOLVED   Food
    [  41.0s] OPENS    Lids (stuck 4 -> 0)

It stops when the level completes, when testserver/logs/unlocks/STOP exists,
or after 20 minutes, and writes testserver/logs/unlocks/<index>.json with each
group's timeline and a candidate edge: the group solved most recently before
it opened. A group open from the first poll has no candidate - nothing had to
come first.

WHY. Every probe that tried to answer "what gates this group" without a player
was wrong at least once - on 2026-09-23 occlusion put Tupperware Nesting's
lids inside Stack 1, a forced-solve sweep said nothing unlocked them, and
droha found by playing that they appear after the food. The player's order is
the truth; this only writes down WHEN each group opened, so the edge comes
from real play instead of from a guess.

WHAT IT CANNOT SEE: a group that is touchable but physically covered (the
chalk behind a drawer read touchable the whole time). So after each level ask
the player whether they had to move or open anything to reach another group.

--boot counts live levels, boots the index, and refuses unless exactly one
level is loaded afterwards. --sweep <index> ... does that for each level in
turn and moves on as soon as one completes, so the player never waits.
Each group also records `shared`: objects it shares with another group, the
"pieces in the way" shape a locks-off play cannot show.
"""
import json
import os
import re
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

OUT = os.path.join(e2e.REPO, "testserver", "logs", "unlocks")
STOP = os.path.join(OUT, "STOP")          # end the current level
STOP_ALL = os.path.join(OUT, "STOPALL")   # end the whole sweep
LIMIT = 20 * 60

#: Controller types a player OPENS. Cupboard doors are AnimScrubbables.
OPENER_TYPES = {"DrawerController", "DrawerExpandableController", "Cupboard",
                "AnimScrubbables", "HangingToolsController"}

#: name, type, total, touchable, inactive, noCollider, colliderOff, stuck, done
REACH = re.compile(r"reachable:\s+(.+?)\t(\S+)\t(\d+)\t(\d+)\t(\d+)\t(\d+)\t"
                   r"(\d+)\t(\d+)\t(\d+)")
SOLVED = re.compile(r"\]\s+\[\d+\] (.+?) type=\S+ solved=(True|False)")


def snapshot(log):
    """{group: (total, stuck, done)} and {group: solved}, from one poll.

    Everything logged since the last poll is KEPT and returned. The first
    version discarded it before polling, which threw away the game's own
    LevelComplete line and recorded the level the mod navigated to next.
    """
    text = log.new()
    e2e.dev("reachable", settle=0.6)
    e2e.dev("controllers", settle=0.6)
    text += log.new()
    reach, solved = {}, {}
    for line in text.splitlines():
        m = REACH.search(line)
        if m:
            reach[m.group(1).strip()] = (int(m.group(3)), int(m.group(8)),
                                         int(m.group(9)), m.group(2))
            continue
        m = SOLVED.search(line)
        if m:
            solved[m.group(1).strip()] = m.group(2) == "True"
    return reach, solved, text


def count_levels(log):
    log.new()
    e2e.dev("livelevels", settle=2.0)
    for line in log.new().splitlines():
        if "livelevels: " in line:
            return int(line.split("livelevels: ", 1)[1].split()[0])
    return -1


LOCK_ROW = re.compile(r"\]\s+\[\d+\] (.+?) type=\S+ objects=(\d+) blocked=\d+ "
                      r"dimmed=\d+ shared=(\d+) ")


def boot(log, index):
    """Title first, then the level. Returns how many levels are loaded.

    FROM THE TITLE, because booting while the game still shows a FINISHED
    level loads it twice: switching back to gameplay from the completion
    screen acts as a retry. Measured 2026-09-23 - every boot from the title
    or from a level in progress came out as one level; both boots from a
    finished level came out as two. `dedupe` could get the count to one but
    left a level that looked alive and could not be dragged ("i can kind of
    click on them and they twitch, but i can't drag"), so it is not used
    here: a count that is not 1 stops the sweep instead.
    """
    log.new()
    e2e.dev("menu:title", settle=3.0)
    e2e.dev(f"boot:{index}", settle=3.0)
    for line in log.new().splitlines():
        if "timeScale" in line:
            print("      " + line.split("] ", 1)[-1], flush=True)
    return count_levels(log)


def shared_counts(log):
    """{group: objects it shares with another group}, from DevTools `locks`."""
    log.new()
    e2e.dev("locks", settle=1.0)
    out = {}
    for line in log.new().splitlines():
        m = LOCK_ROW.search(line)
        if m:
            out[m.group(1).strip()] = int(m.group(3))
    return out


def record(log, index, label):
    """Record one level until it completes or STOP appears. Writes its json."""
    start = time.time()
    groups = {}
    order = []
    last_beat = start
    shared = {}
    types = {}
    print(f"[{label} {index}] recording - play it", flush=True)
    while True:
        t = round(time.time() - start, 1)
        reach, solved, text = snapshot(log)
        if not reach and "no level running" in text:
            print(f"[{label}   {t:6.1f}s] no level running - stopping", flush=True)
            break
        if reach and not shared:
            shared = shared_counts(log)

        for name, (total, stuck, done, kind) in reach.items():
            types[name] = kind
            g = groups.get(name)
            if g is None:
                g = groups[name] = {"appeared": t, "stuckAtFirst": stuck,
                                    "total": total, "opened": None,
                                    "solved": None, "type": types.get(name),
                                    "stuck": [[t, stuck]]}
                if stuck == 0:
                    g["opened"] = t
                print(f"[{label}   {t:6.1f}s] APPEARS  {name} ({total} objects, "
                      f"{stuck} stuck)", flush=True)
            else:
                if stuck != g["stuck"][-1][1]:
                    was = g["stuck"][-1][1]
                    g["stuck"].append([t, stuck])
                    if stuck < was:
                        print(f"[{label}   {t:6.1f}s] FREES    {name} (stuck "
                              f"{was} -> {stuck})", flush=True)
                if g["opened"] is None and stuck == 0:
                    g["opened"] = t
                    print(f"[{label}   {t:6.1f}s] OPENS    {name} (stuck "
                          f"{g['stuckAtFirst']} -> 0)", flush=True)

        for name, is_solved in solved.items():
            g = groups.get(name)
            if g is not None and is_solved and g["solved"] is None:
                g["solved"] = t
                order.append((t, name))
                print(f"[{label}   {t:6.1f}s] SOLVED   {name}", flush=True)

        if "LevelComplete " in text:
            print(f"[{label}   {t:6.1f}s] level complete", flush=True)
            # Let the completion animation play out before the next boot
            # replaces the level - droha saw it run over the next one.
            time.sleep(5.0)
            break
        if os.path.exists(STOP) or os.path.exists(STOP_ALL):
            if os.path.exists(STOP):
                os.remove(STOP)
            print(f"[{label}   {t:6.1f}s] stop requested", flush=True)
            break
        if time.time() - start > LIMIT:
            print(f"[{label}   {t:6.1f}s] 20 minute limit", flush=True)
            break
        if time.time() - last_beat > 30:
            last_beat = time.time()
            done_n = sum(1 for g in groups.values() if g["solved"] is not None)
            print(f"[{label}   {t:6.1f}s] still recording: {done_n}/{len(groups)} "
                  f"groups solved", flush=True)
        time.sleep(0.3)

    # The candidate edge: the group solved most recently before this one
    # opened (or appeared already stuck-free after a solve). None when it was
    # open from the first poll, or opened with nothing solved before it.
    first_poll = min((g["appeared"] for g in groups.values()), default=0)
    result = {}
    for name, g in groups.items():
        opened = g["opened"]
        candidate = None
        if opened is not None and opened > first_poll:
            # An OPENER (drawer, cupboard) that freed objects in the same poll
            # wins: a drawer counts as solved while it is closed, so "the last
            # group solved" names the wrong thing for everything inside it.
            openers = [n for n, o in groups.items() if n != name
                       and (o.get("type") or "") in OPENER_TYPES
                       and any(ts == opened and v < prev for (ts, v), (_, prev)
                               in zip(o["stuck"][1:], o["stuck"][:-1]))]
            if openers:
                candidate = openers[0]
            else:
                before = [n for (ts, n) in order if ts <= opened and n != name]
                candidate = before[-1] if before else None
        result[name] = dict(g, candidate=candidate, shared=shared.get(name))

    path = os.path.join(OUT, f"{index}.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump({"levelIndex": index, "solveOrder": order,
                   "groups": result}, fh, indent=1)
    for name, g in sorted(result.items(), key=lambda kv: kv[1]["appeared"]):
        if g["opened"] is None:
            how = "never opened"
        elif g["opened"] <= first_poll:
            how = "open from the start"
        elif g["candidate"] is None:
            # Stuck at first, then opened with no GROUP solved before it:
            # something outside the tracked groups gated it (an intro, a
            # mechanism, a gesture). Only the player can say what.
            how = (f"opened at {g['opened']:.1f}s with NO group solved "
                   f"before it - ask what happened")
        else:
            how = f"opened after {g['candidate']}"
        print(f"  {name:40} {how}; solved "
              f"{'at %.1fs' % g['solved'] if g['solved'] is not None else 'no'}"
              f"; shared {g['shared']}", flush=True)
    return result


def main():
    """One level:  record-unlocks.py <index> [--boot]
    A sweep:    record-unlocks.py --sweep <index> <index> ...
                (boots each in turn, records until it completes, moves on)"""
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args or not all(a.isdigit() for a in args):
        raise SystemExit(__doc__)
    indices = [int(a) for a in args]
    sweep = "--sweep" in sys.argv
    if not sweep:
        indices = indices[:1]
    os.makedirs(OUT, exist_ok=True)
    for f in (STOP, STOP_ALL):
        if os.path.exists(f):
            os.remove(f)

    log = e2e.Log()
    log.new()
    recorded = 0
    for n, index in enumerate(indices, start=1):
        label = f"{n}/{len(indices)}"
        if os.path.exists(STOP_ALL):
            print(f"[{label} {index}] sweep stopped before this level", flush=True)
            break
        if sweep or "--boot" in sys.argv:
            count = boot(log, index)
            print(f"[{label} {index}] {count} level(s) loaded", flush=True)
            if count != 1:
                print(f"FAIL: {count} levels loaded, not 1 - stopping the "
                      f"sweep at {index}", flush=True)
                return 1
        record(log, index, label)
        recorded += 1
        print("", flush=True)
    print(f"Done: {recorded} of {len(indices)} level(s) recorded in "
          f"{os.path.relpath(OUT, e2e.REPO)}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
