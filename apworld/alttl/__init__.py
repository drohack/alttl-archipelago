"""A Little to the Left - Archipelago world.

This module holds the World and WebWorld classes and nothing else. Every hook
is a one-line delegation into a sibling module, so behaviour is always found
where it belongs rather than accreting here.
"""

from typing import Any, Dict, List, Mapping

from BaseClasses import Item, ItemClassification, Location, Tutorial
from worlds.AutoWorld import WebWorld, World

from . import data, items, locations, options, pool, regions, rules, slots


class ALTTLItem(Item):
    game = "A Little to the Left"


class ALTTLLocation(Location):
    game = "A Little to the Left"


class ALTTLWeb(WebWorld):
    theme = "stone"
    option_groups = options.option_groups
    options_presets = options.options_presets
    #: Rendered as "Report a Bug" on the game's page. Unset, that link simply
    #: does not appear - which is a poor look for a world whose other half is
    #: a BepInEx plugin people will have trouble installing.
    bug_report_page = "https://github.com/drohack/alttl-archipelago/issues"
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to playing A Little to the Left in Archipelago.",
        "English", "setup_en.md", "setup/en",
        ["droha"],
    )]


class ALTTLWorld(World):
    """Tidy a house full of procedurally generated clutter, one pack at a time."""

    game = "A Little to the Left"
    options_dataclass = options.ALTTLOptions
    options: options.ALTTLOptions
    web = ALTTLWeb()

    item_name_to_id = items.ITEM_NAME_TO_ID
    location_name_to_id = locations.LOCATION_NAME_TO_ID

    #: Names a player can use wherever a single item name works: !hint,
    #: start_inventory, item_links, plando. Without these the metaclass
    #: supplies only "Everything", so there was no way to say "any ability"
    #: or "any trap" - and with twelve ability items, that is the group
    #: someone actually wants.
    item_name_groups = items.ITEM_NAME_GROUPS

    #: The same for locations, and it matters more here: a run has up to 432
    #: location names, so `exclude_locations: [Medicine Cabinet]` covering a
    #: whole puzzle is the difference between a usable option and an
    #: unusable one. Archipelago resolves these itself - LocationSet sets
    #: convert_name_groups - so this is the entire implementation.
    location_name_groups = locations.LOCATION_NAME_GROUPS

    # Per-seed state, decided in generate_early and read by everything after.
    plan: List[slots.Slot]
    location_names_in_use: List[str]
    event_names_in_use: List[str]
    requirements: Dict[str, dict]
    live_abilities: List[str]
    starting_abilities: List[str]
    levels_to_beat: int
    levels_to_star: int
    goal_is_stars: bool
    pack_total: int

    def generate_early(self) -> None:
        pool.decide(self)

    def create_regions(self) -> None:
        regions.create_regions(self)

    def create_items(self) -> None:
        pool.create_items(self)

    def set_rules(self) -> None:
        rules.set_all_rules(self)

    def create_item(self, name: str) -> ALTTLItem:
        return ALTTLItem(name, items.classification(name),
                         items.ITEM_NAME_TO_ID[name], self.player)

    def create_event(self, name: str) -> ALTTLItem:
        return ALTTLItem(name, ItemClassification.progression, None, self.player)

    def create_location(self, name: str, address, region) -> ALTTLLocation:
        return ALTTLLocation(self.player, name, address, region)

    def get_filler_item_name(self) -> str:
        return pool.filler_sequence(self, 1)[0]

    def extend_hint_information(self, hint_data: Dict[int, Dict[int, str]]) -> None:
        pool.hint_information(self, hint_data)

    def fill_slot_data(self) -> Mapping[str, Any]:
        return pool.slot_data(self)
