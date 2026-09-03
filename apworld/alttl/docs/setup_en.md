# A Little to the Left Setup Guide

> **This randomizer is not finished.** The world generates seeds, but the game
> mod that plays them is still being built, so there is nothing to connect with
> yet. This guide describes the intended setup and will be accurate at release;
> until then, treat it as a preview rather than instructions.

## Required Software

- [A Little to the Left](https://store.steampowered.com/app/1629520/) on PC.
  Only the base game is needed - DLC is not used.
- The A Little to the Left Archipelago mod, from the
  [releases page](https://github.com/drohack/alttl-archipelago/releases).
- [BepInEx 6 (IL2CPP)](https://builds.bepinex.dev/projects/bepinex_be), which
  the mod release includes.

## Installing

1. Close the game.
2. Extract the release zip into the game's install folder, so that
   `BepInEx/plugins/ALTTLArchipelago/` sits next to the game executable. On
   Steam, right-click the game, then Manage, then Browse local files.
3. Start the game once and let it finish loading. The first BepInEx launch
   takes noticeably longer than usual while it generates its interop files -
   this is normal and only happens once.
4. **Launch the game with no extra command-line arguments.** Custom launch
   options make Steam show a confirmation dialog before the game starts.

## Configuring your YAML file

### What is a YAML file and why do I need one?

Your YAML file contains the settings for your copy of the game. See the
[basic multiworld setup guide](/tutorial/Archipelago/setup/en) for more.

### Where do I get a YAML file?

From the [player options page](/games/A%20Little%20to%20the%20Left/player-options).

### Settings worth knowing about

- **Puzzle Count** is how long the run is. The full game is 79.
- **Puzzles Per Pack** is how many puzzles the *first* packs open. Packs widen
  as the run goes on, so this sets the opening pace rather than the whole game.
- **Mechanic Coverage** is the main variety lever. Four mechanics - stacking,
  containers, drawers and jigsaws - have no procedural generator, so hand-made
  campaign puzzles are the only way to see them. This setting is how many
  puzzles are guaranteed for each. Set it to 0 and the run is generated and
  event puzzles only, at the cost of losing those four mechanics.
- **Ability Locks** off makes every mechanic work from the start, leaving the
  packs as the only gate. Puzzles can no longer be partly solved.
- **Event Packs** chooses which seasonal Archive packs may appear. Jigsaws only
  exist in event packs, so turning enough of them off drops jigsaws from the
  run and the ability with them.

## Joining a MultiWorld Game

1. Start the game and choose **Archipelago** from the main menu.
2. Enter the server address and port, your slot name, and the room password if
   there is one.
3. Connect. Your run appears on the level select in place of the campaign.

Your randomized progress is kept separately from your normal save, so playing a
multiworld will not disturb a campaign already in progress.

## Troubleshooting

**The Archipelago option is not on the main menu.** The mod did not load. Check
that `BepInEx/plugins/ALTTLArchipelago/` exists inside the game folder, and
look at `BepInEx/LogOutput.log` for lines from `ALTTLArchipelago`.

**The game hangs on a Steam dialog at startup.** Custom launch options are set.
Clear them and start the game normally.
