# A Little to the Left

## Where is the options page?

The [player options page](../player-options) has all the settings you need to
configure and export a config file.

## What does randomization do to this game?

The run is shaped like the base campaign - a scrolling filmstrip of cards
across five chapters - but most of the puzzles in it are **procedurally
generated**. Sixteen of the game's puzzles build a fresh layout from a seed, so
they are new even if you have finished the game. They are mixed with the
seasonal event puzzles from the Archive, and with hand-made campaign puzzles
where those are the only source of a mechanic.

Two things gate your progress:

- **Puzzle Packs** open the next block of puzzles on the track. Every block is
  the same size, the free opening included, and only the last is short - a run
  rarely divides evenly. **Pack Size** sets it, five by default.
- **Abilities** unlock the mechanics themselves - swapping, stacking, rotating,
  nesting things in containers, and so on. Objects belonging to a mechanic you
  have not unlocked sit dimmed and cannot be moved.

Because abilities gate individual groups of objects rather than whole puzzles,
a puzzle can be **partly solved**. You tidy the parts you have the verbs for,
leave the rest, and come back when the missing ability arrives.

## What Archipelago items can appear in other players' worlds?

Puzzle Packs, the twelve mechanic abilities, the Credits, Skips, Hint Pages and
background colours. The cat trap is also shuffled out - it knocks your
arrangement over, costing you time but never progress.

**Background Change Trap** recolours every backdrop at once - the puzzle, the
pause screen and the level select track - drawing from the ten background
colours the game itself uses. It is the run's filler, and unlike most filler it
does something you can see the moment it arrives. It is called a trap because
the colour it lands on is chosen by how many you hold rather than by what is on
screen, so a puzzle can end up with pieces that are hard to pick out against
their own background.

Every puzzle in the game has a hint, and a default seed holds about 110 pages
across them. **Hint Coverage** decides how many get an item, and defaults to
50% - enough to open the hints you actually reach for, without the pool being
nothing but hints. Turn it up if you would rather know every notepad in the
run can be opened.

A **Hint Page** uncovers one page of one puzzle's hint notepad. Most puzzles
have a one-page hint, but some run to five, and each page costs its own item.
At the default 50% coverage the seed holds an item for about half the pages it
contains, so some notepads stay shut - turn **Hint Coverage** up to 100 if you
want every hint in the run to be openable. Without the item the notepad still
opens; you just cannot erase the scribble covering the page.

## What is considered a location check?

Three kinds:

- **Solutions.** Many puzzles have more than one valid arrangement. Each
  distinct solution you find is its own check.
- **Parts.** On a puzzle made of several independent groups of objects, each
  group is its own check, collected as soon as that group is tidy.
- **The credits.**

A puzzle you clear with a Skip DOES count toward the goal, and a Skip
fills in every check on that puzzle - so a skipped puzzle is starred as
well as beaten. The shortcut is bounded by how many Skips the seed
contains rather than forbidden.

Hint pages are not checks - they are things you spend items on, not places
items are found.

## When the player receives an item, what happens?

Packs reveal more cards on the level select. An ability takes effect
immediately: objects that were dimmed become movable, including on a puzzle you
already have open. Filler and traps apply the next time they can.

Hint Pages go into a pool you spend by erasing. The pause menu shows how many
you are holding beside the Hint entry.

Opening a notepad is always free, so you can check whether a hint exists and
how long it is before deciding to spend. The page itself tells you where you
stand - that rubbing it out will cost a Hint Page and how many you hold, or
that you have none, or that you already paid for this one and may read it
again for nothing. You are only charged once you actually erase the scribble,
and a page you have paid for stays free for the rest of the run.

A background item repaints straight away, and the colour you end up with
depends only on how many you have been sent - so it survives a reconnect
rather than jumping to a new one every time you log in.

## What is the victory condition?

Finish enough puzzles AND find the Credits item, then play the credits card.
Both are required: the item is shuffled into the multiworld like any other, so
it can turn up early or late, and meeting the count without it leaves the card
locked - as does holding it before the count is met.

Playing the card is what ends the run. Reaching the count does not finish it
on your behalf; you go to the card and watch the ending.

What "enough" means depends on the `goal` option:

- **Beat Levels** (the default) counts a puzzle once you have finished it any
  one way, and wants `levels_to_beat` of them.
- **Star Levels** counts a puzzle only when EVERY check on it is done - every
  solution and every part - and wants `levels_to_star`. It is the same star
  the level select draws on a card with nothing left to do, so you can watch
  your progress toward it while you play.

Starring is a great deal more work than beating, which is why the two have
separate counts rather than sharing one number.
