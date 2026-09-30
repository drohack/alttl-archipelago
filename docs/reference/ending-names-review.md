# Ending names as built

For droha's review (2026-09-28, playtest items 3-5). Every puzzle's endings,
from `apworld/alttl/data/names.json`. Each ending is one check, named after the
puzzle ("Spoons - Solution: Stacked"), and always the same check whichever
order they are found in. A puzzle with one ending is just "- Solution".

- **Game id**: the solution id the game reports for that ending. *unseen*: nobody
  has seen it yet; until then an id the table does not know fills the next
  "Other" check, so nothing is lost.
- **Other checks on the puzzle**: its part checks, shown once per puzzle. A group
  that is itself an ending (Spoons' stack) is that ending's check, not a part.

## Grouped parts

Several controllers checked as one part (`mergedParts` in levels.json).

| Puzzle | Part | What is in it |
|---|---|---|
| Medicine Cabinet | Red Items | Cup Draggables, Oral Containables, Pick Draggables, Tube Draggables, Contact Lenses Draggables, Brush Draggables, Pump Bottle Draggables |
| Mirror | Still Life | JugPositionController, CandlePositionController, DishPositionController, BottlePositionController, Books + Box StackablesY |
| Mirror | Little Things | LemonWedgePositionController, ContainablesController, CandleStateController |

Medicine Cabinet's other parts stay as they were: Blue Bottles, Creams Stacked, Green Bottles, Swabs, Trinkets.
Mirror's big items in place are its one part ("Still Life": jug, candle, dish,
bottle and the books-and-box stack, needing Stacking). Its little things - the
lemon wedge, the lemon, frond and skull in their containers, the candle put out -
are part of the Solution, no check of their own (`solutionOnlyParts`).
Breadtags' Crumbs and Interlocking are the same: its one check is the
Solution (droha, 2026-09-28: "just have the solution").

## Endings nobody has seen

None. droha played the fourteen that were left on 2026-09-28 (Storage Boxes,
Candles, Buttons, Math Set, Bookshelf, Corn, Ghost Cat, the Boss); the Boss's
Lock and Compass endings came from the game's save, where every found id is kept.

## Every puzzle

### Campaign (69 puzzles)

