"""Every DevTools command is dispatched AND documented, or this fails.

WHY THIS EXISTS. ALTTLDevTools references the game's assemblies, so it
cannot be built in CI and has no test project and never will - the
interop assemblies must not be committed. That leaves its commands
covered by nothing at all, in a plugin whose entire job is producing the
measurements this project treats as fact.

The bug class it guards is the one that has already bitten twice:

  * DlcGuard carried Harmony attributes and was missing from the list of
    classes Plugin patches, so none of them were ever applied. Nothing
    noticed until tools/check-patches.py was written for it.
  * `marksolved:` was documented as `solve:` and could never run under
    that name, because `solve:` matches earlier in the ladder.

Both are the same shape: a command that exists in one place and not the
other. This compares the ladder against the documentation in both
directions, which is all a static check can honestly do here.

    py -3.13 tools/check-devtools.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
PLUGIN = os.path.join(REPO, "src", "ALTTLDevTools", "Plugin.cs")
DOC = os.path.join(REPO, "docs", "devtools.md")

#: Ladder entries that are deliberately undocumented, with the reason.
#: Keep this short - an entry here is a command a user cannot discover.
UNDOCUMENTED_OK = set()

#: Documented names that are not ladder keywords: prose, file names, or a
#: command implemented somewhere other than the if/else chain.
NOT_A_COMMAND = {
    "off",          # the suffix half of `cardlabels:off` and friends
    "list",         # the suffix half of `inert:list`
}


def dispatched():
    """Command keywords the if/else ladder actually recognises."""
    with open(PLUGIN, encoding="utf-8") as fh:
        src = fh.read()
    out = set()
    for name in re.findall(r'cmd\.(?:Equals|StartsWith)\("([^"]+)"', src):
        # Compare on the part before the colon, on both sides. The ladder
        # dispatches `inert:` as one entry and the doc lists `inert:list`,
        # `inert:<controller>` and `inert:off` as three rows; they are one
        # command. Splitting here is what keeps those from reading as
        # three phantom entries.
        out.add(name.split(":")[0].lower())
    return out


def documented():
    """Commands named in the first column of the doc's command tables."""
    out = set()
    with open(DOC, encoding="utf-8") as fh:
        for line in fh:
            if not line.startswith("|"):
                continue
            first = line.split("|")[1]
            for token in re.findall(r"`([^`]+)`", first):
                # `boot:<index>[:<seed>]` -> boot ; `menu:title` -> menu
                name = token.strip().split()[0]
                name = re.sub(r"[<\[].*$", "", name)
                name = name.split(":")[0].lower()
                if name and name not in NOT_A_COMMAND:
                    out.add(name)
    return out


def main():
    live = dispatched()
    docd = documented()
    if not live:
        sys.exit("check-devtools: found no commands in the ladder - the "
                 "dispatch shape changed and this check is now blind")

    problems = []

    missing_docs = sorted(live - docd - UNDOCUMENTED_OK)
    for name in missing_docs:
        problems.append(f"  {name}: dispatched but not in docs/devtools.md")

    # The marksolved bug, in the direction that actually bit: documented
    # under a name nothing dispatches, so it silently never runs.
    phantom = sorted(docd - live)
    for name in phantom:
        problems.append(f"  {name}: documented but no ladder entry "
                        f"recognises it - it cannot run")

    if problems:
        print(f"check-devtools: {len(problems)} problem(s)", flush=True)
        print("\n".join(problems), flush=True)
        sys.exit(1)

    print(f"check-devtools: {len(live)} commands, all dispatched and "
          f"documented", flush=True)


if __name__ == "__main__":
    main()
