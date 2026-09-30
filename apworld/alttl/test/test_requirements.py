"""Any location may hold progression, so every requirement has to be right.

Understating a requirement softlocks a seed; overstating is safe. On
2026-09-21 a Progressive Puzzle Pack landed on `Tupperware Nesting - Lids`,
which declared ['Containers'] and could not be touched without Stacking, and
the run was over. The requirement came from levels.json dependsOn, and an edge
the game does not express is simply absent, so nothing downstream could see
it was wrong.

Nothing keeps progression off a location any more (the guard that did was
removed on 2026-09-30, when it covered no location at all). The defence is
the data, and these tests hold it to what play established:

- a group asking for less than its level - a part check, or an ending
  narrowed to its group - must sit on a level whose entry in
  data/proven-requirements.json says, dated, how that was established;
- the requirements play has corrected are pinned by name;
- DLC1's hand-added dependency edges are pinned, so a re-sweep that adds or
  loses one goes red.
"""

from . import bases
from .. import data


#: The hand-test record: level id -> dated evidence. Read by the tests only.
PROVEN = data._load("proven-requirements.json").get("proven", {})


class TestPlayedRequirementsStayPut(bases.ALTTLTestBase):
    """Requirements droha's play corrected. Loosening one needs new play."""

    options = {}

    def test_the_location_that_killed_a_run_needs_its_whole_level(self):
        """Lids asks for everything its level does, from play.

        droha played it three ways on 2026-09-23: holding Containers+Stacking
        the lids stayed game-blocked after every stack, and holding
        Grids+Stacking "the lids showed up finally" only after the food. So
        Lids depends on Food and needs Containers + Grids + Stacking - the
        whole level. If this ever loosens, the check that ended a run is back.
        """
        level = data.BY_ID["TupperwareNesting"]
        self.assertIn("Lids", level.enforced_part_abilities,
                      "TupperwareNesting no longer has a group called Lids - "
                      "if the table was corrected, say so here rather than "
                      "deleting the pin")
        self.assertEqual(
            level.enforced_abilities, level.enforced_part_abilities["Lids"],
            "TupperwareNesting / Lids asks for %s while its level needs %s. "
            "droha found on 2026-09-23 that the lids only appear after the "
            "food; do not loosen this without new play."
            % (sorted(level.enforced_part_abilities["Lids"]),
               sorted(level.enforced_abilities)))
        self.assertEqual({"Containers", "Grids", "Stacking"},
                         set(level.enforced_part_abilities["Lids"]))

    def test_fruit_stickers_cannot_be_peeled_without_sticking(self):
        """Every Fruit Stickers part needs Tidying AND Sticking, from play.

        droha, 2026-09-24, holding only Tidying: "stickers are greyed out and
        i can't peel them". The mod's Sticking lock reaches the peel as well,
        so Remove Stickers asking for Tidying alone let a seed put Sticking
        behind a check that needs Sticking. Holding only Sticking the peel
        works and the stick does not (2026-09-23), so the two parts are one
        mutual group.
        """
        level = data.BY_ID["Fruit Stickers"]
        self.assertEqual({"Sticking", "Tidying"}, set(level.enforced_abilities))
        for part, own in level.enforced_part_abilities.items():
            self.assertEqual(
                level.enforced_abilities, own,
                "Fruit Stickers / %s asks for %s; the stickers cannot be "
                "peeled without Sticking (droha, 2026-09-24)"
                % (part, sorted(own)))


class TestEveryUnderstatementWasPlayed(bases.ALTTLTestBase):
    """A group that needs strictly less than its level claims it can be done
    early. Sometimes that is true, and only play tells which."""

    options = {}

    def test_every_group_asking_less_than_its_level_is_on_a_proven_level(self):
        """New levels, a re-sweep, or an edge removed go red here first.

        The fix is a hand test (CLAUDE.md section 2) and an entry in
        data/proven-requirements.json saying what it found - or an edge in
        levels.json if the group turned out to need more.
        """
        found = {}
        for level in data.LEVELS:
            if level.level_id in PROVEN:
                continue
            parts = sorted(part for part, own
                           in level.enforced_part_abilities.items()
                           if own < level.enforced_abilities)
            if parts:
                found[level.level_id] = parts
        self.assertEqual(
            {}, found,
            "these groups ask for less than their level and no hand test "
            "says they can: play them before they go in a seed")

    def test_the_proven_list_names_levels_that_exist(self):
        for level_id in PROVEN:
            self.assertIn(level_id, data.BY_ID,
                          "proven-requirements.json names %s, which is not a "
                          "level. A typo here silently proves nothing."
                          % level_id)

    def test_every_proven_level_carries_its_evidence(self):
        for level_id, evidence in PROVEN.items():
            self.assertIn("2026", evidence,
                          "%s is proven without a dated record" % level_id)
            self.assertGreater(len(evidence), 60,
                               "%s: 'it looks fine' is not evidence" % level_id)


class TestDlc1EdgesAreTheOnesAddedByHand(bases.ALTTLTestBase):
    """THE CANARY for DLC1's harvest, which recorded no dependency at all.

    Every other source has some edges (archive 7 of 26, base 7 of 69, dlc2 10
    of 34); DLC1, the source richest in drawers, had none, which is a harvest
    failure rather than 24 levels that happen to be ungated. Every edge below
    was added by hand from play or tools/probe-blocked.py. A level appearing
    here that nobody added is the sweep finally recording what it always
    should have: re-check those levels' requirements then. A level
    disappearing is an edge LOST, the direction that ends runs.
    """

    options = {}

    def test_dlc1_edges_are_only_the_ones_we_added_by_hand(self):
        # DLC1 Lunch Tray LEFT on 2026-09-27, deliberately: droha finished the
        # whole level locks on with Drawer revoked (the trays are also held by
        # the baseline TrayOrganizer, so nothing locks), and relaxed its two
        # edges. Its Trays part still needs Drawer as the opener's own ability.
        added = {
            "DLC1 Boss", "DLC1 Craft Supplies", "DLC1 Fossils",
            "DLC1 Game Pieces", "DLC1 Jewelry Box",
            "DLC1 Kitchen Utensils Drawers",
            "DLC1 Sewing Box",
            # Added 2026-09-22 from droha's own play: "i can move the clocks,
            # but i can't open the cubbord to put the clocks in". Its opener
            # is AnimScrubbables, which maps to Gadgets rather than Drawer.
            "DLC1 Clock Cupboard",
            # Added 2026-09-23 from the locks-off sweep: Tea Cabinet's
            # contents wait on its cupboard doors, Daggers' on its drawers.
            "DLC1 Tea Cabinet", "DLC1 Daggers",
            # Added 2026-09-26 from droha's locks-on play with Drawer revoked:
            # "impossible with drawers greyed out, the only thing to do is to
            # put the peanut in the last drawer". The level has no part
            # checks and its Solution already needed Drawer, so the edge
            # changes no requirement; it records what play found.
            "DLC1 Nested Drawers",
        }
        dlc1 = [raw for raw in data._LEVELS_RAW["levels"]
                if raw["source"] == "dlc1"]
        self.assertEqual(24, len(dlc1))

        with_edges = {raw["levelId"] for raw in dlc1
                      if any(c.get("dependsOn") for c in raw["controllers"])}
        self.assertEqual(
            added, with_edges,
            "the set of DLC1 levels carrying dependency edges moved. If the "
            "sweep was re-run, re-check what those levels' groups ask for; if "
            "an edge was LOST, that is the direction that ends runs.")
