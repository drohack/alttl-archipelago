# Installing

A release is three files, and they carry the same version number on purpose.
Not everybody needs all three.

| File | Who needs it |
|---|---|
| `ALTTLArchipelago-<version>.zip` | every player of this game |
| `A Little to the Left.yaml` | every player of this game |
| `alttl.apworld` | only whoever GENERATES the multiworld |

**The mod and the apworld must be the same version.** They ship together
because they have to agree about the item table, and nothing checks it at
runtime - a mismatch shows up as items that do nothing, or a seed the mod
cannot read.

## 1. BepInEx

The mod is a BepInEx plugin, so BepInEx has to be there first.

1. Get **BepInEx 6 for Unity IL2CPP, x64** - the bleeding-edge builds, not
   BepInEx 5. The game is Unity 2020.3.26f1 / IL2CPP, and BepInEx 5 will not
   load it.
2. Extract it into the game folder: the one containing
   `A Little To The Left.exe`. On Steam that is usually
   `steamapps/common/A Little To The Left`.
3. **Launch the game once and quit.** This is not optional - BepInEx generates
   its interop assemblies on that first run, and the mod cannot load without
   them. The first launch takes noticeably longer than usual.

You should now have a `BepInEx` folder with `config`, `core`, `interop` and
`plugins` inside it.

## 2. The mod

Extract `ALTTLArchipelago-<version>.zip` into the **same game folder**. The zip
is laid out so the files land in the right place:

```
<game folder>/
  A Little To The Left.exe
  BepInEx/
    plugins/
      ALTTLArchipelago/
        ALTTLArchipelago.dll
        ALTTLArchipelago.Core.dll
        ALTTLModKit.dll
        Archipelago.MultiClient.Net.dll
        Newtonsoft.Json.dll
```

Launch the game. The main menu gains an **Archipelago** entry.

## 3. Connecting

Open the Archipelago entry and fill in:

- **Address** - the host and port from the room page, as `host:port`
- **Slot name** - your player name in the multiworld, exactly as in your yaml
- **Password** - only if the room has one

Press **Connect**. The status line says what is happening, and the menu entry
carries a small `connected` / `connecting` / `offline` tag.

Settings are saved to
`BepInEx/config/droha.alttl.archipelago.cfg` and can be edited there too. Leave
**Auto-connect** on and the game rejoins your multiworld every launch.

### If the server is not there

The run still starts, from a cache of your last session, and the menu entry
reads **offline run**. Play normally; checks are queued and sent the moment a
connection is made.

To play the ordinary game instead, turn **Auto-connect** off in the Archipelago
dialog and relaunch. That is currently the only route back to the campaign
save - though the campaign save itself is untouched throughout, because a
randomized run never writes to it.

## 4. Generating a seed (the host only)

1. Put `alttl.apworld` in your Archipelago installation's `custom_worlds/`
   folder.
2. Edit `A Little to the Left.yaml` - at minimum, change `name`.
3. Generate as usual.

The yaml ships with every option at its default and generates untouched. Each
setting carries its range and a one-line description; the full text is on the
webhost's player options page for this game.

Requires Archipelago **0.6.7** or later. That is the version the world is
tested against in CI, and the version the template declares.

## Uninstalling

Delete `BepInEx/plugins/ALTTLArchipelago/`. The campaign save was never
modified, so the ordinary game is exactly where you left it. Randomized runs
live in their own `save_ap_*` files beside it and can be deleted too, or kept
in case you rejoin.

## Troubleshooting

**The Archipelago entry is not on the menu.** The plugin did not load. Check
`BepInEx/LogOutput.log` for `A Little To The Left Archipelago loaded`. If
BepInEx itself is not logging, it is BepInEx 5, or the 32-bit build, or it was
extracted into the wrong folder.

**A feature silently does nothing.** The log lists `features live:` and, if any
failed, `FEATURES DISABLED:`. A game update that renames a method disables one
feature rather than the whole plugin, and that line is where it says so.

**"Archipelago refused the connection".** The slot name or password is wrong,
or the room is for a different game or an incompatible version. This is not
retried, deliberately - only you can fix it.

**Nothing connects and it keeps trying.** That is the default: retries are
unlimited, backing off to a minute. Press **Cancel** to stop.
