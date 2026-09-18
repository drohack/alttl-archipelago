"""SlotData's defaults must match the yaml option defaults they stand in for.

WHY THIS EXISTS. SlotData.cs states the rule in its own docstring:

    DEFAULTS MATTER. A server that predates a field, or a hand-edited
    payload, leaves it missing rather than wrong - so every property
    defaults to the same value the corresponding yaml option defaults to.

Two of them did not. `pack_size` defaulted to 4 against an option default of
5, and `cat_trap_chance` to 10 against 25. Neither was caught, because the
only test that looks at them ASSERTED THE DRIFTED VALUES - it pinned the bug
in place rather than finding it. A rule stated in prose and contradicted in
code, with a test defending the contradiction, is worth a tool.

It bites exactly where it is least welcome. These defaults apply only when a
key is MISSING, which is the degraded payload the class exists to survive, and
`pack_size` feeds TrackState's no-boundaries fallback - so it decides how much
of the track opens in the one case where nothing else can.

WHAT IT COMPARES, and what it deliberately does not. Only properties whose C#
default is a simple literal AND whose snake_case JSON name maps to an option
class of the same name in PascalCase. Collections, strings with no option, and
anything it cannot read confidently are REPORTED AS SKIPPED rather than passed
over in silence - a checker that quietly compares nothing is the failure mode
this whole audit was about.

    py -3.13 tools/check-slot-defaults.py
"""
import pathlib
import re

REPO = pathlib.Path(__file__).resolve().parent.parent
SLOTDATA = REPO / "src" / "ALTTLArchipelago.Core" / "SlotData.cs"
OPTIONS = REPO / "apworld" / "alttl" / "options.py"

#: Properties with no yaml option behind them, and why. Listed so the skip is
#: a decision on the record rather than an accident of the parser.
NO_OPTION = {
    "world_version": "the apworld's own version, not a player setting",
    "slots": "the draw, computed by the generator",
    "pack_total": "derived from puzzle_count and pack_size",
    "pack_boundaries": "computed ramp, sent rather than recomputed",
    "abilities": "the ability table, not a setting",
    "starting_abilities": "which ones were drawn, not how many",
    "requirements": "generated access rules",
    "controller_groups": "the level table",
    "dlc": "which DLC a level needs, a property of the level not a setting",
}


def csharp_defaults(text):
    """[(json_name, csharp_literal)] for properties with a literal default."""
    out = []
    pattern = re.compile(
        # [a-z_0-9]+, not [a-z_]+. A json name containing a digit was
        # invisible here and was skipped WITHOUT SAYING SO - the exact
        # 'quietly compares nothing' failure this file exists against.
        # Found when dlc1 and dlc2 were added and neither showed up in
        # the output at all, not even as a skip. They have since been
        # renamed to match their options, but the blind spot was real.
        r'\[JsonPropertyName\("([a-z_0-9]+)"\)\]\s*\n'
        r'\s*public\s+(?:int|bool|string)\s+\w+\s*\{\s*get;\s*set;\s*\}'
        r'\s*=\s*([^;]+);')
    for name, literal in pattern.findall(text):
        out.append((name, literal.strip()))
    return out


def option_defaults(text):
    """{ClassName: default literal} for every option class.

    Three shapes have to be handled, and each was found by this tool reporting
    a mismatch that turned out to be its own fault:

    - a trailing `# comment` on the default line, which otherwise becomes part
      of the value and can never equal anything;
    - a Choice, whose `default = 0` means whichever `option_<name> = 0` it
      matches - the mod stores that NAME as a string, not the number;
    - DefaultOnToggle and friends, which state their default by base class
      rather than with a `default =` line at all.
    """
    out = {}
    for block in re.finditer(r"^class (\w+)\(([^)]*)\)", text, re.MULTILINE):
        name, bases = block.group(1), block.group(2)
        start = block.start()
        nxt = text.find("\nclass ", start + 1)
        body = text[start:nxt if nxt > 0 else len(text)]

        m = re.search(r"^\s+default\s*=\s*(.+?)\s*$", body, re.MULTILINE)
        if m:
            value = m.group(1).split("#")[0].strip()
            if "Choice" in bases:
                named = re.search(
                    r"^\s+option_(\w+)\s*=\s*" + re.escape(value) + r"\s*$",
                    body, re.MULTILINE)
                if named:
                    value = named.group(1)
            out[name] = value
        elif "DefaultOnToggle" in bases:
            out[name] = "True"
        elif "DefaultOffToggle" in bases or bases.strip() == "Toggle":
            out[name] = "False"
    return out


def pascal(snake):
    return "".join(part.capitalize() for part in snake.split("_"))


def same(cs, py):
    """Do a C# literal and a Python literal mean the same value?"""
    cs = cs.strip().strip('"')
    py = py.strip().strip('"').strip("'")
    if cs in ("true", "false"):
        # A Toggle's default is 1/0 or True/False depending on the class.
        return (cs == "true") == (py in ("1", "True"))
    return cs == py


def main():
    slot = SLOTDATA.read_text(encoding="utf-8")
    opts = OPTIONS.read_text(encoding="utf-8")
    options = option_defaults(opts)

    compared, skipped, bad = [], [], []
    for json_name, literal in csharp_defaults(slot):
        if json_name in NO_OPTION:
            skipped.append(f"{json_name} - {NO_OPTION[json_name]}")
            continue
        cls = pascal(json_name)
        if cls not in options:
            skipped.append(f"{json_name} - no option class {cls} to compare")
            continue
        if same(literal, options[cls]):
            compared.append(f"{json_name} = {literal}")
        else:
            bad.append(f"{json_name}: SlotData.cs says {literal}, "
                       f"{cls}.default says {options[cls]}")

    for line in compared:
        print(f"  ok      {line}")
    for line in skipped:
        print(f"  skipped {line}")

    if not compared:
        print("FAIL: nothing was compared at all - the parser has probably "
              "stopped matching SlotData.cs, which is the same as no check",
              flush=True)
        return 1

    if bad:
        print("", flush=True)
        for line in bad:
            print(f"  MISMATCH {line}", flush=True)
        print("\nSlotData.cs states that every property defaults to its yaml "
              "option's default. Fix whichever side is wrong - and check the "
              "test that asserts it, which is how the last drift survived.",
              flush=True)
        return 1

    print(f"\nPASS: {len(compared)} default(s) agree with options.py, "
          f"{len(skipped)} skipped", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
