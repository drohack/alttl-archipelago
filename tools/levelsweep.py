"""Run the DevTools level sweep with the mod parked, and PROVE it was parked.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/levelsweep.py [index ...]
    PYTHONUNBUFFERED=1 py -3.13 -u tools/levelsweep.py --survey
    PYTHONUNBUFFERED=1 py -3.13 -u tools/levelsweep.py --dump

With no argument it sweeps everything. With indices it sweeps only those,
which is how a handful of levels get re-measured without paying twenty minutes
for the other 168 - and merging rows is the normal way this table is
maintained anyway, because a fresh full sweep regresses the hand-audited
phased levels.

WHY THIS IS A TOOL AND NOT A CHECKLIST. The sweep is the source of
apworld/alttl/data/levels.json, and it has to be taken with ALTTLArchipelago
not loaded: DailyGuard answers LevelInterface.IsDailyTidy and IsHolidayDaily
with false while a run is active, so a sweep taken with the mod loaded reports
that no level is a daily - which looks exactly like proof that none are. That
result was very nearly written into levels.json.

The documented workaround was "rename the plugin folder", and it does not
work: BepInEx scans every subdirectory of plugins/ for DLLs and does not care
what the folder is called. A sweep run under that advice came back with all 36
daily flags false. So this MOVES the folder out of plugins/ entirely, and then
reads the log back to confirm the mod never logged a line. A human following
the same steps cannot easily check the last part, which is the part that
failed.

DLC. Levels belonging to a DLC the player does not own throw Il2CppException
on load, so the sweep skips them - but it sweeps the ones they do own. Which
DLCs were seen is printed, because a sweep that silently found none would
produce a base-game table that looks complete.

The output is written beside the log for a human to diff. It is deliberately
NOT copied over levels.json: a changed controller name is a changed location
name, which breaks every seed in flight, and that diff has to be read.
"""
import os
import shutil
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game       # noqa: E402

import release_e2e as e2e                             # noqa: E402

PLUGINS = os.path.join(e2e.GAME, "BepInEx", "plugins")
LIVE = os.path.join(PLUGINS, "ALTTLArchipelago")
PARKED = os.path.join(e2e.GAME, "BepInEx", "_parked", "ALTTLArchipelago")
SWEPT = os.path.join(e2e.GAME, "BepInEx", "alttl-levels.json")
SURVEYED = os.path.join(e2e.GAME, "BepInEx", "alttl-solutions.tsv")
REGISTRATIONS = os.path.join(e2e.GAME, "BepInEx", "alttl-registrations.tsv")

#: --survey runs the PREFAB walk instead of the runtime sweep. The two answer
#: different questions and neither can be derived from the other: the sweep
#: measures what REGISTERS at boot and is authority for locations; the survey
#: measures what the prefab CONTAINS, including inactive children, and is
#: authority for ability requirements. SurveyCrossCheckTests holds them up
#: against each other, so both have to be retaken when content changes.
SURVEY = {
    "command": "solutions",
    "done": "survey complete:",
    "counting": "-- solution survey:",
    "source": SURVEYED,
    "name": "alttl-solutions",
    "suffix": ".tsv",
}
#: --dump is the one check-game-facts.py reads. It is a single read of the
#: game's own tables rather than a walk, so it is fast - but it is the run the
#: parking matters most for: the mod answers IsDailyTidy with false during a
#: run, and a dump taken with it loaded reports that no level is a daily.
DUMP = {
    "command": "dump",
    "done": "dump written to",
    "counting": "AllLevelInterfaces:",
    "source": os.path.join(e2e.GAME, "BepInEx", "alttl-dump.json"),
    "name": "alttl-dump",
    "suffix": ".json",
}
SWEEP = {
    "command": "levelsweep",
    "done": "levelsweep complete:",
    "counting": "-- level data sweep:",
    "source": SWEPT,
    "name": "alttl-levels",
    "suffix": ".json",
}

#: Any line tagged with the mod's BepInEx name means it loaded after all.
MOD_TAG = "A Little To The Left Archipelago"

#: Generous. 173 levels at a few seconds each, and a level that hangs should
#: be reported rather than waited on forever.
SWEEP_TIMEOUT = 40 * 60