| Puzzle | Ending | Game id | Other checks on the puzzle |
|---|---|---|---|
| Angled Image Frame | Solution | `Angled_0` | - |
| Batteries | Solution | `Sort-Batteries_0` | - |
| Broken Vase | Solution | `Draggables_0` | - |
| Books | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
| Books 2 | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
| Books 3 | Solution: Design (Shuffle) | `Design-(Shuffle)_0` | - |
|  | Solution: Height | `Height-(Draggables)_1` |  |
| Bowls | Solution: Match 1 | `Match-(Draggables)_0` | Crack, Pattern |
|  | Solution: Match 2 | `Match-(Draggables)_1` |  |
| Boxes (Stacked) | Solution: Stacked Boxes 1 | `Stacked-Boxes_0` | - |
|  | Solution: Stacked Boxes 2 | `Stacked-Boxes_1` |  |
| Candles | Solution: Candles Object 1 | `CandlesObjectController_0` | - |
|  | Solution: Candles Object 2 | `CandlesObjectController_1` |  |
| Cat Food Cans | Solution | `Matching-Stacks_0` | - |
| Cat Frame | Solution | `Straighten_0` | - |
| Cat Toys | Solution | `Tidy-Up_0` | - |
| Chess Shadows | Solution | `Shuffleables-Shadows_0` | - |
| Cleaning Supplies | Solution | `Supplies-Order_0` | Sponge, Supplies Order |
| Clover | Solution | `SymmetricalPlaceables_0` | - |
| Coins 1 (Shape) | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Stacked | `Stacked_0` |  |
| Coins 2 (Dirtyness) | Solution: Dirtyables 1 | `Dirtyables_0` | - |
|  | Solution: Dirtyables 2 | `Dirtyables_1` |  |
|  | Solution: Dirtyables 3 | `Dirtyables_2` |  |
| Colored Pencils | Solution | `Gradient_0` | - |
| Desktop Computer | Solution | `Computer-Desktop_0` | Computer Desktop, Computer Errors, Keys, Paintbrushes, Clock hands, Notes |
| Drink Glasses | Solution | `Backface-Aligned_0` | - |
| Eggs | Solution | `Eggs-Pattern_0` | - |
| Frame Maze | Solution | `Frame_Rotateables_0` | - |
| Fridge Inside | Solution | `Draggables_0` | Draggables, Tupperware, Yogurt / Jam |
| Fruit Stickers | Solution: Match Stickers | `Match-Stickers_0` | - |
|  | Solution: Remove Stickers | `Remove-Stickers_0` |  |
| Front of Fridge 1 (Postcards) | Solution | `Match-Edges_0` | - |
| Front of Fridge 2 (Polaroids) | Solution | `Fit-Rect_0` | - |
| Gems (Jigsaw) | Solution | `GemsDrawer_0` | - |
| Gems (Simple) | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Ordered 3 | `Ordered_2` |  |
| Jars | Solution: Order 1 | `Order_0` | - |
|  | Solution: Order 2 | `Order_1` |  |
|  | Solution: Order 3 | `Order_2` |  |
| Junk Drawer | Solution | `Organizer_0` | - |
| Keys | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Ordered 3 | `Ordered_2` |  |
| Lamp | Solution | `Switches-Toggleables_0` | Light Rotatable, Switches Toggleables |
| Leaves | Solution: Leaves 1 | `LeavesShuffleables_0` | - |
|  | Solution: Leaves 2 | `LeavesShuffleables_1` |  |
|  | Solution: Leaves 3 | `LeavesShuffleables_2` |  |
| Light Bulbs | Solution | `BulbsBox_0` | - |
| Matchboxes | Solution | `MatchboxesObjectController_0` | - |
| Medicine Cabinet | Solution | `Draggables_0` | Blue Bottles, Red Items, Creams Stacked, Green Bottles, Swabs, Trinkets |
| Mirror | Solution | `StackablesY_0` | Still Life |
| Multiple Frames | Solution | `Straighten_0` | - |
| NES Games | Solution | `StackablesY_0` | - |
| Paint Cans | Solution: Match 1 | `Match-(Draggables)_0` | - |
|  | Solution: Match 2 | `Match-(Draggables)_1` |  |
| Parts Organizer | Solution | `Organizer_0` | - |
| Pasta | Solution | `Ordered_0` | - |
| Paw Prints | Solution | `Clearables-01_0` | Clearables 01, Clearables 02, Clearables 03, Coffee Spill, Free Leaves |
| Pencils 3 | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Ordered 3 | `Ordered_2` |  |
| Place Setting | Solution | `Utensils_0` | Placemat, Utensils |
| Plant Pots | Solution | `Interlocking_0` | - |
| Plate | Solution | `Placement_0` | - |
| Post-It Notes Scribble #1 (Simple Line) | Solution | `Arranged_0` | - |
| Post-It Notes Scribble #2 (3x3 Grid) | Solution | `Arranged_0` | - |
| Post-It Notes Scribble #3 (5x3 Grid) | Solution | `Arranged_0` | - |
| Radial Dance Party | Solution | `Radial-Pencils-0_0` | Radial Cat Food Cans 3, Radial Cat Toys 1, Radial Chess 9, Radial Food 4, Radial Ladybugs 8, Radial Nuts & Bolts 6, Radial Pencils 0, Radial Soup Cans 5, Radial Sticky Notes 2, Radial Tools 7 |
| Record Player | Solution | `Record-Player_0` | - |
| Rock Collection | Solution | `Match-Lines_0` | - |
| Rock Gradient | Solution: Gradient 1 | `Gradient_0` | - |
|  | Solution: Gradient 2 | `Gradient_1` |  |
| Seed Pods | Solution | `SymmetricalPlaceables_0` | - |
| Sharp Pencils | Solution: Pencil Order 1 | `Pencil-Order_0` | Shavings Removed |
|  | Solution: Pencil Order 2 | `Pencil-Order_1` |  |
| Soup Cans | Solution: Match Label 1 | `Match-Label-(Draggables)_0` | - |
|  | Solution: Match Label 2 | `Match-Label-(Draggables)_1` |  |
| Spoons | Solution: Size (Elastic) | `Size-(Elastic)_0` | - |
|  | Solution: Stacked | `Stacked_0` |  |
| Stacked Papers | Solution | `Stack-Papers_0` | - |
| Stamps | Solution | `Stamps-Pattern_0` | - |
| Storage Boxes | Solution: Sorting Items 1 | `SortingItems-Draggables_0` | - |
|  | Solution: Sorting Items 2 | `SortingItems-Draggables_1` |  |
| Tacks | Solution | `Colour-&-Count-Sort_0` | - |
| Trim Plant (Vines) | Solution | `Pluckables_0` | - |
| Tupperware Nesting | Solution | `Nested-Tupperware_0` | (Large Square), Food, Lids, Stack 1, Stack 2, Stack 3, Tray |
| Tupperware Tower | Solution | `Tower_0` | - |
| Utensils Drawer | Solution | `Utensils-Drawer_0` | - |
| Wilting Flowers | Solution | `Upright_0` | Cleanable, Upright |
| Workbench | Solution | `ToolsController_0` | - |
| Wrong Aspect Papers | Solution | `Fit-Rect_0` | - |

