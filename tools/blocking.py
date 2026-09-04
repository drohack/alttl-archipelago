"""Find ability-locked objects sitting on top of free ones.

The question, from the plan's list of things generation could not prove: an
ability-locked group is dimmed and immovable, so if one of its objects lies
physically over a FREE group's objects, a part check the logic calls reachable
might not be. Logic looser than the game is the dangerous direction - it is what
makes a seed unwinnable rather than merely tedious.

Input is `bounds:<tag>` from DevTools, which dumps every managed object's world
renderer bounds grouped by controller. Locked-ness is decided here from
abilities.json and the abilities actually held, so the same dump can be re-read
for any set of abilities without going back into the game.

This reports CANDIDATES, not verdicts. Whether an overlap actually prevents
solving the free group depends on where its pieces need to travel, and that
needs a person to try. Printing it as a verdict would be exactly the kind of
false confidence that made three earlier cat-trap "verifications" worthless.
"""
import json
import sys


def load_abilities(path="apworld/alttl/data/abilities.json"):
    raw = json.load(open(path, encoding="utf-8"))
    cls2ab = {c: a for a, cs in raw["abilities"].items() for c in cs}
    return set(raw["baseline"]), set(raw["notPuzzles"]), cls2ab


def read_bounds(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        header = f.readline()
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) != 7:
                continue
            rows.append({
                "controller": parts[0],
                "type": parts[1],
                "object": parts[2],
                "cx": float(parts[3]), "cy": float(parts[4]),
                "ex": float(parts[5]), "ey": float(parts[6]),
            })
    return rows


def overlap(a, b, pad=0.0):
    """Do two axis-aligned boxes intersect? pad shrinks them, so a shared edge
    does not count - objects laid out neatly in a row touch constantly."""
    dx = abs(a["cx"] - b["cx"]) - (a["ex"] + b["ex"] - pad)
    dy = abs(a["cy"] - b["cy"]) - (a["ey"] + b["ey"] - pad)
    return dx < 0 and dy < 0


def area(a, b):
    ox = min(a["cx"] + a["ex"], b["cx"] + b["ex"]) - max(a["cx"] - a["ex"], b["cx"] - b["ex"])
    oy = min(a["cy"] + a["ey"], b["cy"] + b["ey"]) - max(a["cy"] - a["ey"], b["cy"] - b["ey"])
    return max(ox, 0.0) * max(oy, 0.0)


def main(path, held):
    baseline, notpuzzles, cls2ab = load_abilities()
    rows = read_bounds(path)
    if not rows:
        print(f"no rows in {path}")
        return

    def locked(t):
        if t in baseline or t in notpuzzles:
            return False
        ab = cls2ab.get(t)
        return ab is not None and ab not in held

    lock = [r for r in rows if locked(r["type"])]
    free = [r for r in rows if not locked(r["type"])]
    print(f"{path}: {len(rows)} objects, {len(lock)} locked, {len(free)} free "
          f"(held: {', '.join(sorted(held)) or 'none'})")
    if not lock or not free:
        print("  nothing to compare - the level is entirely one or the other")
        return

    # A shared edge is normal; a real overlap has meaningful area. Sorted by
    # how much, so the worst candidate is the first thing read.
    hits = []
    for a in lock:
        for b in free:
            if overlap(a, b, pad=0.02):
                hits.append((area(a, b), a, b))
    hits.sort(key=lambda h: -h[0])

    if not hits:
        print("  no locked object overlaps a free one")
        return

    print(f"  {len(hits)} overlap(s), worst first:")
    for sq, a, b in hits[:20]:
        print(f"    {sq:6.3f}  LOCKED {a['controller']}/{a['object']} ({a['type']})"
              f"  over  FREE {b['controller']}/{b['object']} ({b['type']})")
    print("  CANDIDATES ONLY - confirm by hand that the free group can still be solved")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("usage: blocking.py <alttl-bounds-*.tsv> [HeldAbility ...]")
        raise SystemExit(2)
    main(sys.argv[1], set(sys.argv[2:]))