def park():
    """Move the mod OUT of plugins/. Renaming it in place does nothing."""
    if not os.path.isdir(LIVE):
        print("      the mod is not installed; nothing to park", flush=True)
        return False
    if os.path.isdir(PARKED):
        raise SystemExit(f"FAIL: {PARKED} already exists. A previous run did "
                         "not restore the mod - check it before continuing.")
    os.makedirs(os.path.dirname(PARKED), exist_ok=True)
    shutil.move(LIVE, PARKED)
    print(f"      moved the mod out of plugins/ -> {PARKED}", flush=True)
    return True


def unpark(was_parked):
    if not was_parked:
        return
    if os.path.isdir(LIVE):
        print("WARNING: the mod reappeared in plugins/ while parked; leaving "
              f"the parked copy at {PARKED} rather than overwriting",
              flush=True)
        return
    shutil.move(PARKED, LIVE)
    print("      the mod is back in plugins/", flush=True)


def wait_for(log, needle, timeout, phase, what):
    """A local wait loop.

    NOT e2e.Log.wait: that one prints through e2e.say, which numbers its lines
    [n/7] for the gate's phases and overwrites the gate's progress file. A
    second tool borrowing it reports someone else's phase count.
    """
    got = ""
    end = time.time() + timeout
    last = 0.0
    while time.time() < end:
        got += log.new()
        if needle in got:
            return True
        if time.time() - last > 8:
            last = time.time()
            print(f"[{phase}/6] waiting for {what} "
                  f"({int(end - time.time())}s left)", flush=True)
        time.sleep(0.5)
    return False


def sweep(log, only, mode):
    """Send the command and follow the run's own progress lines."""
    command = mode["command"]
    if only and mode is SWEEP:
        command += ":" + only
    e2e.dev(command)
    total = None
    seen = -1
    deadline = time.time() + SWEEP_TIMEOUT
    text = ""
    while time.time() < deadline:
        text += log.new()
        for line in text.splitlines():
            if "-- DLC installed:" in line:
                which = line.split("-- DLC installed:")[1].strip(" -")
                if which and total is None:
                    print(f"[4/6] DLC the game reports installed: {which}",
                          flush=True)
            if mode["counting"] in line and total is None:
                total = line.split(mode["counting"])[1].split("levels")[0].strip()
                print(f"[4/6] {mode['command']}: {total} levels", flush=True)
        # The sweep numbers its own lines, so mirror the last one rather than
        # inventing a second counter that could disagree with it.
        loading = [l for l in text.splitlines()
                   if "] loading level " in l or "] prefab " in l]
        if loading and len(loading) != seen:
            seen = len(loading)
            print(f"[4/6] {loading[-1].split('[')[-1].strip()}", flush=True)
        if mode["done"] in text:
            # Reported BEFORE the completion line by the sweep, deliberately:
            # a table missing levels is still a table, and the harness would
            # otherwise call a short sweep a successful one.
            for line in text.splitlines():
                if "never loaded and are ABSENT" in line:
                    print("[4/6] WARNING: " + line.split("] ")[-1], flush=True)
            return True
        time.sleep(2)
    return False


