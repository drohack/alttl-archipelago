"""The shipped player template must stay in step with the options.

player.yaml is a release asset - one of the three files a player is handed -
and it is the one nothing else touches, so it is the one that rots. A template
that quietly omits an option is not obviously broken: generation succeeds using
the default, and the player never learns the setting exists. A template naming
an option that has been REMOVED is worse, because Archipelago rejects the yaml
and the player has no idea which line to delete.

So the template is pinned to ALTTLOptions here, both directions.

It deliberately does NOT ship inside the .apworld - see
tools/build_apworld.py - but it lives in the package so this test can find it
without knowing where the repo root is.
"""

import pathlib
import unittest

from Options import PerGameCommonOptions

from ..options import ALTTLOptions

YAML = pathlib.Path(__file__).resolve().parent.parent / "player.yaml"


def _game_section() -> dict:
    """Parse the game's block without a yaml dependency.

    Deliberately crude: it reads `key: value` and `- item` lines under the
    "A Little to the Left:" heading. A real parser would be better, but
    Archipelago's own test environment is the only place this runs and adding
    a dependency to it for one file is not worth it. What matters here is
    WHICH KEYS are present, and this reads those exactly.
    """
    lines = YAML.read_text(encoding="utf-8").splitlines()
    out = {}
    inside = False
    key = None
    for line in lines:
        if line.startswith("A Little to the Left:"):
            inside = True
            continue
        if not inside:
            continue
        if line and not line[0].isspace():
            break                       # a new top-level key ends the block
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped.startswith("- "):
            if key is not None:
                out.setdefault(key, []).append(stripped[2:].strip())
            continue
        if ":" in stripped:
            key, _, value = stripped.partition(":")
            key = key.strip()
            value = value.strip()
            out[key] = value if value else []
    return out


class TestPlayerYaml(unittest.TestCase):
    def setUp(self):
        self.section = _game_section()
        # THIS world's options only. ALTTLOptions inherits the whole of
        # PerGameCommonOptions - plando, item links, start inventory and the
        # rest - and those are Archipelago's to document, not this template's.
        # The first version of this test demanded all of them and failed
        # asking for a description of "plando_items".
        common = set(PerGameCommonOptions.type_hints)
        self.options = set(ALTTLOptions.type_hints) - common
        self.common = common

    def test_the_template_exists_and_parses(self):
        self.assertTrue(YAML.is_file(), f"no template at {YAML}")
        self.assertTrue(self.section, "the game's block is empty")

    def test_every_option_is_offered(self):
        """A missing option is a setting the player never finds out about."""
        missing = sorted(self.options - set(self.section))
        self.assertFalse(
            missing,
            f"player.yaml does not mention {missing}. Add them, with the "
            f"range and a one-line description, or a player will never know "
            f"they exist.")

    def test_nothing_is_offered_that_does_not_exist(self):
        """A removed option makes Archipelago reject the whole yaml."""
        extra = sorted(set(self.section) - self.options - self.common)
        self.assertFalse(
            extra,
            f"player.yaml offers {extra}, which ALTTLOptions does not define. "
            f"Archipelago rejects a yaml naming an unknown option.")

    def test_the_values_are_the_documented_defaults(self):
        """The template must generate untouched, and match what the webhost
        would hand out - otherwise two players reading the same docs get
        different seeds depending on where they got their yaml."""
        for name, option in ALTTLOptions.type_hints.items():
            if name not in self.options:
                # A common option's default is Archipelago's business, and
                # some of them are enum members whose default is an int while
                # the yaml spells the name ("accessibility: full" against a
                # default of 0). Not this template's contract to pin.
                continue
            default = getattr(option, "default", None)
            if default is None:
                continue
            written = self.section.get(name)

            if isinstance(written, list):
                self.assertEqual(
                    sorted(written), sorted(str(v) for v in default),
                    f"{name}: template lists {sorted(written)}, "
                    f"default is {sorted(str(v) for v in default)}")
                continue

            if isinstance(default, bool) or written in ("true", "false"):
                self.assertEqual(
                    written, "true" if default else "false",
                    f"{name}: template says {written}, default is {default}")
                continue

            self.assertEqual(
                str(written), str(default),
                f"{name}: template says {written}, default is {default}")

    def test_the_required_version_is_the_declared_minimum(self):
        """A template requiring a version older than the world supports lets a
        player generate against an Archipelago this world was never tested on."""
        import json
        manifest = json.loads(
            (YAML.parent / "archipelago.json").read_text(encoding="utf-8"))
        text = YAML.read_text(encoding="utf-8")
        self.assertIn(f"version: {manifest['minimum_ap_version']}", text)
