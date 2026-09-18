"""Every patch and every tick is actually wired in.

WHY THIS EXISTS. DlcGuard was written with two Harmony prefixes and never
added to the list of classes Plugin patches, so NONE of its attributes were
ever applied - only its Tick, which Update calls directly, ever ran. It stayed
that way through a whole feature's development. Worse than the bug: the file
grew a confident comment explaining that its prefix "sees NOTHING on the DLC
route ... so every one of them came through the generic overload", which read
like a measurement and was an explanation for a patch that had never been
installed. Two more comments were then written on top of that one.

The game said so at every launch and nobody read it. Plugin prints

    features live: save redirect, connection pane, track, skips, hints,
                   navigation, daily guard, title screen

and "dlc guard" was not in it. That is the whole detection story, and it cost
a screenshot from droha to find instead.

So this checks the two ways a feature can be written and never run:

  1. a class carrying [HarmonyPatch]/[HarmonyPrefix]/[HarmonyPostfix] that
     Plugin never passes to harmony.PatchAll, and
  2. a Tick-style method that Plugin's frame loop never calls.

Neither needs the game, so it runs in CI beside check-docs.py.

    py -3.13 tools/check-patches.py

Exits non-zero if anything is defined but never wired.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "ALTTLArchipelago")
PLUGIN = os.path.join(SRC, "Plugin.cs")

#: Ticks the loop deliberately does not call, with the reason. Anything not
#: listed here has to be called, or this fails.
TICK_EXEMPT = {
    # Plugin's own helpers - these ARE the loop, they are not called by it.
    ("Plugin", "TickRetry"),
    ("Plugin", "TickCredits"),
    ("Plugin", "TickChecks"),
    ("Plugin", "TickItemReplay"),
    ("Plugin", "TickOfflineStart"),
}

HARMONY = re.compile(r"\[Harmony(Patch|Prefix|Postfix|Finalizer|Transpiler)")
TICK = re.compile(r"internal\s+static\s+\w[\w<>?\s]*\s+(Tick\w*)\s*\(")


def classes_with_patches():
    """Source file stem -> how many Harmony attributes it carries."""
    found = {}
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs"):
            continue
        with open(os.path.join(SRC, name), encoding="utf-8") as f:
            text = f.read()
        count = len(HARMONY.findall(text))
        if count:
            # Badges.Cards.cs and friends are partial classes of one type.
            found[name[:-3].split(".")[0]] = count
    return found


def registered_classes(plugin_text):
    """The types Plugin hands to harmony.PatchAll, by C# type name."""
    block = plugin_text.split("harmony.PatchAll", 1)[0]
    # ("save redirect", typeof(SaveRedirect)),
    return set(re.findall(r'typeof\((\w+)\)\s*\)', block))


def ticks_defined():
    out = []
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs"):
            continue
        with open(os.path.join(SRC, name), encoding="utf-8") as f:
            text = f.read()
        owner = name[:-3].split(".")[0]
        for method in TICK.findall(text):
            out.append((owner, method))
    return out


def main():
    plugin = open(PLUGIN, encoding="utf-8").read()
    problems = []

    patched = classes_with_patches()
    registered = registered_classes(plugin)
    for cls, count in sorted(patched.items()):
        if cls not in registered:
            problems.append(
                f"{cls} carries {count} Harmony attribute(s) but Plugin never "
                f"patches it - none of them will ever be installed")
    print(f"  {len(patched)} class(es) carry Harmony attributes; "
          f"{len(registered)} registered", flush=True)

    # A registered class with no attributes is the other half of the same
    # mistake: harmony.PatchAll over nothing, and a feature name in the live
    # list that means nothing.
    for cls in sorted(registered):
        if cls not in patched:
            problems.append(
                f"Plugin patches {cls}, which carries no Harmony attributes - "
                f"the name in 'features live' promises something that is not "
                f"there")

    ticks = ticks_defined()
    uncalled = []
    for owner, method in ticks:
        if (owner, method) in TICK_EXEMPT:
            continue
        # Called as Owner.Method( anywhere in the plugin's frame loop.
        if f"{owner}.{method}(" not in plugin:
            uncalled.append(f"{owner}.{method}")
    print(f"  {len(ticks)} tick method(s) defined, "
          f"{len(ticks) - len(uncalled) - len(TICK_EXEMPT)} called by the loop",
          flush=True)
    for name in uncalled:
        problems.append(f"{name} is defined but the frame loop never calls it")

    if problems:
        print(flush=True)
        for p in problems:
            print(f"FAIL: {p}", flush=True)
        print(f"\n{len(problems)} wiring problem(s). A feature that is never "
              f"installed does nothing and says nothing.", flush=True)
        return 1

    print("\nPASS: every Harmony class is patched and every tick is called.",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
