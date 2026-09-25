"""Which groups sit physically underneath another group's objects?

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-occlusion.py [levelIndex ...]

THE REMAINING BLIND SPOT, and the one that has beaten every instrument so far.

A group can be fully interactable and still unreachable, because something else
is on top of it. Measured on 2026-09-22: Paper Plane Supplies' chalk jigsaws
reported blocked=0 dimmed=0 while droha could not get at them - "the rest of
the chalk pieces are behind the drawer, so i need to be able to close it to get
to them". Tupperware Nesting's Lids is the same shape: registered at boot,
undimmed, and impossible.

So neither of the two censuses this project has can see it. `locks` reports
interactability. `reachable` reports colliders. Occlusion is geometry, and
geometry is what `bounds:<index>` dumps - every managed object's world bounds,
grouped by controller, written for exactly this question.

WHAT IT COMPUTES. For each pair of groups, how much of group A's bounding area
lies inside group B's. A group largely inside a container group's footprint is
a candidate for being stored in it, which is a lead worth playing and is not
proof: two groups can share a tabletop without either gating the other, and a
piece can overlap a drawer while resting beside it.

WHY BOUNDS AND NOT THE DRAWER DATA. Drawer.ContainedObjects was tried first and
checked against the only hand audit available. It was wrong in BOTH directions
- it missed Bathroom Drawer's Bottle and Indexable, missed Workbench entirely,
and added five chalk jigsaws the audit had excluded. Bounds are measured from
the live scene rather than from an authored list, so they fail differently.
Neither is trusted alone.
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

LOGS = os.path.join(e2e.REPO, "testserver", "logs")
QUEUE = os.path.join(LOGS, "handtest-queue.json")
REPORT = os.path.join(LOGS, "occlusion-report.json")

#: bounds: writes a TSV, it does not print the rows.
#: controller, type, object, cx, cy, ex, ey - centre and extents.
BOUNDS_TSV = os.path.join(e2e.GAME, "BepInEx", "alttl-bounds-occl.tsv")

#: A LIMITATION INHERITED, NOT FIXED. DumpBounds walks ManagedObjects only,
#: which is the same trap that made freeze: disable one collider instead of
#: fifty-six on 2026-09-22. Containables, Stickables, StackablesY and
#: Dirtyables each keep objects outside that list, so a group's footprint here
#: can be a fraction of its real one. Under-reporting an overlap is the safe
#: direction for a CANDIDATE list - it misses leads, it does not invent them -
#: but a group that looks small here may not be.


def rect_of(boxes):
    """One rectangle covering every object in the group."""
    x0 = min(cx - ex for cx, cy, ex, ey in boxes)
    x1 = max(cx + ex for cx, cy, ex, ey in boxes)
    y0 = min(cy - ey for cx, cy, ex, ey in boxes)
    y1 = max(cy + ey for cx, cy, ex, ey in boxes)
    return x0, y0, x1, y1


def overlap(a, b):
    """Fraction of rect a's area that lies inside rect b."""
    ax0, ay0, ax1, ay1 = a
    bx0, by0, bx1, by1 = b
    ix = max(0.0, min(ax1, bx1) - max(ax0, bx0))
    iy = max(0.0, min(ay1, by1) - max(ay0, by0))
    area = (ax1 - ax0) * (ay1 - ay0)
    return (ix * iy / area) if area > 0 else 0.0


def main():
    wanted = [int(a) for a in sys.argv[1:] if a.isdigit()]
    if not wanted:
        with open(QUEUE, encoding="utf-8") as fh:
            wanted = [r["levelIndex"] for r in json.load(fh)
                      if r["verdict"] == "pending"]

    print(f"[1/2] launching; {len(wanted)} level(s)", flush=True)
    log = e2e.Log()
    log.before_launch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    got = ""
    end = time.time() + 240
    while time.time() < end:
        got += log.new()
        if "connected." in got or "ALTTL dev tools loaded" in got:
            break
        time.sleep(1)
    time.sleep(10)

    report = {}
    for n, index in enumerate(wanted, start=1):
        log.new()
        e2e.dev(f"boot:{index}", settle=7.0)
        log.new()
        if os.path.exists(BOUNDS_TSV):
            os.remove(BOUNDS_TSV)
        e2e.dev("bounds:occl", settle=3.5)
        log.new()

        groups = {}
        if os.path.exists(BOUNDS_TSV):
            with open(BOUNDS_TSV, encoding="utf-8", errors="replace") as fh:
                for row in fh.read().splitlines()[1:]:
                    cells = row.split("	")
                    if len(cells) < 7:
                        continue
                    try:
                        groups.setdefault(cells[0], []).append(
                            tuple(float(c) for c in cells[3:7]))
                    except ValueError:
                        continue

        rects = {k: rect_of(v) for k, v in groups.items() if v}
        pairs = []
        for a, ra in rects.items():
            for b, rb in rects.items():
                if a == b:
                    continue
                frac = overlap(ra, rb)
                if frac >= 0.85:
                    pairs.append({"inside": a, "container": b,
                                  "fraction": round(frac, 3)})

        report[str(index)] = {"groups": len(rects), "pairs": pairs}
        note = ""
        if pairs:
            note = "  <== %d group(s) sit almost entirely inside another" % len(
                {p["inside"] for p in pairs})
        print(f"[2/2] {n}/{len(wanted)} index {index}: "
              f"{len(rects)} group(s) with bounds{note}", flush=True)
        for p in pairs[:6]:
            print(f"        {p['inside']} is {int(p['fraction'] * 100)}% "
                  f"inside {p['container']}", flush=True)

    close_game()
    with open(REPORT, "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)
    hits = sum(1 for v in report.values() if v["pairs"])
    print("", flush=True)
    print(f"Done: {len(report)} level(s), {hits} with a group sitting inside "
          f"another -> {REPORT}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
