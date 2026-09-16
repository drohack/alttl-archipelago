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
import json
import os
import pathlib
import re
import shutil
import sys
import zipfile

REPO = pathlib.Path(__file__).resolve().parent.parent
SOURCE = REPO / "apworld" / "alttl"

#: THE MANIFEST IS NOT COPIED VERBATIM, and this is the reason.
#:
#: There are TWO manifests and they have different rules, which is easy to
#: miss because they are the same file name:
#:
#:   apworld/alttl/archipelago.json   the SOURCE manifest. Must NOT carry
#:                                    `version` or `compatible_version` -
#:                                    Archipelago's own
#:                                    test_world_manifest.test_no_container_version
#:                                    fails a world that does, and
#:                                    tools/check-version.py enforces it here.
#:
#:   alttl/archipelago.json (in zip)  the CONTAINER manifest. MUST carry both.
#:                                    They describe the APContainer packaging
#:                                    scheme rather than the world.
#:
#: Those two rules only look contradictory. The packager is supposed to add
#: the container keys, which is what Archipelago's "Build APWorlds" launcher
#: component does, and the spec says outright: "Do not write these fields
#: yourself."
#:
#: This script wrote the source manifest into the zip unchanged, so the
#: packaged world had NEITHER key, and every Archipelago that loaded it said:
#:
#:   Invalid or missing manifest file for ... alttl.apworld.
#:   This apworld will stop working with Archipelago 0.7.0.
#:   compatible_version - This might be the incorrect world version for this file
#:
#: The loader reads `manifest["compatible_version"]` as a plain subscript, so
#: an absent key is a KeyError, and the bare key name becomes the message.
#:
#: IT IS NOT ONLY A FUTURE PROBLEM, which is how it went unnoticed - the world
#: loads and generates today, because the loader logs and carries on below
#: 0.7.0. But the throw happens before any metadata is populated, so game,
#: world_version and minimum_ap_version are all left None. The consequence:
#: our declared `minimum_ap_version` was NEVER ENFORCED - the loader's check
#: is `if apworld.minimum_ap_version and ...`, and None skips it - so a player
#: on too old an Archipelago got no warning, just whatever broke next. And
#: with two versions of the world installed, "load the newest" sorted ours as
#: 0.0.0.
CONTAINER_VERSION = 7
COMPATIBLE_VERSION = 7

#: Never ship these. __pycache__ in particular can carry stale bytecode built
#: against a different Python and makes the archive non-reproducible.
EXCLUDE_DIRS = {"__pycache__", ".pytest_cache", ".mypy_cache"}
EXCLUDE_SUFFIXES = {".pyc", ".pyo"}

#: Always excluded, whatever .apignore says. `.apignore` is packaging
#: instructions rather than world code, and Archipelago's own GLOBAL.apignore
#: drops it for the same reason.
ALWAYS_EXCLUDED = {".apignore"}


def _apignore_excludes() -> set:
    """Top-level names .apignore says to leave out of the archive.

    READ, NOT DUPLICATED. This list used to live only here, as a Python set,
    which meant Archipelago's own "Build APWorlds" component - the one its
    spec calls "the correct way to package your .apworld" - knew nothing about
    it and produced a DIFFERENT archive from the same source, shipping the
    test suite and the player yaml into people's installs.

    Adding .apignore fixed that packager and created a second copy of the same
    knowledge, so this reads it rather than restating it. A comment saying
    "keep these in step" is a thing to forget; one source of truth is not.
    """
    path = SOURCE / ".apignore"
    if not path.is_file():
        sys.exit(f"no {path} - Archipelago's own packager needs it to leave "
                 f"the tests and the player yaml out of the archive")
    names = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.split("#")[0].strip()
        if line:
            names.add(line.rstrip("/"))
    return names


def _archipelago_container_versions(ap: pathlib.Path):
    """What Archipelago's own packager would write, read from its source.

    Returns (container_version, compatible_version) or None when there is no
    checkout to read - the build must work without one, since packaging a
    release does not otherwise need Archipelago at all.

    Read rather than imported: importing worlds.Files drags in the whole of
    Archipelago's runtime and its dependencies, which is a heavy thing to
    require of a packaging script. The two values are plain integer literals.
    """
    files = ap / "worlds" / "Files.py"
    if not files.is_file():
        return None
    text = files.read_text(encoding="utf-8")

    m = re.search(r"^container_version:\s*int\s*=\s*(\d+)", text, re.MULTILINE)
    if not m:
        return None
    container = int(m.group(1))

    # APWorldContainer.get_manifest sets its own compatible_version, and it is
    # NOT the same number for every container type in that file - APPlayer
    # containers use 6 and 5. Anchor on the class so a future edit to one of
    # the others cannot silently move ours.
    cls = re.search(r"class APWorldContainer\b.*?(?=\nclass )", text, re.DOTALL)
    if not cls:
        return None
    m = re.search(r'manifest\["compatible_version"\]\s*=\s*(\d+)', cls.group(0))
    if not m:
        return None
    return container, int(m.group(1))


