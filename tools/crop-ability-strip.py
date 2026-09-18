"""Cut the ability strip out of the frames capture-ability-strip.py took.

Separate from the capture because the framing is the part that needs looking
at, and looking at it should not cost a game launch. Re-run this as often as
the crop needs adjusting; the raw frames under docs/images/raw do not change.

The box is in pixels of a 1920x1080 frame. The mod's overlay is scaled to that
same reference, so the strip lands in the same place at any window size the
frame was taken at - but the frame itself is whatever size the player's window
was, so anything else is scaled to 1920x1080 first rather than cropped blind.

    py -3.13 tools/crop-ability-strip.py [shot name ...]

With no argument it writes every shot. Name one to write only that, which is
what you want whenever the raw frames on disk came from a different run than
the image you are replacing - the two strips are shot on a base-game run and
ability-distributing.png on a Seeing Stars one.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.path.join(ROOT, "docs", "images", "raw")
OUT = os.path.join(ROOT, "docs", "images")

#: left, top, right, bottom, measured off a captured frame rather than
#: computed from the layout constants - the strip is positioned by anchors and
#: a pivot, so the constants alone do not say where it lands on screen.
#:
#: Deliberately loose by a few pixels on every side. A crop tight to the icons
#: reads as a sprite sheet; a little of the menu's ground around it reads as a
#: photograph of the game, which is what this is.
#:
#: SIZED FOR THE TWO-ROW STRIP, which is what a base-game run shows: twelve
#: abilities, six to a row. A run with Seeing Stars on needs Distributing as
#: well and the strip wraps to a third row of one, which this bottom edge cuts
#: off - deliberately. The two strip images document the DEFAULT run, where
#: both DLC toggles are off, and the thirteenth icon is shown on its own
#: instead (DST_BOX below) rather than by reshooting them.
STRIP_BOX = (236, 2, 636, 192)

#: The lone third-row pill, for the DLC note in the README. Same grid: column
#: one of row three, with the row pitch measured off a captured frame.
DST_BOX = (236, 170, 330, 265)

REFERENCE = (1920, 1080)

#: name, source frame, box, caption. The source is named per shot because the
#: thirteenth icon can only come from a frame of a DLC run, while the two
#: strips must come from a base-game one.
SHOTS = [
    ("ability-strip-locked", "ability-strip-locked", STRIP_BOX,
     "every ability still to find"),
    ("ability-strip-held", "ability-strip-held", STRIP_BOX,
     "every ability held"),
    ("ability-distributing", "ability-strip-held", DST_BOX,
     "the DLC2 ability on its own"),
]


def main():
    if not os.path.isdir(RAW):
        raise SystemExit(f"no frames in {RAW} - run capture-ability-strip.py")

    # Named shots only, when asked. The raw frames are whatever was captured
    # last, and the two strips and the DLC icon come from DIFFERENT runs - so
    # regenerating all three from one set of frames is usually wrong. Passing
    # a name is how you say which one you actually mean.
    wanted = set(sys.argv[1:])
    shots = [s for s in SHOTS if not wanted or s[0] in wanted]
    unknown = wanted - {s[0] for s in SHOTS}
    if unknown:
        raise SystemExit("no such shot: " + ", ".join(sorted(unknown)))

    for name, frame_name, box, what in shots:
        src = os.path.join(RAW, frame_name + ".png")
        if not os.path.exists(src):
            raise SystemExit(f"missing {src} - run capture-ability-strip.py")

        with Image.open(src) as im:
            frame = im
            if im.size != REFERENCE:
                print(f"{name}: {im.size[0]}x{im.size[1]}, scaling to "
                      f"{REFERENCE[0]}x{REFERENCE[1]} to crop", flush=True)
                frame = im.resize(REFERENCE, Image.LANCZOS)
            out = os.path.join(OUT, name + ".png")
            frame.crop(box).save(out)

        size = os.path.getsize(out)
        print(f"{name}.png: {box[2] - box[0]}x{box[3] - box[1]}, "
              f"{size // 1024} KB - {what}", flush=True)

    print(f"Done: {len(shots)} image(s) written to docs/images", flush=True)


if __name__ == "__main__":
    main()