### Generated puzzles (16 puzzles)

Each id is the same on every seed, by position (Pencils: `Ordered_0` and
`Ordered_1`), even where the seed picks which sorting rules count. Repeated
copies share the names ("Pencils (Randomized) #2 - Solution: Ordered 1").
Books (Randomized): a seed with a symmetric rule checks that rule with a
second controller, and its id `Draggables_0` is that rule wherever the seed
puts it, first or second. The mod reads the seed's two rules when the puzzle
finishes (`Endings.Canonical`) and files it as that rule's Shuffle entry; the
`|Draggables_0` below answers only if the rules cannot be read.

| Puzzle | Ending | Game id | Other checks on the puzzle |
|---|---|---|---|
| Buttons | Solution: Sorting Items 1 | `SortingItems-Draggables_0` | - |
|  | Solution: Sorting Items 2 | `SortingItems-Draggables_1` |  |
| Breadtags | Solution | `Interlocking_0` | - |
| Calendar | Solution | `Repeating-Sequence_0` | - |
| Clock | Solution | `Match-Shadows_0` | - |
| Microscope | Solution | `Build-Snowflakes_0` | - |
| Shells | Solution | `Symmetrical-Placeables_0` | - |
| Spice Jars | Solution: Spice Jar 1 | `SpiceJarShuffleables_0` | - |
|  | Solution: Spice Jar 2 | `SpiceJarShuffleables_1` |  |
| Spider Web | Solution | `Web-Symmetry_0` | - |
| Telescope | Solution | `Constellations_0` | - |
| Trim Plant | Solution | `Pluckables_0` | - |
| Procedural Grid Puzzle | Solution | `GridPuzzle_0` | - |
| Pencils (Randomized) | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
| Post-It Notes (Randomized) | Solution | `Draggable_0` | - |
| Stamps (Randomized) | Solution | `Stamps-Pattern_0` | - |
| Batteries (Randomized) | Solution | `Draggables_0` | - |
| Books (Randomized) | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1\|Draggables_0` |  |

### Archive (event packs) (26 puzzles)

| Puzzle | Ending | Game id | Other checks on the puzzle |
|---|---|---|---|
| Cookies (Good Tidings) | Solution: Row Col Sets 1 | `RowColSets_0` | - |
|  | Solution: Row Col Sets 2 | `RowColSets_1` |  |
|  | Solution: Row Col Sets 3 | `RowColSets_2` |  |
| Popcorn (Good Tidings) | Solution | `TopRow_0` | Bot Row, Mid Row, Top Row |
| Wreath (Good Tidings) | Solution | `SymmetricalPlaceables_0` | - |
| Presents Stacked (Good Tidings) | Solution | `Stacked-Boxes_0` | - |
| Cookies Jigsaw (Good Tidings) | Solution | `Match-Candy-Cane_0` | Match Candy Cane, Match Gingerbread Man, Match Reindeer, Match Snowflake, Match Tree |
| Ornament Box (Good Tidings) | Solution | `BulbsBox_0` | - |
| Painted Eggs (Something Eggstra) | Solution: Row Col Sets 1 | `RowColSets_0` | - |
|  | Solution: Row Col Sets 2 | `RowColSets_1` |  |
| Egg Cups (Something Eggstra) | Solution: Eggs 1 | `Shuffleables-Eggs_0` | - |
|  | Solution: Eggs 2 | `Shuffleables-Eggs_1` |  |
|  | Solution: Eggs 3 | `Shuffleables-Eggs_2` |  |
| Fridge (Something Eggstra) | Solution | `EggsContainable_0` | - |
| Broken Eggs (Something Eggstra) | Solution | `Egg-#1-(Center)-Draggables_0` | Egg #1 (Center), Egg #2 (Left), Egg #3 (Right) |
| Bats (Trick or Tidy) | Solution | `Interlocking_0` | - |
| Bones (Trick or Tidy) | Solution | `Match-Bones_0` | - |
| Candy (Trick or Tidy) | Solution: Grouped 1 | `Grouped_0` | - |
|  | Solution: Grouped 2 | `Grouped_1` |  |
| Chocolate Bars (Trick or Tidy) | Solution: Design (Shuffle) 1 | `Design-(Shuffle)_0` | - |
|  | Solution: Design (Shuffle) 2 | `Design-(Shuffle)_1` |  |
|  | Solution: Design (Shuffle) 3 | `Design-(Shuffle)_2` |  |
|  | Solution: Height | `Height-(Draggables)_0` |  |
| Jack O'Lanterns (Trick or Tidy) | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Ordered 3 | `Ordered_2` |  |
|  | Solution: Ordered 4 | `Ordered_3` |  |
| Candy Canes (Merry Mess) | Solution | `Ordered_0` | Match Candy Cane (1), Match Candy Cane (2), Match Candy Cane (3), Match Candy Cane (4), Match Candy Cane (5), Ordered |
| Crackers (Merry Mess) | Solution | `Build-Train_0` | Build Train, Cracker |
| Snow Globes (Merry Mess) | Solution: Match Pan 1 | `Match-Pan-(Draggables)_0` | - |
|  | Solution: Match Pan 2 | `Match-Pan-(Draggables)_1` |  |
|  | Solution: Match Pan 3 | `Match-Pan-(Draggables)_2` |  |
| Presents (Merry Mess) | Solution | `Presents-Aligned_0` | - |
| Nut Crackers (Merry Mess) | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
|  | Solution: Shuffle 3 | `Shuffle_2` |  |
|  | Solution: Shuffle 4 | `Shuffle_3` |  |
|  | Solution: Shuffle 5 | `Shuffle_4` |  |
| Paper Plane Supplies (Drawer Chores) | Solution | `Draggables_0` | Chalk, Chalk Blue, Chalk Green, Chalk Mint, Chalk Pink, Chalk Purple, Chalk Red, Chalk Yellow, Containables, Draggables |
| Tool Drawer (Drawer Chores) | Solution | `Draggables_0` | Containables, Draggables |
| Bathroom Drawer (Drawer Chores) | Solution | `Draggables_0` | Bottle, Draggables, Indexable |
| Sandwich (Snack Pack) | Solution | `Stack-Ingredients_0` | - |
| Pretzels (Snack Pack) | Solution: Ordered 1 | `Ordered_0` | - |
|  | Solution: Ordered 2 | `Ordered_1` |  |
|  | Solution: Ordered 3 | `Ordered_2` |  |
| Cereal (Snack Pack) | Solution | `Draggables-Controller_0` | - |

