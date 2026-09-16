"""The documentation must not lie, and nothing else checks that it does not.

WHY THIS EXISTS. An audit on 2026-09-16 found that the setup guide Archipelago
serves for this game had said "**This randomizer is not finished** ... there is
nothing to connect with yet" since before the first release. It also claimed
the mod zip includes BepInEx, which it does not, and then never told the player
to install BepInEx at all - so following it exactly produced a mod that could
not load.

None of it was caught, because NOTHING IN THIS REPO VALIDATES A SINGLE .md
FILE. `test_player_yaml.py` pins the shipped yaml against options.py and works
well - player.yaml is the most accurate document in the project. Prose had no
equivalent, and Archipelago's own compliance suite only asserts the tutorial
file EXISTS, never what is in it. So a wrong document passed CI, passed
compliance, and passed the 23/23 release gate.

WHAT IT CHECKS, and why each one earned its place rather than being a tidy
idea someone had:

1. **Every relative markdown link resolves.** One was broken - devtools.md
   pointed at `docs/data/` from inside `docs/`, which resolves to
   `docs/docs/data/`. Two forms that look broken on disk are allowed
   explicitly, because both are correct where they are actually rendered:
   GitHub's `../../releases` from a repo-root README, and the Archipelago
   webhost's `../player-options` from a world's docs (86 upstream worlds use
   it).

2. **No member carries two `<summary>` elements.** Twenty-five did. It is
   invalid doc XML - only the first binds - and it happens mechanically: a
   member is moved or inserted and the doc comment above it is left behind on
   whatever is now underneath. The results actively lied, one of them
   describing an approach that the very next summary said had been tried twice
   and rejected. This is the one finding here that is purely mechanical and
   will certainly recur, which is why it is checked rather than just fixed.

3. **No `file:line` citations in source comments.** Seven existed and three
   pointed at unrelated content - two into an append-only 2,910-line log, where
   the cited ranges had long since drifted to other subjects. The form is
   BANNED rather than re-resolved, because a checker that merely verifies them
   would go green the moment someone corrects the numbers, and they would rot
   again. Name the symbol or the section heading instead; those move with the
   thing they name.

4. **Facts that can be read are read, not asserted.** The version and the
   minimum Archipelago version appear in prose across several docs. Anything
   spelling one out must agree with the manifest.

    py -3.13 tools/check-docs.py
"""
from __future__ import annotations

import json
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent

#: Directories whose contents are not ours, or are build output.
SKIP_DIRS = ("Archipelago", "bin", "obj", "node_modules", "__pycache__",
             "testserver", "dist", "release-test", "release-final",
             ".git", ".pytest_cache")

#: Link targets that do not exist on disk and are still correct.
#:
#: Both are resolved by the site that renders them, not by the filesystem:
#: `../../releases` from a repo-root README is GitHub's own convention for the
#: releases page, and `../player-options` is how a world's game page reaches
#: its options page on the Archipelago webhost - checked against the upstream
#: checkout, where 86 worlds spell it exactly that way.
ALLOWED_UNRESOLVED = ("../../releases", "../player-options")

LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
CITATION = re.compile(r"[\w./-]+\.(?:md|cs|py|json|yml|yaml):\d+")


def _ours(path: pathlib.Path) -> bool:
    return not any(part in SKIP_DIRS for part in path.relative_to(REPO).parts)


def _walk(pattern: str):
    return sorted(p for p in REPO.rglob(pattern) if _ours(p))


def check_links() -> list[str]:
    """Every relative markdown link must resolve to something on disk."""
    problems = []
    for doc in _walk("*.md"):
        for n, line in enumerate(
                doc.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            for target in LINK.findall(line):
                if target.startswith(("http://", "https://", "#", "mailto:")):
                    continue
                if target in ALLOWED_UNRESOLVED:
                    continue
                # A leading slash is a webhost-absolute path, e.g.
                # /games/<game>/player-options. Not ours to resolve.
                if target.startswith("/"):
                    continue
                landing = (doc.parent / target.split("#")[0]).resolve()
                if not landing.exists():
                    rel = doc.relative_to(REPO).as_posix()
                    problems.append(f"{rel}:{n}: broken link -> {target}")
    return problems


def check_summaries() -> list[str]:
    """No member may carry more than one <summary>.

    A run of contiguous /// lines documents exactly one member. Two summaries
    in that run means one of them was orphaned by a member that moved.
    """
    problems = []
    for source in _walk("*.cs"):
        lines = source.read_text(encoding="utf-8", errors="replace").splitlines()
        run: list[int] = []
        for i, line in enumerate(lines):
            if line.strip().startswith("///"):
                run.append(i)
                continue
            if run:
                count = sum(1 for j in run if "<summary>" in lines[j])
                if count > 1:
                    rel = source.relative_to(REPO).as_posix()
                    problems.append(
                        f"{rel}:{run[0] + 1}: {count} <summary> elements on one "
                        f"member ({lines[i].strip()[:60]})")
            run = []
    return problems


def check_citations() -> list[str]:
    """Comments must not cite a file by line number."""
    problems = []
    for pattern in ("*.cs", "*.py"):
        for source in _walk(pattern):
            if source.name == pathlib.Path(__file__).name:
                continue
            for n, line in enumerate(
                    source.read_text(encoding="utf-8",
                                     errors="replace").splitlines(), 1):
                stripped = line.strip()
                commentish = stripped.startswith(("//", "/", "#", "*"))
                if not (commentish or '"""' in line):
                    continue
                for hit in CITATION.findall(line):
                    rel = source.relative_to(REPO).as_posix()
                    problems.append(
                        f"{rel}:{n}: cites {hit} by line number. Name the "
                        f"symbol or the section heading instead - line numbers "
                        f"rot, and three of these already pointed at "
                        f"unrelated content.")
    return problems


def check_declared_versions() -> list[str]:
    """Prose that spells out a version must agree with the manifest."""
    manifest = json.loads(
        (REPO / "apworld" / "alttl" / "archipelago.json").read_text(encoding="utf-8"))
    minimum = manifest["minimum_ap_version"]

    problems = []
    # Any "Archipelago <x.y.z>" in a doc must be the version we declare.
    wanted = re.compile(r"Archipelago \*{0,2}(\d+\.\d+\.\d+)")
    for doc in _walk("*.md"):
        for n, line in enumerate(
                doc.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            for found in wanted.findall(line):
                if found != minimum:
                    rel = doc.relative_to(REPO).as_posix()
                    problems.append(
                        f"{rel}:{n}: says Archipelago {found}, but "
                        f"archipelago.json declares {minimum}")
    return problems


CHECKS = (
    ("markdown links resolve", check_links),
    ("one <summary> per member", check_summaries),
    ("no file:line citations", check_citations),
    ("declared versions agree", check_declared_versions),
)


def main() -> int:
    failed = 0
    for name, check in CHECKS:
        problems = check()
        if problems:
            failed += len(problems)
            print(f"FAIL: {name}", flush=True)
            for problem in problems:
                print(f"  {problem}", flush=True)
        else:
            print(f"PASS: {name}", flush=True)

    if failed:
        print(f"\n{failed} documentation problem(s).", flush=True)
        return 1
    print("\nDone: the docs check out.", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
