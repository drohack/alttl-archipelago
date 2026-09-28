"""Every DevTools command is dispatched AND documented, spelled the same, or this fails.

WHY THIS EXISTS. ALTTLDevTools references the game's assemblies, so it
cannot be built in CI and has no test project and never will - the
interop assemblies must not be committed. That leaves its commands
covered by nothing at all, in a plugin whose entire job is producing the
measurements this project treats as fact.

The bug class it guards has bitten three ways:

  * DlcGuard carried Harmony attributes and was missing from the list of
    classes Plugin patches, so none of them were ever applied. Nothing
    noticed until tools/check-patches.py was written for it.
  * `marksolved:` was documented as `solve:` and could never run under
    that name, because `solve:` matched earlier in the old if/else ladder.
  * The doc read `gensweep:<index>` for a command whose argument was a SEED
    COUNT, so the obvious call started some sixteen thousand regenerations.
    A keyword check passed it: the keyword was right, the arguments were not.

So the command table in src/ALTTLDevTools/Plugin.cs carries, for every
keyword, the doc section it belongs in and every spelling the doc gives it,
and this holds the two to each other both ways:

  * each usage in the table appears, exactly, as a first-column token of a
    `| Command | Effect |` row in docs/dev/devtools.md, under the `##`
    heading named after its area;
  * each first-column token in those tables is a usage in the table.

    py -3.13 tools/check-devtools.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
PLUGIN = os.path.join(REPO, "src", "ALTTLDevTools", "Plugin.cs")
DOC = os.path.join(REPO, "docs", "dev", "devtools.md")

ENTRY = re.compile(r'Cmd\(\s*"([^"]+)"\s*,\s*Areas\.(\w+)\s*,\s*new\[\]\s*\{([^}]*)\}', re.S)
AREA = re.compile(r'internal const string (\w+) = "([^"]+)";')
STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')


def table():
    """{keyword: (area title, [usages])} from Plugin.cs, in order."""
    with open(PLUGIN, encoding="utf-8") as fh:
        src = fh.read()
    areas_block = src[src.index("class Areas"):]
    areas_block = areas_block[:areas_block.index("}")]
    areas = dict(AREA.findall(areas_block))
    out = {}
    problems = []
    for keyword, area, braces in ENTRY.findall(src):
        usages = [u.replace('\\"', '"') for u in STRING.findall(braces)]
        if keyword in out:
            problems.append(f"  {keyword}: two table entries")
        if area not in areas:
            problems.append(f"  {keyword}: Areas.{area} is not an area")
        for usage in usages:
            prefix = re.split(r"[:\s\[<]", usage, maxsplit=1)[0]
            if prefix.lower() != keyword.lower():
                problems.append(f"  {keyword}: usage `{usage}` does not start with the keyword")
        out[keyword] = (areas.get(area, area), usages)
    return out, set(areas.values()), problems


def documented():
    """{token: section} for every first-column token of a Command table."""
    tokens = {}
    section = ""
    in_commands = False
    with open(DOC, encoding="utf-8") as fh:
        lines = fh.read().splitlines()
    for i, line in enumerate(lines):
        if line.startswith("## "):
            section = line[3:].strip()
            in_commands = False
            continue
        if not line.startswith("|"):
            in_commands = False
            continue
        cells = re.split(r"(?<!\\)\|", line)
        first = cells[1].strip() if len(cells) > 1 else ""
        # A header row is the one above a |---| separator.
        if i + 1 < len(lines) and re.match(r"^\|\s*-+", lines[i + 1]):
            in_commands = first == "Command"
            continue
        if re.match(r"^\|\s*-+", line) or not in_commands:
            continue
        for token in re.findall(r"`([^`]+)`", first):
            tokens[token.replace("\\|", "|").strip()] = section
    return tokens


def main():
    commands, area_titles, problems = table()
    if not commands:
        sys.exit("check-devtools: found no Cmd(...) entries in Plugin.cs - the "
                 "table's shape changed and this check is now blind")
    docd = documented()

    usages = {}
    for keyword, (area, spellings) in commands.items():
        for usage in spellings:
            usages[usage] = (keyword, area)
            if usage not in docd:
                problems.append(f"  `{usage}` ({keyword}): in the table, not in "
                                f"docs/dev/devtools.md")
            elif docd[usage] != area:
                problems.append(f"  `{usage}` ({keyword}): documented under "
                                f"'{docd[usage]}', its area is '{area}'")

    for token, section in sorted(docd.items()):
        if token not in usages:
            problems.append(f"  `{token}` (under '{section}'): documented, but no "
                            f"table entry has that spelling - it cannot run as written")

    for section in sorted(set(docd.values()) - area_titles):
        problems.append(f"  '## {section}' holds commands but is not an area name")

    if problems:
        print(f"check-devtools: {len(problems)} problem(s)", flush=True)
        print("\n".join(problems), flush=True)
        sys.exit(1)

    print(f"check-devtools: {len(commands)} commands, {len(usages)} spellings, "
          f"all dispatched and documented exactly", flush=True)


if __name__ == "__main__":
    main()