### Cupboards and Drawers (25 puzzles)

| Puzzle | Ending | Game id | Other checks on the puzzle |
|---|---|---|---|
| Clock Cupboard (Cupboards and Drawers) | Solution | `Cupboard_0` | - |
| Craft Supplies (Cupboards and Drawers) | Solution | `Drawers_0` | Brushes, Highlighters, Misc, Paints |
| Fountain Pens (Cupboards and Drawers) | Solution: Ordered 1 | `Draggables-Ordered_0` | Containables |
|  | Solution: Ordered 2 | `Draggables-Ordered_1` |  |
|  | Solution: Ordered 3 | `Draggables-Ordered_2` |  |
|  | Solution: Ordered 4 | `Draggables-Ordered_3` |  |
| Pressed Flowers (Cupboards and Drawers) | Solution | `Flower-Shapes_0` | - |
| Bathroom Cupboard (Cupboards and Drawers) | Solution | `Cupboard_0` | Broom/Dustpan, Glove Hanging, Items |
| Lunch Tray (Cupboards and Drawers) | Solution | `TrayOrganizer_0` | Broccoli Organizer, Celery, Cracker, Peas, Sandwich, Tray Organizer |
| Kitchen Hanging Tools 1 (Cupboards and Drawers) | Solution | `HangingObjectsController_0` | Guiding Targets, Hanging Objects, Knives |
| Kitchen Utensils Drawers (Cupboards and Drawers) | Solution | `Top-Drawer_0` | Bottom Drawer, Top Drawer |
| Tea Cabinet (Cupboards and Drawers) | Solution | `Cupboard_0` | Cupcake, Items Placements, Jam Jars Stack, Spoon, Teacup Stack, Teapot Stack |
| Kitchen Hanging Tools 2 (Cupboards and Drawers) | Solution | `HangingObjectsController_0` | Draggables For Console, Hanging Objects |
| Pantry (Cupboards and Drawers) | Solution | `Cupboard_0` | Apple Basket, Can Stacks, Items Placements, Mixer, Mixing Bowl, Potato Crate |
| Junk Drawer 2 (Cupboards and Drawers) | Solution | `Draggables_0` | Draggables, Shuffleables |
| Filing Cabinet (Cupboards and Drawers) | Solution: Folder 1 | `FolderShuffleables_0` | - |
|  | Solution: Folder 2 | `FolderShuffleables_1` |  |
|  | Solution: Folder 3 | `FolderShuffleables_2` |  |
| Fossils (Cupboards and Drawers) | Solution | `Fern-Fossil-Jigsaw_0` | Dragonfly Fossil, Fern Fossil, Fish Fossil, Leaf Fossil, Shell Fossil, Snake Fossil |
| Media Cabinet (Cupboards and Drawers) | Solution | `Cupboard_0` | DVD, Joysticks + Gameboy, NES Stack, Rubix Stack |
| Game Pieces (Cupboards and Drawers) | Solution | `Drawers_0` | Bottom Drawer, Center Tiles, Heart, Left Drawer, Right Drawer, Top Drawer |
| Tackle Box (Cupboards and Drawers) | Solution | `Supplies-Draggables_0` | Bugs Draggable Ordered, Supplies, Weight |
| Coin Box (Cupboards and Drawers) | Solution: Match-Lines 1 | `Match-Lines_0` | - |
|  | Solution: Match-Lines 2 | `Match-Lines_1` |  |
|  | Solution: Match-Lines 3 | `Match-Lines_2` |  |
| Sewing Box (Cupboards and Drawers) | Solution | `Drawers_0` | Buttons Sortable, Curved Needles, Large Spools, Safety Pin, Scissors, Small Spools, Supplies, Top Drawer, Zipper |
| Jewelry Box (Cupboards and Drawers) | Solution | `Drawers_0` | Brooches, Cameos, Gold Bars, Locket, Radiolaria, Rings, Watches |
| Daggers (Cupboards and Drawers) | Solution | `Drawers_0` | - |
| Trophy Cabinet (Cupboards and Drawers) | Solution | `Cupboard_0` | Cupboard Doors, Items Placements |
| Nesting Boxes (Cupboards and Drawers) | Solution | `BoxOrganizer_0` | Box Organizer, Cat Organizer |
| Nested Drawers (Cupboards and Drawers) | Solution | `Drawers_0` | - |
| Boss (Cupboards and Drawers) | Solution | `KeysDraggables_0` | Dining Room, Keys, Landscape, Parking Lot |

