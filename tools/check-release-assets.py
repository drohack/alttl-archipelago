"""Check the three SHIPPED files, not the sources they were built from.

WHY THIS EXISTS. Every other checker in this repo reads an input.
check-version.py binds three files in the repo; package-release.py inspects
the build directory the zip is made from; the world's tests run against the
source folder. Nothing opened the artifacts a player downloads - which is how
the .apworld shipped for three releases with a manifest Archipelago could not
read, while CI stayed green the whole time.

WHAT IT CATCHES, each one a gap the audit found:

  - a release whose four versions disagree. The zip filename, the README.txt
    inside it, the plugin DLL and the apworld each carry a version
    independently, and nothing compared them. They agree only because one
    package-release.py run makes them together - the documented workflow of
    downloading the apworld from CI and building the zip locally has no such
    guarantee.
  - a STALE assets folder. tools/release_e2e.py defaults to release-test/,
    refuses two zips as ambiguous, and accepts one old zip without a word. It
    green-lit the previous release twice during 0.3.2.
  - a .apworld whose manifest is missing its container keys, which is the bug
    that started all this.
  - a player yaml that is not valid YAML. Nothing has ever parsed the shipped
    template: package-release.py copies it and both CI jobs that look like
    they generate from it actually generate from Archipelago's own template.
    test_player_yaml.py uses a hand-rolled line reader that never invokes a
    YAML parser, so a tab, a flow sequence or a duplicate key would ship.

    py -3.13 tools/check-release-assets.py dist
"""
import argparse
import json
import pathlib
import re
import sys
import zipfile

try:
    import yaml
except ImportError:      # pragma: no cover - PyYAML ships with Archipelago
    yaml = None

REPO = pathlib.Path(__file__).resolve().parent.parent
ZIP_NAME = re.compile(r"^ALTTLArchipelago-(\d+\.\d+\.\d+)\.zip$")
DLL_PATH = "BepInEx/plugins/ALTTLArchipelago/ALTTLArchipelago.dll"
MANIFEST = "alttl/archipelago.json"


def fail(problems, msg):
    problems.append(msg)


def dll_carries(blob: bytes, version: str) -> bool:
    """Does the assembly carry this version string, in either encoding?

    Deliberately a CONTAINMENT check rather than a parse. Reading the version
    out of .NET metadata properly means walking the tables, and the failure
    worth catching is coarse: a stale or wrong DLL zipped under a correct
    filename. If the DLL were 0.3.1 under an 0.3.2 name, "0.3.2" would simply
    not be in it. The cost of the shortcut is a theoretical false pass if the
    string appears coincidentally, which is worth it for a check that needs no
    dependencies and cannot itself break on an odd assembly.
    """
    if version.encode() in blob:
        return True
    return version.encode("utf-16-le") in blob


def check_mod_zip(path: pathlib.Path, problems):
    m = ZIP_NAME.match(path.name)
    if not m:
        fail(problems, f"{path.name} is not named ALTTLArchipelago-X.Y.Z.zip")
        return None
    version = m.group(1)

    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        if DLL_PATH not in names:
            fail(problems, f"{path.name} has no {DLL_PATH}")
            return version
        if not dll_carries(z.read(DLL_PATH), version):
            fail(problems,
                 f"{path.name} is named {version} but its plugin DLL does not "
                 f"carry that version - a stale or Debug build was packaged")
        if "README.txt" in names:
            readme = z.read("README.txt").decode("utf-8", "replace")
            if version not in readme.splitlines()[0]:
                fail(problems,
                     f"{path.name}'s README.txt first line does not name "
                     f"{version}")
        else:
            fail(problems, f"{path.name} has no README.txt")
    return version


def check_apworld(path: pathlib.Path, problems):
    with zipfile.ZipFile(path) as z:
        if MANIFEST not in z.namelist():
            fail(problems, f"{path.name} has no {MANIFEST}")
            return None
        manifest = json.loads(z.read(MANIFEST))

    for key in ("version", "compatible_version"):
        if key not in manifest:
            fail(problems,
                 f"{path.name}'s manifest has no {key!r} - Archipelago cannot "
                 f"read it, and silently stops enforcing minimum_ap_version")
    for key in ("game", "world_version", "minimum_ap_version"):
        if key not in manifest:
            fail(problems, f"{path.name}'s manifest has no {key!r}")
    return manifest.get("world_version")


def check_yaml(path: pathlib.Path, problems):
    text = path.read_text(encoding="utf-8")
    if yaml is None:
        fail(problems, "PyYAML is not installed, so the shipped yaml was not "
                       "parsed - install it rather than skipping this")
        return
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        fail(problems, f"{path.name} is not valid YAML: "
                       f"{str(e).splitlines()[0]}")
        return
    if not isinstance(doc, dict):
        fail(problems, f"{path.name} does not parse to a mapping")
        return
    for key in ("name", "game", "requires"):
        if key not in doc:
            fail(problems, f"{path.name} has no {key!r}")
    if doc.get("game") != "A Little to the Left":
        fail(problems, f"{path.name} names game {doc.get('game')!r}")
    # A hardcoded name generates one slot and collides on two. The shipped
    # template must keep a placeholder - this is the bug droha caught by
    # reading it, which no test could see.
    name = str(doc.get("name", ""))
    if "{" not in name:
        fail(problems,
             f"{path.name} has a literal name {name!r} with no placeholder; "
             f"two copies of it cannot generate together")


