# The thirteen ability icons

What the level-select ability strip draws, where each picture came from,
and what was tried and rejected. Written down because the search was the
expensive part: finding these took four passes over the game's loaded
sprites and sixteen puzzles opened one at a time.

The game has **no per-mechanic art**. Abilities are the mod's invention -
the game has `ObjectController` classes, not "abilities" - so there is
nothing to look up, only things to borrow.

## What ships

`src/ALTTLArchipelago/Icons/*.png`, embedded in the DLL, 145 KB for all
thirteen. Loaded by `Badges.LoadPillArt`.

Twelve shipped with the base game's mechanics. The thirteenth, Distributing,
arrived with Seeing Stars and is the only DLC mechanic with no base-game
equivalent - the other four new controller classes are a drawer, a Shuffleables
subtype, what GridPuzzle derives from, and a one-level gadget.

| Ability | File | Source sprite | Where it came from |
|---|---|---|---|
| Swapping | `Books.png` | `Badge1-Books2` | badge element |
| Stacking | `Cartridges.png` | `Badge2-NES` | badge element |
| Ordering | `Pencils.png` | `Badge1-Pencils` | badge element |
| Gadgets | `Lightbulb.png` | `badge3-lightbulb` | badge element |
| Rotating | `Record.png` | `Badge1-Record` | badge element |
| Sticking | `Stickers.png` | `Badge1-Stickers` | badge element |
| Grids | `GridTile.png` | `1x1-1` | Procedural Grid Puzzle |
| Tidying | `Breadtag.png` | `Breadtag-red` | Breadtags |
| Containers | `EggCarton.png` | `Carton-front copy` | Fridge (Something Eggstra) |
| Drawer | `Drawer.png` | `Drawer-Top+Bottom` | Tool Drawer (Drawer Chores) |
| Symmetry | `Wreath.png` | `Wreath` | Wreath (Good Tidings) |
| Jigsaw | `Gingerbread.png` | `GingerbreadMan` | Cookies Jigsaw (Good Tidings) |
| Distributing | `Pizza.png` | `Pizza` | Pizza (Seeing Stars) |

Two kinds, and the distinction matters:

- **Badge elements** are the small item pictures that sit ON a badge.
  The assembled badges are separate sprites - `Badge1` through `Badge8`
  are the frames, `Badge1-BG` the backing, `Badge1-Banner` the ribbon -
  and none of those are used, so badges stay free for whatever they may
  become in the randomizer.
- **Puzzle objects** are lifted out of a puzzle that uses that mechanic
  and **nothing else**, generator puzzles preferred over campaign ones,
  so the picture and the lock mean the same thing.

## Why they ship as files rather than being found at runtime

The six puzzle objects are **Addressable assets**. The game loads a
level's art when the level opens and releases it when it closes
(`ReleaseAddressable`, `ClearLoadedAddressables`, `UnloadAssets`), so
they are not in memory on the level select - which is the only screen
that needs them.

Measured rather than assumed: with the icons looked up at runtime, **six
of twelve resolved** on the level select and the other six drew as
lettered plates.

The alternative was to catch each sprite as it passed and hold a managed
reference, which keeps it alive past the release. That works, but it
means the strip fills in gradually as a player happens to visit the right
puzzles, and a legend that is incomplete for hours is worse than one
drawn from files.

## The tools this needed

All in `src/ALTTLDevTools/Plugin.cs`:

- `sprites <filter>` - every loaded sprite name. 998 at the title screen.
- `spritegrid:<names>` - draws a batch on screen at full size AND at icon
  size. A name says nothing about silhouette or how something reads at
  twenty pixels, and this file's history contains two wrong guesses at a
  sprite name.
- `loadlevel:<index>` - opens any level by index. **Run it with no run
  active**: `Track`'s `StartLevel` prefix rewrites the index to whatever
  slot the run intends, and returns early only when there is no run.
- `newsprites` - what has appeared since the last call. Loading a level
  adds its objects, so the difference is exactly that level's art.
