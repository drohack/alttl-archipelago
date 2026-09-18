"""With both DLC toggles off, this world must be the world that shipped in 0.3.4.

WHY THIS FILE EXISTS. Adding the DLCs grows the level table by more than half,
and everything a player already holds hangs off that table: location ids are
positional (locations.py), item ids are positional (items.py), and a seed in
flight is a set of numbers agreed between a generator and a mod that are no
longer running. "Appending is safe" is the rule, and this file is what turns
that rule from a convention into something that can fail.

THE GOLDENS ARE FROZEN, NOT REGENERATED. fixtures/id-table-0.3.4.json and
fixtures/plan-0.3.4.json were captured from the pre-DLC tree and must never be
rewritten - a regenerated golden agrees with whatever the current code does,
which is precisely the thing being tested. If one of these tests fails, the
answer is a code change, not a new fixture. There is deliberately no
ALTTL_WRITE_GOLDEN path here, unlike test_slot_data.

WHAT EACH ONE CATCHES:

- the id tables catch a DLC name minted in the wrong place: an ability item
  inserted into ABILITY_ITEMS instead of after HINT_PAGE, or DLC levels sorted
  ahead of the Credits location rather than after it.
- the draw catches a change to the random stream. Filtering DLC out in
  slots._eligible happens before any random call, so the candidate lists - and
  therefore every random.choice - should be untouched. "Should be" is what this
  file exists to disbelieve.
"""

import json
import os
import unittest

from . import bases
from .. import items, locations

#: Every configuration/seed pair in the golden is checked. The draw is cheap
#: here because nothing is filled - only the plan is built - so there is no
#: reason to sample. ALTTL_REGRESSION_SEEDS trims a local run to the first N
#: seeds; it is deliberately not read in CI.


def _golden(name):
    path = bases.fixture_path(name)
    if not os.path.isfile(path):
        raise unittest.SkipTest(f"{name} is missing; it is a frozen record of "
                                "the pre-DLC world and cannot be regenerated")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


class TestTheIdTablesNeverMoved(unittest.TestCase):
    """Every id 0.3.4 handed out still means the same thing."""

    @classmethod
    def setUpClass(cls):
        cls.golden = _golden("id-table-0.3.4.json")

    def test_every_pre_dlc_location_keeps_its_id(self):
        moved = []
        for name, was in self.golden["locations"].items():
            now = locations.LOCATION_NAME_TO_ID.get(name)
            if now != was:
                moved.append(f"  {name}: {was} -> {now}")
        self.assertFalse(moved, self._why("location", moved))

    def test_every_pre_dlc_item_keeps_its_id(self):
        moved = []
        for name, was in self.golden["items"].items():
            now = items.ITEM_NAME_TO_ID.get(name)
            if now != was:
                moved.append(f"  {name}: {was} -> {now}")
        self.assertFalse(moved, self._why("item", moved))

    def test_every_pre_dlc_event_name_still_exists(self):
        """Events carry no address, but the mod matches them by name."""
        now = set(locations.EVENT_NAMES)
        missing = [n for n in self.golden["eventNames"] if n not in now]
        self.assertFalse(missing, "event names that disappeared:\n"
                         + "\n".join(f"  {n}" for n in missing))

    def test_new_names_were_appended_after_the_old_ones(self):
        """Anything new sits past the last id 0.3.4 used.

        The stronger statement of the two tests above: not merely that the old
        ids held, but that nothing was interleaved among them, which is the
        shape a future append has to preserve too.
        """
        last = max(self.golden["locations"].values())
        old = set(self.golden["locations"])
        early = sorted(n for n, i in locations.LOCATION_NAME_TO_ID.items()
                       if n not in old and i <= last)
        self.assertFalse(early, "new location names landed among the old ids "
                         "instead of after them:\n"
                         + "\n".join(f"  {n}" for n in early))

    @staticmethod
    def _why(kind, moved):
        return (f"{len(moved)} {kind} id(s) moved, which silently repoints "
                f"every seed already in flight:\n" + "\n".join(moved)
                + f"\n\nIds are positional. A new {kind} name must be APPENDED, "
                "never inserted. Do not regenerate the golden - it is a frozen "
                "record of 0.3.4.")


class TestTheDrawNeverMoved(unittest.TestCase):
    """The same yaml and the same seed still draw the same run."""

    def test_every_configuration_draws_what_it_drew(self):
        # Imported here rather than at module scope: it pulls in Fill and the
        # world test harness, which a pure id-table check has no need of.
        from .test_fill_stress import CONFIGURATIONS, _generate

        golden = _golden("plan-0.3.4.json")
        limit = int(os.environ.get("ALTTL_REGRESSION_SEEDS", 0))
        seeds = golden["seeds"][:limit] if limit else golden["seeds"]

        # Every pair the golden holds for the seeds in play. NOT
        # len(CONFIGURATIONS) * len(seeds): new configurations get added over
        # time - the DLC ones took it from 38 to 47 - and a golden frozen
        # before them is still a complete record of what it recorded. What
        # must not happen is a golden entry going unchecked, which is what
        # this counts.
        expected = sum(1 for key in golden["configurations"]
                       if int(key.rsplit("|", 1)[1]) in seeds)

        differences = []
        compared = 0
        for key, want in sorted(golden["configurations"].items()):
            name, seed = key.rsplit("|", 1)
            if int(seed) not in seeds:
                continue
            if name not in CONFIGURATIONS:
                differences.append(f"  {name}: the configuration is gone")
                continue
            compared += 1
            world = _generate(CONFIGURATIONS[name], int(seed))
            world = world.multiworld.worlds[world.player]
            got = [[s.level.level_id, s.instance, s.seed] for s in world.plan]
            if got != want["plan"]:
                differences.append(
                    f"  {key}: drew {len(got)} slot(s), expected "
                    f"{len(want['plan'])}; first difference at "
                    f"{self._first_difference(got, want['plan'])}")

        # A comparison loop that quietly matched nothing would pass, and a
        # green test that tested nothing is the failure this whole file is
        # insurance against.
        self.assertEqual(expected, compared,
                         f"only {compared} of {expected} golden pair(s) were "
                         "compared; a configuration the golden records has "
                         "gone missing, so this test is guarding less than it "
                         "claims")

        self.assertFalse(
            differences,
            f"{len(differences)} configuration(s) draw a different run than "
            "0.3.4 did with the same seed:\n" + "\n".join(differences)
            + "\n\nWith both DLC toggles off the draw must be identical. A "
            "difference means DLC content reached the random stream - check "
            "that slots._eligible filters it out BEFORE any random call.")

    @staticmethod
    def _first_difference(got, want):
        for i, (a, b) in enumerate(zip(got, want)):
            if a != b:
                return f"slot {i}: {a} instead of {b}"
        return f"slot {min(len(got), len(want))}: one run is longer"
