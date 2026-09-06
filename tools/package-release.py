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
  enter the server address, port and your slot name.

THE APWORLD (alttl.apworld, shipped alongside)

  Only whoever GENERATES the seed needs it. Put it in the Archipelago
  installation's custom_worlds/ folder.

THE YAML ("A Little to the Left.yaml", shipped alongside)

  Your settings for the multiworld. Edit the name, then hand it to whoever
  generates.

The mod and the apworld carry the same version and are meant to be used
together. A mismatch is not detected at runtime.
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

    if args.skip_build:
        print("[2/5] --skip-build: using the Release output on disk", flush=True)
    else:
        build_plugin()

    print("[3/5] collecting and zipping the plugin", flush=True)
    files = collect_plugin_files()
    plugin_zip = write_plugin_zip(out, version, files)

    apworld = build_apworld(out)

    print("[5/5] copying the player yaml", flush=True)
    yaml = write_yaml(out)

    print(f"\nRelease {version} in {out}:", flush=True)
    total = 0
    for path in (plugin_zip, apworld, yaml):
        size = path.stat().st_size
        total += size
        print(f"  {path.name:<34} {size:>9,} bytes", flush=True)
    print(f"Done: 3 assets, {total:,} bytes, version {version}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