- `spriteexport:<names>|<dir>` - writes them out as PNG.

`spriteexport` goes through a RenderTexture because the game's textures
are not readable, and crops to `textureRect` because they are atlased.
**The crop has to invert y**: `Graphics.Blit` writes the RenderTexture
upside down on D3D relative to the source while `textureRect` is measured
from the bottom, and reading at the rect as given returns a neighbouring
sprite - asking for stacked books and getting a bottle opener.

## Dead ends

Recorded so nobody spends the time again.

- **The Calendar stickers, the Shells shells, and the dirty paw prints
  all export blank.** They are white masks the game tints at runtime, so
  there is no colour in the sprite to take. Breadtags cover Tidying and
  Buttons cover Sticking instead - both still generator puzzles that use
  only that mechanic.
- **The Microscope has no microscope.** Its puzzle pieces are crystal
  rings (`Inner-N`, `Mid-N`, `Outer-N`), shards, and a transparent
  `lens`; the instrument itself is scenery, not a puzzle object.
- **The clock cannot be used.** `bighand_straight`, `littlehand_straight`
  and `Pin` are the separate hands - there is no clock face, and the
  parts only read as a clock assembled.
- **`badge7-spider` is not the symmetry puzzle.** Symmetry is `Shells`,
  `Clover`, `Seed Pods` and `Wreath (Good Tidings)`.
- **`Gems (Jigsaw)` is a Grids puzzle**, despite the name.
- **The pizza TOPPINGS, all forty-eight of them.** Pepperoni, jalapeno,
  mushroom, olive, basil and bacon are what Distributing actually moves, and
  every one is a small disc - at icon size they are dots, indistinguishable
  from each other and from anything else round. The pizza in its pan is the
  surface rather than the distributables, and it is the picture that reads.
  The mapping was always a metaphor; a record is not rotation either.
- **Badge elements too thin or too low-contrast to survive 32 pixels**,
  tested on screen: `Badge5-hammer`, `Badge4-Nails`, `Badge5-Callipers`,
  `Badge4-keys2`, `badge3-dice`, `badge3-Scissors`, `Badge5-Cord`,
  `badge3-paperclip`, `Badge2-Crumbs`, `badge3-cloth`, `Badge2-Cutlery`,
  `Badge2-Whisk`, `Badge4-Screws`, `badge6-pawprints1`.

## Single-ability puzzles, which is how the objects were chosen

Generator first, then campaign, then event packs. The ability with the
fewest doors is the one whose icon was hardest to find.

| Ability | Generator | Campaign | Event |
|---|---|---|---|
| Swapping | Spice Jars, Books (Randomized) | Books, Books 2, Books 3, Chess Shadows, Cleaning Supplies, Gems (Simple), Jars, Leaves, Pasta | seven |
| Stacking | - | Cat Food Cans, Fridge Inside, NES Games, Stacked Papers | Sandwich |
| Ordering | Pencils (Randomized) | Eggs, Keys, Pencils 3 | - |
| Gadgets | Microscope, Telescope | Candles, Matchboxes | Candy |
| Rotating | Clock | Angled Image Frame, Cat Frame, Frame Maze, Multiple Frames, Radial Dance Party | - |
| Grids | Procedural Grid Puzzle | Boxes (Stacked), Rock Collection, Rock Gradient | Cookies, Presents Stacked, Painted Eggs |
| Tidying | Breadtags, Trim Plant | Coins 2 (Dirtyness), Paw Prints, Trim Plant (Vines) | - |
| Containers | - | - | Fridge (Something Eggstra) |
| Drawer | - | Workbench | - |
| Sticking | Buttons, Calendar | Storage Boxes | - |
| Symmetry | Shells | Clover, Seed Pods | Wreath |
| Jigsaw | - | - | Cookies Jigsaw, Bones |

Containers and Drawer have exactly one each, and Jigsaw has none
outside the event packs - which is why those three took the longest.
