"""List the controllers that are already solved the moment a level opens.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-solved-at-load.py [index ...]

The game must be open (DevTools loaded). With no index it walks the levels
with more than one part (names.json) - a single-part level has no part check
to send - and skips any already in the results file. Each level is booted from the title with DevTools, then the
`locks` command prints every controller with solved=True/False.

A group solved at load is sent by the mod the moment the level opens, which
is not a puzzle, so such a controller should be `"notALocation": true` in
levels.json. Results go to testserver/solved-at-load.json.

Loads only, no seed: nothing here needs a check to be sent. A level that is a
slot in a connected run can loop its loading screen, so run this with no
server up (the mod just retries the connection).
"""
import json
import os
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

OUT = os.path.join(e2e.REPO, "testserver", "solved-at-load.json")


def levels():
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "levels.json"), encoding="utf-8") as fh:
        return json.load(fh)["levels"]


def probe(log, index):
    """(level id seen, [solved controller names], live level count)."""
    log.new()
    e2e.dev("menu:title", settle=2.0)
    e2e.dev(f"boot:{index}", settle=7.0)
    e2e.dev("locks", settle=1.5)
    e2e.dev("livelevels", settle=1.0)
    seen, solved, live, in_locks = None, [], None, False
    for raw in log.new().splitlines():
        line = raw.split("] ", 1)[-1].strip()
        if line.startswith("locks: "):
            # "locks: DLC1 Jewelry Box 8 controller(s), ..." -> the level id
            seen = line[len("locks: "):].rsplit(" controller(s)", 1)[0]
            seen = seen.rsplit(" ", 1)[0]
            in_locks = True
            continue
        if in_locks and " type=" in line:
            name = line.split(" type=", 1)[0]
            if name.startswith("[") and "] " in name:    # "[0] Drawers"
                name = name.split("] ", 1)[1]
            if "solved=True" in line:
                solved.append(name)
            continue
        in_locks = False
        if line.startswith("livelevels: "):
            try:
                live = int(line.split()[1])
            except (IndexError, ValueError):
                live = None
    return seen, solved, live


def multi_part():
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "names.json"), encoding="utf-8") as fh:
        names = json.load(fh)["levels"]
    return {lid for lid, v in names.items() if len(v["parts"]) > 1}


def main():
    wanted = {int(a) for a in sys.argv[1:]}
    try:
        with open(OUT, encoding="utf-8") as fh:
            results = json.load(fh)
    except (OSError, ValueError):
        results = {}
    if wanted:
        rows = [l for l in levels() if l["levelIndex"] in wanted]
    else:
        several = multi_part()
        rows = [l for l in levels() if l["levelId"] in several
                and "solvedAtLoad" not in results.get(l["levelId"], {})]
    log = e2e.Log()
    log.new()
    found = 0
    for n, row in enumerate(rows, 1):
        idx, lid = row["levelIndex"], row["levelId"]
        t0 = time.time()
        seen, solved, live = probe(log, idx)
        tag = f"[{n}/{len(rows)} {idx} {lid}]"
        if seen != lid or live != 1:
            print(f"{tag} SKIP: loaded {seen!r}, {live} live level(s)",
                  flush=True)
            results[lid] = {"index": idx, "error": f"loaded {seen!r}, "
                                                   f"live {live}"}
        else:
            results[lid] = {"index": idx, "solvedAtLoad": solved}
            if solved:
                found += 1
            print(f"{tag} solved at load: {', '.join(solved) or 'none'} "
                  f"({time.time() - t0:.0f}s)", flush=True)
        with open(OUT, "w", encoding="utf-8") as fh:
            json.dump(results, fh, indent=1)
    e2e.dev("menu:title", settle=1.0)
    print(f"Done: {len(rows)} levels probed, {found} with a controller "
          f"solved at load -> {OUT}", flush=True)


if __name__ == "__main__":
    main()
