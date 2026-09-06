"""The version is written in three files. Fail if they disagree.

WHY THIS EXISTS. The mod DLL and the apworld ship together and a player
installs both, so a version number is only useful if it identifies one code
state on both sides. Twice it has not:

- In this project, world_version was bumped to 0.2.0 and then 0.3.0 for two
  item-id changes while the csproj and BepInPlugin sat at 0.1.0. A player could
  have held a mod and an apworld that both said a version and disagreed about
  what the items were called, and nothing in the build objected.
- The same thing happened twice in cw4-archipelago, once across twelve commits
  that included an item rename. Their tools/bump-version.ps1 records it.

The comment beside the csproj version used to say the three were "in sync only
by care". Care is not a mechanism.

Run standalone, or with --set X.Y.Z to write all three at once.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

CSPROJ = ROOT / "src" / "ALTTLArchipelago" / "ALTTLArchipelago.csproj"
PLUGIN = ROOT / "src" / "ALTTLArchipelago" / "Plugin.cs"
MANIFEST = ROOT / "apworld" / "alttl" / "archipelago.json"

#: Each site as (label, path, pattern). The pattern's one group is the version,
#: which is also what --set rewrites - so reading and writing cannot drift into
#: disagreeing about where the number lives.
SITES = (
    ("csproj <Version>", CSPROJ, re.compile(r"(?<=<Version>)([^<]+)(?=</Version>)")),
    ("Plugin.cs [BepInPlugin]", PLUGIN,
     re.compile(r'(?<=\[BepInPlugin\(Guid, "A Little To The Left Archipelago", ")'
                r'([^"]+)(?=")')),
    ("archipelago.json world_version", MANIFEST,
     re.compile(r'(?<="world_version": ")([^"]+)(?=")')),
)

#: Keys the apworld specification forbids in a world manifest - they describe
#: the .apworld CONTAINER and belong to whatever builds it. Archipelago's own
#: test/general/test_world_manifest.py::test_no_container_version enforces this,
#: and we shipped both of them until it was finally run.
FORBIDDEN_MANIFEST_KEYS = ("version", "compatible_version")


def read(path: pathlib.Path, pattern: re.Pattern[str], label: str) -> str:
    text = path.read_text(encoding="utf-8")
    found = pattern.search(text)
    if not found:
        raise SystemExit(f"could not find the version in {label} ({path})")
    return found.group(1)


def check() -> int:
    versions = {label: read(path, pattern, label) for label, path, pattern in SITES}

    problems = []
    if len(set(versions.values())) != 1:
        problems.append("the version disagrees between files:")
        problems += [f"    {label:34} {value}" for label, value in versions.items()]

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for key in FORBIDDEN_MANIFEST_KEYS:
        if key in manifest:
            problems.append(
                f"archipelago.json must not define {key!r} - it describes the "
                f".apworld container, not the world. See apworld "
                f"specification.md, enforced by test_no_container_version.")

    if problems:
        print("\n".join(problems))
        return 1

    print(f"version {next(iter(versions.values()))}, consistent across "
          f"{len(SITES)} files; manifest clean")
    return 0


def write(version: str) -> int:
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise SystemExit(f"version must be major.minor.build, not {version!r}")

    for label, path, pattern in SITES:
        text = path.read_text(encoding="utf-8")
        # count=1 deliberately: a second match would mean the pattern is looser
        # than the one site it is meant to name, and silently rewriting an
        # unrelated number is worse than failing the check.
        updated, hits = pattern.subn(version, text, count=1)
        if hits != 1:
            raise SystemExit(f"could not set the version in {label} ({path})")
        path.write_text(updated, encoding="utf-8", newline="")
        print(f"  {label} -> {version}")
    return check()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--set", metavar="X.Y.Z",
                        help="write this version to all three files")
    args = parser.parse_args()
    return write(args.set) if args.set else check()


if __name__ == "__main__":
    sys.exit(main())