def _manifest_bytes(source_manifest: bytes, ap: pathlib.Path) -> bytes:
    """The source manifest plus the two container keys.

    Key order follows the apworld specification's worked example - the world's
    own fields, then version and compatible_version - so a diff against the
    spec reads straight across.
    """
    manifest = json.loads(source_manifest)

    for forbidden in ("version", "compatible_version"):
        if forbidden in manifest:
            sys.exit(f"the SOURCE manifest defines {forbidden!r}, which "
                     f"Archipelago's test_no_container_version forbids. It "
                     f"belongs to the packaged container only - this script "
                     f"adds it.")

    actual = _archipelago_container_versions(ap)
    if actual is None:
        print(f"note: no Archipelago checkout at {ap} to confirm the "
              f"container versions against; using the pinned "
              f"{CONTAINER_VERSION}/{COMPATIBLE_VERSION}")
    elif actual != (CONTAINER_VERSION, COMPATIBLE_VERSION):
        sys.exit(f"Archipelago now packages containers as version "
                 f"{actual[0]}/compatible {actual[1]}, but this script pins "
                 f"{CONTAINER_VERSION}/{COMPATIBLE_VERSION}. Update the "
                 f"constants - shipping the wrong number is worse than "
                 f"shipping none, because the loader will believe it.")

    manifest["version"] = CONTAINER_VERSION
    manifest["compatible_version"] = COMPATIBLE_VERSION
    return (json.dumps(manifest, indent=4) + "\n").encode("utf-8")


def _wanted(path: pathlib.Path) -> bool:
    rel = path.relative_to(SOURCE)
    excluded = _apignore_excludes() | ALWAYS_EXCLUDED
    if rel.parts and rel.parts[0] in excluded:
        return False
    if any(part in EXCLUDE_DIRS for part in rel.parts):
        return False
    return path.suffix not in EXCLUDE_SUFFIXES


def build(out_dir: pathlib.Path, ap: pathlib.Path,
          require_ap: bool = False) -> pathlib.Path:
    if not (SOURCE / "__init__.py").is_file():
        sys.exit(f"no world package at {SOURCE}")

    # WHERE ARCHIPELAGO IS, passed in rather than assumed.
    #
    # This was `REPO / "Archipelago"`, hardcoded, and /Archipelago/ is
    # gitignored - so on a CI runner the container-version cross-check below
    # found no checkout, took the warn-and-continue branch, and never once
    # ran. The only reason it ever ran at all is that the path happens to
    # exist on droha's machine. A note printed into a green log is the same
    # failure mode as the logged manifest error this whole guard exists to
    # prevent, which is why --require-ap makes it fatal where a checkout is
    # known to be present.
    if require_ap and _archipelago_container_versions(ap) is None:
        sys.exit(f"--require-ap was given but {ap} has no readable "
                 f"worlds/Files.py, so the container versions cannot be "
                 f"confirmed. This flag exists because the check silently "
                 f"did nothing in CI for three releases.")

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
            payload = path.read_bytes()
            if str(arcname) == "alttl/archipelago.json":
                payload = _manifest_bytes(payload, ap)
            zf.writestr(info, payload)

    names = zipfile.ZipFile(target).namelist()
    required = ["alttl/__init__.py", "alttl/archipelago.json",
                "alttl/data/levels.json", "alttl/docs/setup_en.md"]
    missing = [r for r in required if r not in names]
    if missing:
        sys.exit(f"built archive is missing: {missing}")

    # THE ARTIFACT, NOT THE INPUT. Nothing else checks the manifest that
    # actually ships: check-version.py reads the source one, and a world whose
    # container keys are missing still loads and generates, so CI's "generate
    # a real seed from the zip" job passed throughout. The only symptom was a
    # logged error nobody was reading.
    with zipfile.ZipFile(target) as zf:
        shipped = json.loads(zf.read("alttl/archipelago.json"))
    for key in ("version", "compatible_version", "game", "world_version",
                "minimum_ap_version"):
        if key not in shipped:
            sys.exit(f"the packaged manifest has no {key!r} - Archipelago "
                     f"will refuse to read it and silently stop enforcing "
                     f"minimum_ap_version")

    print(f"built {target} ({target.stat().st_size} bytes, {len(names)} "
          f"entries, container v{shipped['version']})")
    return target


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="dist", help="output directory")
    parser.add_argument("--install-to", default=None,
                        help="also copy into this Archipelago checkout's custom_worlds")
    parser.add_argument("--ap", default=None,
                        help="Archipelago checkout to confirm the container "
                             "versions against. Defaults to --install-to, "
                             "then to ./Archipelago.")
    parser.add_argument("--require-ap", action="store_true",
                        help="fail rather than warn when that checkout cannot "
                             "be read. CI passes this; it has a checkout and "
                             "an unverified build there is worthless.")
    args = parser.parse_args()

    # --install-to is an Archipelago checkout by definition, so a caller that
    # named one has already told us where to look.
    ap = pathlib.Path(args.ap or args.install_to or (REPO / "Archipelago"))

    target = build(pathlib.Path(args.out) if os.path.isabs(args.out)
                   else REPO / args.out,
                   ap, require_ap=args.require_ap)

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
