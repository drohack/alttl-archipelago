"""Add dependsOn edges to levels.json, deliberately and reviewably.

    py -3.13 tools/add-edges.py <plan.json>            dry run
    py -3.13 tools/add-edges.py <plan.json> --write

The plan is a list of {levelId, controller, dependsOn, why}. `why` is required
and is not decoration: every edge in this file is a claim about what a player
must do first, and the ones that were added without a recorded reason are
exactly the ones that later turned out to be wrong.

DIRECTION MATTERS MORE THAN ANYTHING ELSE HERE. Adding an edge TIGHTENS a
requirement, and a requirement that asks for too much only makes a card look
busier. Removing one loosens it, and a requirement that asks for too little
lets the generator put progression somewhere a player cannot reach - which
ended a run on 2026-09-21. So this tool only ADDS. Taking an edge out is a
hand edit, on purpose, so that it cannot happen in a batch.

AND THE TRAP THAT COMES WITH BEING RIGHT. Writing a level's real edges can
DELETE the guard that covers it: once every group in a level declares the
opener's ability, `_behind_an_opener` goes empty, `structural_gap` goes blank,
and the level's locations become progression-eligible again. Measured
2026-09-22: that path would have taken the guard from 114 locations to 53. So
this refuses to write unless every level it touches is already listed in
data/proven-requirements.json, under `suspect` or `proven`. The rule is in that
file too: a suspect entry lands in the same commit as the edges.

After writing, regenerate the exported names:

    ALTTL_WRITE_GOLDEN=1 dotnet test src/ALTTLArchipelago.Core.Tests
"""
import io
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

TABLE = os.path.join(e2e.REPO, "apworld", "alttl", "data", "levels.json")
PROVEN = os.path.join(e2e.REPO, "apworld", "alttl", "data",
                      "proven-requirements.json")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    write = "--write" in sys.argv
    if not args:
        raise SystemExit(__doc__)

    with open(args[0], encoding="utf-8") as fh:
        plan = json.load(fh)

    for entry in plan:
        for key in ("levelId", "controller", "dependsOn", "why"):
            if not entry.get(key):
                raise SystemExit(f"every entry needs {key}: {entry}")

    with open(PROVEN, encoding="utf-8") as fh:
        guard = json.load(fh)
    covered = set(guard.get("suspect", {})) | set(guard.get("proven", {}))

    def gap_survives(level_id, raw_level):
        """Would this level still be guarded after its edges are written?

        The trap is narrow and worth being narrow about. `structural_gap` has
        several sources and only ONE of them is cleared by adding edges: "a
        drawer or cupboard whose contents the table does not record" goes away
        precisely because the contents now record it. A bespoke levelClass, an
        extraAbilities patch, a controller absent from the phase list - none of
        those move, so a level carrying any of them stays guarded and needs no
        suspect entry.

        Refusing on every level would have blocked TupperwareNesting, whose
        gap is its bespoke class and is untouched by anything written here.
        """
        if level_id in covered:
            return True
        if raw_level.get("levelClass", "Level") != "Level":
            return True
        if raw_level.get("extraAbilities"):
            return True
        if raw_level.get("phases"):
            return True

        # NOTHING TO LOSE IF THERE IS NO GUARD TODAY. This check exists to
        # stop edges DELETING protection, not to demand protection for a
        # level that never had any. DLC2 Ink Bottles is unguarded precisely
        # BECAUSE its groups declare nothing - no drawer, no bespoke class, no
        # extraAbilities, so no structural_gap - which is the bug rather than
        # a reason to refuse the fix. Writing the edge is what makes its
        # requirement honest.
        sys.path.insert(0, os.path.join(e2e.REPO, "Archipelago"))
        try:
            from worlds.alttl import data
            level = next((l for l in data.LEVELS
                          if l.level_id == level_id), None)
            if level is not None and not level.unproven_parts:
                return True
        except Exception:
            pass                    # cannot tell: fall through and refuse
        return False

    raw = io.open(TABLE, encoding="utf-8", newline="").read()
    ending = "\r\n" if "\r\n" in raw else "\n"
    table = json.loads(raw)
    by_id = {l["levelId"]: l for l in table["levels"]}

    # The guard check, before anything is changed.
    touched = {e["levelId"] for e in plan}
    naked = sorted(l for l in touched
                   if l in by_id and not gap_survives(l, by_id[l]))

    added, already, missing = [], [], []
    for entry in plan:
        level = by_id.get(entry["levelId"])
        if level is None:
            missing.append(entry["levelId"])
            continue
        for c in level["controllers"]:
            if c["name"] != entry["controller"]:
                continue
            deps = set(c.get("dependsOn") or [])
            if entry["dependsOn"] in deps:
                already.append((entry["levelId"], entry["controller"]))
                break
            if entry["dependsOn"] not in {x["name"] for x in level["controllers"]}:
                missing.append(f"{entry['levelId']}: no controller named "
                               f"{entry['dependsOn']!r} to depend on")
                break
            c["dependsOn"] = sorted(deps | {entry["dependsOn"]})
            added.append(entry)
            break
        else:
            missing.append(f"{entry['levelId']}: no controller named "
                           f"{entry['controller']!r}")

    for entry in added:
        print(f"  + {entry['levelId']}: {entry['controller']} dependsOn "
              f"{entry['dependsOn']}", flush=True)
        print(f"      {entry['why']}", flush=True)
    for lid, ctrl in already:
        print(f"  = {lid}: {ctrl} already had it", flush=True)
    for problem in missing:
        print(f"  ! {problem}", flush=True)

    print("", flush=True)
    print(f"  {len(added)} to add, {len(already)} already present, "
          f"{len(missing)} problem(s)", flush=True)

    if naked:
        print("", flush=True)
        print("REFUSING TO WRITE. These levels are not in "
              "proven-requirements.json, so writing their edges would clear "
              "the structural_gap that guards them and make their locations "
              "progression-eligible again:", flush=True)
        for lid in naked:
            print(f"     {lid}", flush=True)
        print("  Add a suspect entry in the same change. See the rule in "
              "that file.", flush=True)
        return 1

    if missing:
        print("", flush=True)
        print("Done: refusing to write with unresolved names above.",
              flush=True)
        return 1

    if not write:
        print("", flush=True)
        print("Done: dry run, nothing written. Pass --write to apply.",
              flush=True)
        return 0

    with io.open(TABLE, "w", encoding="utf-8", newline=ending) as fh:
        json.dump(table, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    print("", flush=True)
    print(f"Done: wrote {len(added)} edge(s). Now regenerate names.json: "
          f"ALTTL_WRITE_GOLDEN=1 dotnet test src/ALTTLArchipelago.Core.Tests",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