#: Source that ends up inside the shipped files. obj/ and bin/ are build
#: output, and the test project is not shipped.
SHIPPED_SOURCE = (
    ("src", "*.cs"),
    ("apworld", "*.py"),
    ("apworld", "*.json"),
)


def newer_than(asset: pathlib.Path):
    """Shipped source modified after `asset` was built.

    WHY. --expect catches assets carrying a DIFFERENT version. It cannot
    catch the case that actually bit on 2026-09-20: the version was still
    0.4.0, so release-test/ agreed with the checkout perfectly, while
    AbilityLocks.cs, Checks.cs and Plugin.cs had all moved on. The release
    gate installed that zip and spent two dozen runs measuring a mod that
    contained neither the reachability gate nor the ability-lock register
    hook - the two things the runs were meant to be testing.

    mtime-based, so a fresh clone (every file stamped at checkout time)
    would report everything as newer. That is why this is opt-in behind
    --fresh and why CI does not pass it.
    """
    cutoff = asset.stat().st_mtime
    out = []
    for folder, pattern in SHIPPED_SOURCE:
        for path in (REPO / folder).rglob(pattern):
            parts = set(path.parts)
            if parts & {"obj", "bin", "__pycache__"}:
                continue
            if ".Tests" in path.parent.name or "test" in path.parts:
                continue
            if path.stat().st_mtime > cutoff:
                out.append(path.relative_to(REPO))
    return sorted(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("assets", nargs="?", default="dist",
                    help="folder holding the three release files")
    ap.add_argument("--expect", default=None,
                    help="the version these assets must be. A folder whose "
                         "files agree with each other can still be the LAST "
                         "release - which is exactly what release-test/ holds "
                         "between releases, and what the release gate tested "
                         "twice during 0.3.2 without noticing.")
    ap.add_argument("--fresh", action="store_true",
                    help="also fail if any shipped source file is NEWER "
                         "than the assets. Catches a rebuild that never "
                         "happened, which --expect cannot see while the "
                         "version is unchanged. Local only - a fresh clone "
                         "stamps every file at checkout time.")
    args = ap.parse_args()

    folder = pathlib.Path(args.assets)
    if not folder.is_absolute():
        folder = REPO / folder
    if not folder.is_dir():
        sys.exit(f"no such folder: {folder}")

    problems = []
    zips = sorted(folder.glob("ALTTLArchipelago-*.zip"))
    apworlds = sorted(folder.glob("*.apworld"))
    yamls = sorted(folder.glob("*.yaml"))

    if len(zips) != 1:
        fail(problems, f"expected exactly one mod zip, found {len(zips)}")
    if len(apworlds) != 1:
        fail(problems, f"expected exactly one .apworld, found {len(apworlds)}")
    if len(yamls) != 1:
        fail(problems, f"expected exactly one .yaml, found {len(yamls)}")
    if problems:
        print(f"checking {folder}", flush=True)
        for p in problems:
            print(f"  PROBLEM {p}", flush=True)
        return 1

    print(f"checking {folder}", flush=True)
    mod_version = check_mod_zip(zips[0], problems)
    world_version = check_apworld(apworlds[0], problems)
    check_yaml(yamls[0], problems)

    if args.expect:
        for what, got in (("mod zip", mod_version),
                          ("apworld", world_version)):
            if got and got != args.expect:
                fail(problems,
                     f"the {what} is {got} but this checkout is "
                     f"{args.expect} - these are last release's assets")

    if args.fresh:
        stale = newer_than(zips[0])
        if stale:
            fail(problems,
                 f"{len(stale)} shipped source file(s) are newer than "
                 f"{zips[0].name} - it was never rebuilt, so this run would "
                 f"test code that is not in the repo any more: "
                 f"{', '.join(str(p) for p in stale[:5])}"
                 + (" ..." if len(stale) > 5 else "")
                 + ". Rebuild with tools/package-release.py")

    if mod_version and world_version and mod_version != world_version:
        fail(problems,
             f"the mod zip is {mod_version} and the apworld is "
             f"{world_version}. They disagree about the item table, and the "
             f"mod now refuses such a pair at connect - so this release "
             f"would not play at all")

    for line in (f"  mod zip      {zips[0].name}",
                 f"  apworld      world_version {world_version}",
                 f"  player yaml  {yamls[0].name}"):
        print(line, flush=True)

    if problems:
        print("", flush=True)
        for p in problems:
            print(f"  PROBLEM {p}", flush=True)
        print(f"\nFAIL: {len(problems)} problem(s) in the shipped files",
              flush=True)
        return 1

    print(f"\nPASS: the three assets agree on {mod_version}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