### Seeing Stars (37 puzzles)

| Puzzle | Ending | Game id | Other checks on the puzzle |
|---|---|---|---|
| Canapes (Seeing Stars) | Solution: Stacked Groups 1 | `Stacked-Groups_0` | Cracker Positions |
|  | Solution: Stacked Groups 2 | `Stacked-Groups_1` |  |
|  | Solution: Stacked Groups 3 | `Stacked-Groups_2` |  |
| Forks (Seeing Stars) | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
|  | Solution: Shuffle 3 | `Shuffle_2` |  |
|  | Solution: Shuffle 4 | `Shuffle_3` |  |
|  | Solution: Shuffle 5 | `Shuffle_4` |  |
| Mugs (Seeing Stars) | Solution: Grid 1 | `Grid_0` | - |
|  | Solution: Grid 2 | `Grid_1` |  |
|  | Solution: Grid 3 | `Grid_2` |  |
|  | Solution: Grid 4 | `Grid_3` |  |
| Corn (Seeing Stars) | Solution: Pannables 1 | `Pannables-Controller_0` | - |
|  | Solution: Pannables 2 | `Pannables-Controller_1` |  |
|  | Solution: Pannables 3 | `Pannables-Controller_2` |  |
|  | Solution: Pannables 4 | `Pannables-Controller_3` |  |
| Pressed Leaves (Seeing Stars) | Solution: Leaves 1 | `Leaves-Draggables_0` | - |
|  | Solution: Leaves 2 | `Leaves-Draggables_1` |  |
|  | Solution: Leaves 3 | `Leaves-Draggables_2` |  |
| Broken Vases (Seeing Stars) | Solution | `Draggables_0` | - |
| Nanopets (Seeing Stars) | Solution: Shuffle 1 | `Shuffle_0` | Indexables |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
|  | Solution: Shuffle 3 | `Shuffle_2` |  |
|  | Solution: Shuffle 4 | `Shuffle_3` |  |
|  | Solution: Shuffle 5 | `Shuffle_4` |  |
| Obsolete Tech (Seeing Stars) | Solution | `Organizer_0` | Cassettes Ordered, Cell Phones Ordered, Floppy, Organizer, VHSOrdered |
| Balls (Seeing Stars) | Solution: Shuffle 1 | `Shuffle_0` | - |
|  | Solution: Shuffle 2 | `Shuffle_1` |  |
|  | Solution: Shuffle 3 | `Shuffle_2` |  |
| Water Glasses (Seeing Stars) | Solution: Jigsaw | `Jigsaw_0` | - |
|  | Solution: Sorting (Type & Size) 1 | `Sorting-(Type-&-Size)_0` |  |
|  | Solution: Sorting (Type & Size) 2 | `Sorting-(Type-&-Size)_1` |  |
|  | Solution: Water Level (Indexable) | `Water-Level-(Indexable)_0` |  |
| Pizza (Seeing Stars) | Solution: Distributables 1 | `Distributables_0` | - |
|  | Solution: Distributables 2 | `Distributables_1` |  |
|  | Solution: Distributables 3 | `Distributables_2` |  |
|  | Solution: Distributables 4 | `Distributables_3` |  |
| Leftovers (Seeing Stars) | Solution: Stackable Grid 1 | `StackableGrid_0` | - |
|  | Solution: Stackable Grid 2 | `StackableGrid_1` |  |
|  | Solution: Stackable Grid 3 | `StackableGrid_2` |  |
|  | Solution: Stackable Grid 4 | `StackableGrid_3` |  |
|  | Solution: Stackable Grid 5 | `StackableGrid_4` |  |
| Windows (Seeing Stars) | Solution | `Draggables_0` | - |
| Cacti (Seeing Stars) | Solution: Pots 1 | `Shuffleables-Pots_0` | - |
|  | Solution: Pots 2 | `Shuffleables-Pots_1` |  |
|  | Solution: Pots 3 | `Shuffleables-Pots_2` |  |
|  | Solution: Pots 4 | `Shuffleables-Pots_3` |  |
|  | Solution: Pots 5 | `Shuffleables-Pots_4` |  |
| Origami (Seeing Stars) | Solution | `OrigamiPad_0` | - |
| Moths (Seeing Stars) | Solution: Draggables | `Draggables_0` | - |
|  | Solution: DraggablesOrdered 1 | `DraggablesOrdered_0` |  |
|  | Solution: DraggablesOrdered 2 | `DraggablesOrdered_1` |  |
|  | Solution: DraggablesOrdered 3 | `DraggablesOrdered_2` |  |
| Math Set (Seeing Stars) | Solution: Indexables 1 | `Indexables_0` | - |
|  | Solution: Indexables 2 | `Indexables_1` |  |
| Ink Bottles (Seeing Stars) | Solution: Drips | `Draggables---Drips_0` | - |
|  | Solution: Label | `Draggables---Label_0` |  |
| Material Drawers (Seeing Stars) | Solution | `Drawer-Draggables_0` | Containable, Drawer Draggables |
| Curtains (Seeing Stars) | Solution | `Draggables_0` | - |
| Books Stacked (Seeing Stars) | Solution: Stacked Groups 1 | `StackedGroups_0` | - |
|  | Solution: Stacked Groups 2 | `StackedGroups_1` |  |
|  | Solution: Stacked Groups 3 | `StackedGroups_2` |  |
|  | Solution: Stacked Groups 4 | `StackedGroups_3` |  |
|  | Solution: Stacked Groups 5 | `StackedGroups_4` |  |
| Sticky Drawer (Seeing Stars) | Solution | `Draggables_0` | Draggables, Stickables |
| Bells (Seeing Stars) | Solution: Ordered Groups 1 | `OrderedGroups_0` | - |
|  | Solution: Ordered Groups 2 | `OrderedGroups_1` |  |
|  | Solution: Ordered Groups 3 | `OrderedGroups_2` |  |
| Figurines (Seeing Stars) | Solution: Draggables | `Draggables_0` | - |
|  | Solution: Groupables (Achievement) | `Groupables-(Achievement)_0` |  |
|  | Solution: Sorting Items | `SortingItemsDraggables_0` |  |
| Bookshelf (Seeing Stars) | Solution: Shuffle - Top Left 1 | `Shuffle---Top-Left_0` | - |
|  | Solution: Shuffle - Top Left 2 | `Shuffle---Top-Left_1` |  |
|  | Solution: Shuffle - Top Left 3 | `Shuffle---Top-Left_2` |  |
| Cat Eyes (Seeing Stars) | Solution | `IndexablesController_0` | Cat Eyes, Indexables |
| Junk Drawer Transforming (Seeing Stars) | Solution: Indexed 1 | `IndexedDraggables_0` | - |
|  | Solution: Indexed 2 | `IndexedDraggables_1` |  |
| Music Box (Seeing Stars) | Solution | `Organizer_0` | Organizer, State |
| Combs (Seeing Stars) | Solution: Draggables 1 | `Draggables_0` | - |
|  | Solution: Draggables 2 | `Draggables_1` |  |
| First Aid Kit (Seeing Stars) | Solution | `Draggables_0` | Bottle, Draggables, Pump State, Wipe |
| Robots (Seeing Stars) | Solution: Indexed 1 | `IndexedDraggables_0` | Containables |
|  | Solution: Indexed 2 | `IndexedDraggables_1` |  |
| Boss (Seeing Stars) | Solution: Knife | `DLC2Boss_Knife` | Compass, Hub, Knives, Locks |
|  | Solution: Lock | `DLC2Boss_Lock` |  |
|  | Solution: Compass | `DLC2Boss_Compass` |  |
| Bread Crusts (Seeing Stars) | Solution: Bread Loop | `BreadLoopDraggables_0` | - |
|  | Solution: Concentric Crust | `ConcentricCrustDraggables_0` |  |
| Cupcakes (Seeing Stars) | Solution: Colors | `Draggables-Colors_0` | - |
|  | Solution: Candles 1 | `Shuffleables-Candles_0` |  |
|  | Solution: Candles 2 | `Shuffleables-Candles_1` |  |
| Markers (Seeing Stars) | Solution: Markers Ordered 1 | `Markers-Ordered_0` | - |
|  | Solution: Markers Ordered 2 | `Markers-Ordered_1` |  |
|  | Solution: Markers Ordered 3 | `Markers-Ordered_2` |  |
| Whistles (Seeing Stars) | Solution: Draggables Ordered 1 | `DraggablesOrdered_0` | - |
|  | Solution: Draggables Ordered 2 | `DraggablesOrdered_1` |  |
|  | Solution: Draggables Ordered 3 | `DraggablesOrdered_2` |  |
|  | Solution: Draggables Ordered 4 | `DraggablesOrdered_3` |  |
|  | Solution: Draggables Ordered 5 | `DraggablesOrdered_4` |  |
| Ghost Cat (Seeing Stars) | Solution | `Indexables_0` | - |