def main():
    flags = sys.argv[1:]
    args = [a for a in flags if not a.startswith("--")]
    mode = SWEEP
    if "--survey" in flags:
        mode = SURVEY
    elif "--dump" in flags:
        mode = DUMP
    if mode is not SWEEP and args:
        raise SystemExit(f"{mode['command']} covers everything; "
                         "it takes no indices")
    only = ",".join(args)
    if only:
        print(f"[0/6] sweeping ONLY these level indices: {only}", flush=True)
    print("[1/6] closing the game and parking the mod", flush=True)
    close_game()
    was_parked = park()

    ok = False
    with Environment("levelsweep"):
        try:
            print("[2/6] launching with DevTools only", flush=True)
            log = e2e.Log()
            what, _windowed = e2e.describe_display()
            print(f"      {what}", flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)

            if not wait_for(log, "ALTTL dev tools loaded", 180, 2,
                            "DevTools to load"):
                print("FAIL: DevTools never loaded", flush=True)
                return 1

            # The level manager does not exist the instant the plugin does.
            print("[3/6] waiting for the title screen to settle", flush=True)
            time.sleep(20)

            # RECORD THE REGISTRATION TIMELINE IN THE SAME PASS. regstart is a
            # global toggle on Level.RegisterObjectController, so it composes
            # with the sweep for free - and the sweep is the only thing that
            # boots all 173 levels, which is what makes the timeline worth
            # having. Without it the question "does this level reveal
            # controllers as you solve it, or is that entry a ghost" is
            # answered by counting once and believing the count, which this
            # project has got wrong three times.
            #
            # Sweep mode only: the prefab survey never boots a level and the
            # dump never loads one, so neither registers anything.
            if mode is SWEEP:
                print("[4/6] recording the registration timeline too",
                      flush=True)
                e2e.dev("regstart", settle=1.0)

            print(f"[4/6] running {mode['command']}", flush=True)
            finished = sweep(log, only, mode)

            # Stop BEFORE reporting the failure: the timeline of a sweep that
            # died partway is still the timeline of every level it reached,
            # and that is often the evidence for why it died.
            if mode is SWEEP:
                e2e.dev("regstop", settle=2.0)

            if not finished:
                print("FAIL: the sweep did not finish. The last progress line "
                      "above names the level it stopped on.", flush=True)
                return 1
        finally:
            close_game()

    print("[5/6] checking the mod really was absent", flush=True)
    with open(e2e.LOG, encoding="utf-8", errors="replace") as fh:
        contaminated = [l for l in fh if MOD_TAG in l]
    unpark(was_parked)
    if contaminated:
        print(f"FAIL: the mod logged {len(contaminated)} line(s), so it was "
              "loaded and the sweep's daily flags cannot be trusted:",
              flush=True)
        for line in contaminated[:3]:
            print("   " + line.strip(), flush=True)
        return 1
    print("      zero mod lines in the log", flush=True)

    print("[6/6] saving the table", flush=True)
    if not os.path.isfile(mode["source"]):
        print(f"FAIL: {mode['source']} was not written", flush=True)
        return 1
    out = os.path.join(e2e.REPO, "testserver", "logs",
                       "%s-%s%s" % (mode["name"], time.strftime("%Y%m%d-%H%M%S"),
                                    mode["suffix"]))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    shutil.copyfile(mode["source"], out)
    ok = True

    # The timeline, saved beside the table it explains. Reported rather than
    # failed when absent: a sweep that produced a good table and no timeline is
    # still a useful sweep, and saying so is better than a red run.
    if mode is SWEEP:
        if os.path.isfile(REGISTRATIONS):
            reg_out = os.path.join(
                os.path.dirname(out),
                "alttl-registrations-%s.tsv" % time.strftime("%Y%m%d-%H%M%S"))
            shutil.copyfile(REGISTRATIONS, reg_out)
            with open(reg_out, encoding="utf-8", errors="replace") as fh:
                rows = max(0, len(fh.read().splitlines()) - 1)
            print(f"      registration timeline: {rows} row(s) -> {reg_out}",
                  flush=True)
        else:
            print("      WARNING: no registration timeline was written",
                  flush=True)
    with open(out, encoding="utf-8") as fh:
        body = fh.read()
    if mode is DUMP:
        try:
            import json
            shape = "%d levels, valid JSON" % len(json.loads(body)["levels"])
        except (ValueError, KeyError) as bad:
            shape = f"UNREADABLE: {bad}"
    elif mode is SURVEY:
        threw = body.count("result-threw")
        shape = (f"{len(body.splitlines()) - 1} rows, "
                 + (f"{threw} prefab(s) THREW - check which DLC is installed"
                    if threw else "no prefab threw"))
    else:
        try:
            import json
            rows = len(json.loads(body)["levels"])
            shape = f"{rows} levels, valid JSON"
        except ValueError as bad:
            shape = f"INVALID JSON: {bad}"
    advice = {
        # The one that can destroy work. A fresh full sweep drops the phased
        # controllers the 2026-09-08 audit restored by hand and puts back the
        # prefab ghosts it removed, so copying it over levels.json silently
        # undoes both.
        "levelsweep": ("MERGE it with tools/merge-levels.py, do not copy it "
                       "over apworld/alttl/data/levels.json - a fresh sweep "
                       "REGRESSES the hand-audited phased levels"),
        "solutions": ("diff it against fixtures/controller-survey.tsv before "
                      "replacing that file"),
        "dump": ("feed it to tools/check-game-facts.py, which is what reads "
                 "this"),
    }[mode["command"]]
    print(f"Done: {mode['command']} wrote {out} ({shape}) - {advice}",
          flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
