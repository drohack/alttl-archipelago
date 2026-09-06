"""Package apworld/alttl into a distributable alttl.apworld.

An .apworld is a zip whose root entry is the world package folder, so
`worlds.alttl` is importable straight out of the archive. That import path is
the reason this script exists rather than a one-line `zip -r`:

**A packaged world runs from inside a zip, where nothing has a path on disk.**
Anything reading its own data with open() works perfectly in a development
checkout and fails the moment a player installs the release. That exact bug
shipped in this world once - data.py used open() and the packaged build could
not load at all - so the CI job that calls this script goes on to generate a
real seed from the ZIP with the loose copy removed. Testing the folder proves
nothing about the artifact.

Usage:
    python tools/build_apworld.py [--out dist]
"""

import argparse
import os
import pathlib
import shutil
import sys
import zipfile

REPO = pathlib.Path(__file__).resolve().parent.parent
SOURCE = REPO / "apworld" / "alttl"

#: Never ship these. __pycache__ in particular can carry stale bytecode built
#: against a different Python and makes the archive non-reproducible.
EXCLUDE_DIRS = {"__pycache__", ".pytest_cache", ".mypy_cache"}
EXCLUDE_SUFFIXES = {".pyc", ".pyo"}

#: The world's own tests are not shipped: they import Archipelago's test
#: harness, which a player's install does not have on the import path.
#: player.yaml is the shipped template, but it is a RELEASE asset handed to
#: the player separately - not something the world imports. It lives in the
#: package so a test can hold it in step with ALTTLOptions; it does not belong
#: inside the archive.
EXCLUDE_TOP_LEVEL = {"test", "player.yaml"}


def _wanted(path: pathlib.Path) -> bool:
    rel = path.relative_to(SOURCE)
    if rel.parts and rel.parts[0] in EXCLUDE_TOP_LEVEL:
        return False
    if any(part in EXCLUDE_DIRS for part in rel.parts):
        return False
    return path.suffix not in EXCLUDE_SUFFIXES


def build(out_dir: pathlib.Path) -> pathlib.Path:
    if not (SOURCE / "__init__.py").is_file():
        sys.exit(f"no world package at {SOURCE}")

    out_dir.mkdir(parents=True, exist_ok=True)
    target = out_dir / "alttl.apworld"
    if target.exists():
        target.unlink()

    files = sorted(p for p in SOURCE.rglob("*") if p.is_file() and _wanted(p))

    # Sorted order and a fixed timestamp so the same input gives byte-identical
    # output, which makes "did the world actually change?" answerable.
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
        for path in files:
            arcname = pathlib.PurePosixPath("alttl") / path.relative_to(SOURCE).as_posix()
            info = zipfile.ZipInfo(str(arcname), date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, path.read_bytes())

    names = zipfile.ZipFile(target).namelist()
    required = ["alttl/__init__.py", "alttl/archipelago.json",
                "alttl/data/levels.json", "alttl/docs/setup_en.md"]
    missing = [r for r in required if r not in names]
    if missing:
        sys.exit(f"built archive is missing: {missing}")

    print(f"built {target} ({target.stat().st_size} bytes, {len(names)} entries)")
    return target


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="dist", help="output directory")
    parser.add_argument("--install-to", default=None,
                        help="also copy into this Archipelago checkout's custom_worlds")
    args = parser.parse_args()

    target = build(pathlib.Path(args.out) if os.path.isabs(args.out)
                   else REPO / args.out)

    if args.install_to:
        ap = pathlib.Path(args.install_to)
        custom = ap / "custom_worlds"
        custom.mkdir(parents=True, exist_ok=True)
        shutil.copy2(target, custom / target.name)

        # A loose worlds/alttl would satisfy the import and hide a broken
        # archive, so the caller is told about it rather than left guessing.
        loose = ap / "worlds" / "alttl"
        if loose.exists():
            print(f"WARNING: {loose} still exists, so an import may come from "
                  f"the folder rather than the archive. Remove it to test the "
                  f"packaged build honestly.")
        print(f"installed to {custom / target.name}")


if __name__ == "__main__":
    main()
