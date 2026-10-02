"""Build the three things a player installs, and refuse if they disagree.

A player of a mod that owns both halves of an Archipelago integration needs
three files, and getting only two of them is worse than getting none:

    ALTTLArchipelago-<version>.zip   the BepInEx plugin, for the game folder
    alttl.apworld                    the world, for whoever generates the seed
    A Little to the Left.yaml        a player template to edit

The third is easy to leave out and the one most often asked for. cw4 settled
on the same three and calls it the convention for BepInEx Archipelago mods.

WHAT THIS REFUSES TO DO, and why each refusal exists:

- **Build when the version numbers disagree.** The mod and the apworld ship
  together, so a version is only useful if it names one code state on both
  sides. tools/check-version.py exists because they drifted to 0.1.0 / 0.1.0 /
  0.3.0 in a single session; running it here is what stops that shipping.
- **Ship a file it was not expecting.** The plugin folder is next to the
  game's interop assemblies, which are derived from the game and must never be
  redistributed. So the contents are an ALLOWLIST, and anything else in the
  build output is an error rather than a passenger.
- **Package a stale build.** It builds Release itself rather than zipping
  whatever happens to be lying in bin/.

This runs on a machine with the game installed - the plugin cannot be compiled
without the interop assemblies - so it is deliberately not a CI job. CI builds
and tests the apworld half, which is the half a runner can have.

    python tools/package-release.py [--out dist] [--skip-build]
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

MOD = ROOT / "src" / "ALTTLArchipelago"
BUILD_OUT = MOD / "bin" / "Release"

#: Exactly what goes in the plugin zip, and nothing else.
#:
#: An allowlist rather than a glob, because the build output sits beside
#: assemblies that must never leave this machine. Three are ours; two come
#: from NuGet and BepInEx does not provide them:
#:
#:   Archipelago.MultiClient.Net  the client, MIT
#:   Newtonsoft.Json              its serialiser, MIT
#:
#: Anything the build starts producing that is not here fails the package, so
#: a new dependency has to be looked at before it ships.
PLUGIN_FILES = (
    "ALTTLArchipelago.dll",
    "ALTTLArchipelago.Core.dll",
    "ALTTLModKit.dll",
    "Archipelago.MultiClient.Net.dll",
    "Newtonsoft.Json.dll",
)

#: Files the build may produce that are simply not shipped. Listed rather than
#: ignored silently, so the "unexpected file" check stays meaningful.
IGNORED = {
    ".pdb", ".xml", ".json", ".config", ".deps.json",
}

PLUGIN_DIR_IN_ZIP = "BepInEx/plugins/ALTTLArchipelago"


def check_versions() -> str:
    """The gate. Returns the agreed version, or exits."""
    result = subprocess.run([sys.executable, str(ROOT / "tools" / "check-version.py")],
                            capture_output=True, text=True)
    print(result.stdout.strip() or result.stderr.strip(), flush=True)
    if result.returncode != 0:
        sys.exit("REFUSING TO PACKAGE: the three version numbers disagree. "
                 "Fix them with tools/check-version.py --set X.Y.Z")

    manifest = json.loads(
        (ROOT / "apworld" / "alttl" / "archipelago.json").read_text(encoding="utf-8"))
    return manifest["world_version"]


def build_plugin() -> None:
    print("[2/5] building the plugin in Release", flush=True)
    # SkipDeploy: packaging must not overwrite the plugin in the player's game
    # folder as a side effect. Deploying is tools/deploy.sh's job and it closes
    # the game first; this does not, and a copy into a running install would
    # fail halfway.
    result = subprocess.run(
        ["dotnet", "build", str(MOD), "-c", "Release", "--nologo", "-v", "q",
         "-p:SkipDeploy=true"],
        capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout[-3000:], flush=True)
        sys.exit("REFUSING TO PACKAGE: the plugin did not build")


def collect_plugin_files() -> list[pathlib.Path]:
    if not BUILD_OUT.is_dir():
        sys.exit(f"no Release build at {BUILD_OUT}")

    present = {p.name: p for p in BUILD_OUT.iterdir() if p.is_file()}

    missing = [n for n in PLUGIN_FILES if n not in present]
    if missing:
        sys.exit("REFUSING TO PACKAGE: the build is missing "
                 + ", ".join(missing))

    unexpected = [n for n, p in sorted(present.items())
                  if n not in PLUGIN_FILES
                  and p.suffix not in IGNORED
                  and not n.endswith(".deps.json")]
    if unexpected:
        sys.exit(
            "REFUSING TO PACKAGE: the build produced files this script does "
            "not know about:\n  " + "\n  ".join(unexpected)
            + "\n\nLook at each one before adding it to PLUGIN_FILES. The "
              "plugin folder sits beside assemblies derived from the game, "
              "and those must never be redistributed.")

    return [present[n] for n in PLUGIN_FILES]


def write_plugin_zip(out: pathlib.Path, version: str,
                     files: list[pathlib.Path]) -> pathlib.Path:
    target = out / f"ALTTLArchipelago-{version}.zip"
    if target.exists():
        target.unlink()

    # The zip root is the game folder, so installing is "extract here" - the
    # layout matches what the csproj deploys to a dev install, deliberately,
    # so the two cannot diverge.
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for path in files:
            z.write(path, f"{PLUGIN_DIR_IN_ZIP}/{path.name}")
        z.writestr("README.txt", INSTALL_TXT.format(version=version))
    return target


#: The Thunderstore package: the same plugin files, with a manifest, the icon
#: and README from thunderstore/ and the changelog, all at the zip root, where
#: r2modman expects them (it installs them into BepInEx/plugins/<Team>-<Name>/).
#: The package name cannot change after the first upload (droha, 2026-09-30).
THUNDERSTORE = ROOT / "thunderstore"
TS_NAME = "A_Little_to_the_Left_Archipelago"
TS_DESCRIPTION = ("Archipelago multiworld randomizer for A Little to the Left. "
                  "Needs the matching alttl.apworld and player yaml from the "
                  "GitHub releases page.")
TS_WEBSITE = "https://github.com/drohack/alttl-archipelago"
#: The build every test ran on (CHANGELOG, "Tested on BepInEx be.755").
TS_DEPENDENCIES = ["BepInEx-BepInExPack_IL2CPP-6.0.755"]
TS_REQUIRED = ("manifest.json", "icon.png", "README.md")
#: Thunderstore refuses a README.md or CHANGELOG.md longer than this (its
#: upload form, 2026-10-02: "CHANGELOG.md is too long, max: 100000").
TS_TEXT_LIMIT = 100_000


def thunderstore_changelog(full: str, version: str) -> str:
    """The package's CHANGELOG.md: this version's section and a link to the
    whole history, which is far over TS_TEXT_LIMIT. ValueError when the
    changelog has no section for `version`. Pure."""
    head = f"\n## {version} - "
    start = full.find(head)
    if start < 0:
        raise ValueError(f"CHANGELOG.md has no '## {version} - ' section")
    end = full.find("\n## ", start + len(head))
    section = full[start + 1:end if end >= 0 else len(full)].rstrip() + "\n"
    return ("# Changelog\n\nThis release's changes. Every release, with how each change was "
            f"checked: [CHANGELOG.md]({TS_WEBSITE}/blob/v{version}/CHANGELOG.md).\n\n" + section)


def thunderstore_manifest(version: str) -> dict:
    return {"name": TS_NAME, "version_number": version, "website_url": TS_WEBSITE,
            "description": TS_DESCRIPTION, "dependencies": list(TS_DEPENDENCIES)}


def thunderstore_problems(manifest: dict, icon: bytes, names: list[str],
                          texts: dict[str, str] | None = None) -> list[str]:
    """What Thunderstore would refuse (wiki: creating a package; the upload
    form's length limit), as messages. `texts` is the README and changelog
    as text. Pure, so tools/test_package_release.py holds it to each rule."""
    problems = []
    for name, text in (texts or {}).items():
        if len(text) > TS_TEXT_LIMIT:
            problems.append(f"{name} is {len(text)} characters, over {TS_TEXT_LIMIT}")
    if not re.fullmatch(r"[A-Za-z0-9_]{1,128}", manifest.get("name", "")):
        problems.append("name must be 1-128 of a-z A-Z 0-9 _")
    if len(manifest.get("description", "")) > 250:
        problems.append(f"description is {len(manifest['description'])} chars, over 250")
    if not re.fullmatch(r"\d+\.\d+\.\d+", manifest.get("version_number", "")):
        problems.append("version_number must be Major.Minor.Patch")
    for dep in manifest.get("dependencies", []):
        if not re.fullmatch(r"[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+", dep):
            problems.append(f"dependency {dep!r} is not Team-Name-X.Y.Z")
    if not json.dumps(manifest).isascii():
        problems.append("the manifest is not ASCII")
    # PNG signature, then the IHDR chunk's width and height.
    if icon[:8] != b"\x89PNG\r\n\x1a\n" or icon[12:16] != b"IHDR":
        problems.append("icon.png is not a PNG")
    elif (int.from_bytes(icon[16:20], "big"), int.from_bytes(icon[20:24], "big")) != (256, 256):
        problems.append("icon.png must be 256x256")
    for name in TS_REQUIRED:
        if name not in names:
            problems.append(f"{name} is missing from the zip root")
    return problems


def write_thunderstore_zip(out: pathlib.Path, version: str,
                           files: list[pathlib.Path]) -> pathlib.Path:
    """dist/thunderstore/<name>-<version>.zip, in a folder of its own so the
    three-asset checks (check-release-assets.py, release_e2e.py), which read
    dist/ALTTLArchipelago-*.zip, never see it."""
    folder = out / "thunderstore"
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / f"{TS_NAME}-{version}.zip"
    for stale in folder.glob(f"{TS_NAME}-*.zip"):
        if stale != target:
            stale.unlink()

    manifest = thunderstore_manifest(version)
    icon = (THUNDERSTORE / "icon.png").read_bytes()
    readme = (THUNDERSTORE / "README.md").read_text(encoding="utf-8")
    try:
        changelog = thunderstore_changelog(
            (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), version)
    except ValueError as e:
        sys.exit(f"REFUSING TO PACKAGE the Thunderstore zip:\n  {e}")
    entries = {
        "manifest.json": (json.dumps(manifest, indent=4) + "\n").encode("ascii"),
        "icon.png": icon,
        "README.md": readme.encode("utf-8"),
        "CHANGELOG.md": changelog.encode("utf-8"),
    }
    for path in files:                      # the same allowlist as the plugin zip
        entries[path.name] = path.read_bytes()

    problems = thunderstore_problems(manifest, icon, list(entries),
                                     texts={"README.md": readme, "CHANGELOG.md": changelog})
    if problems:
        sys.exit("REFUSING TO PACKAGE the Thunderstore zip:\n  " + "\n  ".join(problems))

    if target.exists():
        target.unlink()
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in entries.items():
            z.writestr(name, data)
    return target


def write_yaml(out: pathlib.Path) -> pathlib.Path:
    target = out / "A Little to the Left.yaml"
    shutil.copyfile(ROOT / "apworld" / "alttl" / "player.yaml", target)
    return target


def build_apworld(out: pathlib.Path) -> pathlib.Path:
    print("[4/5] packaging the apworld", flush=True)
    result = subprocess.run(
        [sys.executable, str(ROOT / "tools" / "build_apworld.py"),
         "--out", str(out)],
        capture_output=True, text=True)
    print("      " + (result.stdout.strip().splitlines() or [""])[-1], flush=True)
    if result.returncode != 0:
        print(result.stderr[-2000:], flush=True)
        sys.exit("REFUSING TO PACKAGE: the apworld did not build")
    return out / "alttl.apworld"


INSTALL_TXT = """A Little To The Left - Archipelago, version {version}

THE MOD (this zip)

  Needs BepInEx 6 for IL2CPP, installed into the game folder and launched
  once so it generates its interop assemblies.

  Extract this zip into the game folder - the one containing
  "A Little To The Left.exe" - so the files land in

      BepInEx/plugins/ALTTLArchipelago/

  Launch the game. The main menu gains an Archipelago entry; open it and
  enter the Server (host:port from the room page) and your slot name.

THE APWORLD (alttl.apworld, shipped alongside)

  Only whoever GENERATES the seed needs it. Put it in the Archipelago
  installation's custom_worlds/ folder.

THE YAML ("A Little to the Left.yaml", shipped alongside)

  Your settings for the multiworld. Edit the name, then hand it to whoever
  generates.

The mod and the apworld carry the same version and are meant to be used
together. The mod checks this when it connects and refuses a seed built by a
different version, because the two disagree about the item table.
"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="dist")
    parser.add_argument("--skip-build", action="store_true",
                        help="package the Release output already on disk")
    args = parser.parse_args()

    # The version gate runs BEFORE anything is created. It used to run after
    # out.mkdir(), so a refused package still left an empty dist/ behind -
    # harmless, but it makes "did this build?" answerable only by reading the
    # output rather than by looking at the folder.
    print("[1/5] checking the version is consistent", flush=True)
    version = check_versions()

    out = (ROOT / args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    # LAST RELEASE'S ZIP GOES FIRST. The output folder accumulates: the zip is
    # named for its version, so building 0.3.3 beside 0.3.2 leaves two, and
    # then "the mod zip" is ambiguous - which is precisely what release_e2e
    # cannot resolve and what check-release-assets.py refuses. Found by that
    # refusal on its first real use, which is the checker earning its place.
    #
    # Only OUR zips, only in the top level, and only other versions - a rerun
    # of the same version simply overwrites.
    for stale in out.glob("ALTTLArchipelago-*.zip"):
        if stale.name != f"ALTTLArchipelago-{version}.zip":
            stale.unlink()
            print(f"      removed the older {stale.name}", flush=True)

    if args.skip_build:
        print("[2/5] --skip-build: using the Release output on disk", flush=True)
    else:
        build_plugin()

    print("[3/5] collecting and zipping the plugin, and its Thunderstore package", flush=True)
    files = collect_plugin_files()
    plugin_zip = write_plugin_zip(out, version, files)
    ts_zip = write_thunderstore_zip(out, version, files)

    apworld = build_apworld(out)

    print("[5/5] copying the player yaml", flush=True)
    yaml = write_yaml(out)

    print(f"\nRelease {version} in {out}:", flush=True)
    total = 0
    for path in (plugin_zip, apworld, yaml):
        size = path.stat().st_size
        total += size
        print(f"  {path.name:<34} {size:>9,} bytes", flush=True)
    # THE ARTIFACTS, NOT THE INPUTS. Everything above this line inspects the
    # build directory and the sources; this is the only step that opens the
    # three files a player downloads. The .apworld shipped for three releases
    # with an unreadable manifest precisely because nothing did.
    print("", flush=True)
    print("[5/5] checking the shipped files", flush=True)
    rc = subprocess.run(
        [sys.executable, str(ROOT / "tools" / "check-release-assets.py"),
         str(out), "--expect", version]).returncode
    if rc != 0:
        sys.exit("REFUSING TO PACKAGE: the assets just built do not check out")

    print(f"  Thunderstore: {ts_zip.relative_to(out.parent)} "
          f"({ts_zip.stat().st_size:,} bytes; upload by hand, not a GitHub asset)", flush=True)
    print(f"Done: 3 assets, {total:,} bytes, version {version}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
