"""Generate the gate's seed without the gate, the game, or a server.

Generation is pure Python - Generate.py and nothing else - so a DLC seed
costs seconds. The DATA layer of the harness can then be tested against
real DLC content instead of against whichever seed happened to be left in
testserver/out-e2e, which until now was how test_harness_data.py chose
what to read.

Writes to its OWN directory per scenario so a base and a DLC seed can
exist side by side and each test says which it meant:

    py -3.13 tools/make-seed.py           -> testserver/out-base
    py -3.13 tools/make-seed.py --dlc     -> testserver/out-dlc

Same fixed seed the gate uses, so what comes out here is what the gate
will play.
"""
import argparse
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e

SEED = "20260906"


def apply_overrides(text, overrides):
    """Replace `  key: value` lines in the generated yaml.

    yaml_text is what the release gate itself generates from and is
    covered by tests; a probe wanting an unusual combination edits the
    OUTPUT rather than growing a parameter on it. The combination this
    was written for is locks ON with every ability HELD - nothing is
    dimmed, but AbilityLocks still runs, which is the difference
    between a level the gate could not force and the same level solving
    cleanly under --quick.
    """
    for key, value in overrides:
        lines = text.splitlines()
        hit = False
        for i, line in enumerate(lines):
            if line.strip().startswith(f"{key}:"):
                indent = line[:len(line) - len(line.lstrip())]
                lines[i] = f"{indent}{key}: {value}"
                hit = True
        if not hit:
            sys.exit(f"override {key} matched no line in the yaml")
        text = "\n".join(lines) + "\n"
        print(f"      override {key}: {value}", flush=True)
    return text


def make(dlc, quick, steady, tag=None, overrides=()):
    """Generate one seed. Returns the output directory.

    `tag` names the output directory. Pass one for any variant seed, so
    an ability-locks-off seed cannot quietly overwrite the real one and
    leave a later probe reading a scenario nobody asked for.
    """
    tag = tag or ("dlc" if dlc else "base")
    yaml_dir = os.path.join(e2e.REPO, "testserver", f"yaml-{tag}")
    out = os.path.join(e2e.REPO, "testserver", f"out-{tag}")
    for d in (yaml_dir, out):
        if os.path.isdir(d):
            shutil.rmtree(d)
        os.makedirs(d)

    path = os.path.join(yaml_dir, f"{tag}.yaml")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(apply_overrides(e2e.yaml_text(quick, steady, dlc),
                                 overrides))
    print(f"[1/2] wrote {os.path.relpath(path, e2e.REPO)}", flush=True)

    r = subprocess.run(
        [sys.executable, "Generate.py",
         "--player_files_path", yaml_dir,
         "--outputpath", out, "--seed", SEED],
        cwd=e2e.AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stdout[-3000:], flush=True)
        print(r.stderr[-3000:], flush=True)
        sys.exit(f"[2/2] generation failed for {tag}")

    zips = [f for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        sys.exit(f"[2/2] generation produced no seed in {out}")
    print(f"[2/2] {tag} seed: {zips[0]}", flush=True)
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dlc", action="store_true",
                        help="every puzzle drawn from the two DLCs")
    parser.add_argument("--quick", action="store_true",
                        help="ability locks OFF and no cat traps. Use this "
                             "to measure pure FORCEABILITY - with locks on, "
                             "a gated level is never attempted at all and a "
                             "probe reports it as correctly gated while "
                             "testing nothing about whether it can be "
                             "solved.")
    parser.add_argument("--steady", action="store_true")
    parser.add_argument("--tag", default=None,
                        help="output directory suffix, testserver/out-<tag>")
    parser.add_argument("--override", action="append", default=[],
                        metavar="KEY=VALUE",
                        help="rewrite one yaml option after generating "
                             "the text, e.g. --override ability_locks=true")
    args = parser.parse_args()

    overrides = [tuple(o.split("=", 1)) for o in args.override]
    out = make(args.dlc, args.quick, args.steady, args.tag, overrides)
    print(f"Done: seed in {os.path.relpath(out, e2e.REPO)}", flush=True)


if __name__ == "__main__":
    main()
