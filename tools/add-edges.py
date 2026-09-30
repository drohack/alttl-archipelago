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

    raw = io.open(TABLE, encoding="utf-8", newline="").read()
    ending = "\r\n" if "\r\n" in raw else "\n"
    table = json.loads(raw)
    by_id = {l["levelId"]: l for l in table["levels"]}

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
