# Backlog

Things droha has asked for that are NOT bugs in flight. Raised 2026-09-10,
mid-playtest, and parked deliberately so the cat trap work could finish.

Nothing here is started. Each entry records what was asked and what is
already known about it, so picking one up does not begin with rediscovery.

---

## 1. Level endings are inconsistent

Some levels finish on the three-button panel - restart, pause menu, next
arrow - and others drop you straight into the next puzzle with no panel.

droha: "why is that? We might want to make that the same across all levels."

Unknown which levels do which, and whether the difference is the game's own
(a property of the level type) or something the mod's navigation introduces.
`Navigation.AfterGetNextLevelIndex` and the post-level screen are the places
to look; `TitleScreen.QueueSlotForGameplay` and the daily guard both redirect
level launches and could plausibly skip the panel.

Answer that first - if it is the game's own behaviour, making it uniform
means suppressing a screen the game wants to show, which is a different and
bigger decision than fixing a mod inconsistency.

## 2. A "levels beaten / needed" counter

Show progress toward the credits somewhere on the level select.

The number already exists - the beaten toast reads "Puzzle beaten (1/40)" -
so this is presentation, not bookkeeping. `Badges` already draws the
connected tag on the level select and would be the natural home.

## 3. Gate the credits on STARS rather than levels beaten

As an option, not a replacement: finish the credits requirement when N levels
have at least one star.

Worth knowing before designing: the goal currently counts `Level Beaten`
event items, and `rules.py` builds the access rule from that count. A star is
a different condition - every location on a slot - so this changes what the
generator must prove reachable, not just what the mod displays. It is a logic
change, not a UI one.

## 4. Show ability locks on the level select

Icons for the twelve mechanics, so you can see at a glance which you hold.

`Inventory` already knows, and `AbilityState` distinguishes held from the
seed's starting set. Placement is the open question; the level select is
already carrying badges, an overview strip and a connected tag.

---

## Yaml and option surface

droha read through `player.yaml` during the playtest. Verbatim, because the
wording is the point:

- **`mechanic_coverage: 6`** - "what is this? Should it be defaulted to 6 so
  it's as random as possible?" It reserves slots so each mechanic no
  generator can make - stacking, containers, drawers, jigsaws - is guaranteed
  to appear. Higher is MORE guaranteed, not more random, so 6 is the least
  random setting rather than the most. Either the name or the comment is
  misleading and it should not need explaining.
- **`pack_size`** - "why does the comment say 1 to 10 when we want a minimum
  4?" Because the range is the option's, and the floor is applied afterwards
  in `items.MIN_OPENING` (now 5). A player picking 1 silently gets 5. The
  comment should say so.
- **`archive_packs`** - "why is it a bullet point list? Shouldn't it be an
  array? And the comments should say the options." It is a YAML sequence,
  which is valid, but the flow style `[a, b, c]` reads better for a fixed
  set. The valid keys are in `data.ARCHIVE_PACKS` and are not written down
  anywhere a player sees.
- **`generator_weight`** - "kind of a bad name, as it's generated random
  levels." Renaming moves an option key and breaks existing yamls, so the
  comment is the cheaper fix unless it is done alongside something else that
  already breaks them.

droha: "looking through the comments for all of these and making them easier
to read and understand would be good." Treat that as the actual task - the
four above are examples, not the whole of it.
