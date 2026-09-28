"""Which ending each level reports when the gate FORCES it, from the gate logs.

    py -3.13 tools/harvest-forced-endings.py

Writes fixtures/forced-endings.tsv: levelId, the solution id its forced
completions reported most often, and how many times. release_e2e reads it to
know which Solution location forcing files on a level with fixed endings
(the mod files the ending the id names; see CheckRouter.ForEnding), so the
paper plan collects the same location the run will.

Sources: testserver/logs/e2e-*.log, the game logs every gate keeps. A level
the gate has never forced is absent, and the harness then predicts what the
mod does with an id it does not know: the first unseen ending, else the first
ending. Re-run after a gate that reports "the run left its paper plan" on a
level missing here.
"""
import collections
import glob
import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "fixtures", "forced-endings.tsv")
LINE = re.compile(r"LevelComplete +id=(.+?)  index=\d+ .*solutionId=(\S+)")


def main():
    seen = collections.defaultdict(collections.Counter)
    logs = sorted(glob.glob(os.path.join(REPO, "testserver", "logs", "e2e-*.log")))
    for path in logs:
        with open(path, encoding="utf-8", errors="replace") as fh:
            for m in LINE.finditer(fh.read()):
                seen[m.group(1)][m.group(2)] += 1
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# The solution id each level's FORCED completion reported most often in\n"
                 "# the gate logs (testserver/logs/e2e-*.log). Regenerate with\n"
                 "# tools/harvest-forced-endings.py; do not hand-edit.\n")
        fh.write("levelId\tsolutionId\ttimes\n")
        for level_id in sorted(seen):
            solution_id, times = seen[level_id].most_common(1)[0]
            fh.write(f"{level_id}\t{solution_id}\t{times}\n")
    print(f"Done: {len(seen)} level(s) from {len(logs)} gate log(s) -> "
          f"{os.path.relpath(OUT, REPO)}", flush=True)


if __name__ == "__main__":
    main()
