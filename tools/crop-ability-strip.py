"""Cut the ability strip out of the frames capture-ability-strip.py took.

Separate from the capture because the framing is the part that needs looking
at, and looking at it should not cost a game launch. Re-run this as often as
the crop needs adjusting; the raw frames under docs/images/raw do not change.

The box is in pixels of a 1920x1080 frame. The mod's overlay is scaled to that
same reference, so the strip lands in the same place at any window size the
frame was taken at - but the frame itself is whatever size the player's window
was, so anything else is scaled to 1920x1080 first rather than cropped blind.

    py -3.13 tools/crop-ability-strip.py
"""
import os

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
BOX = (236, 2, 636, 192)

REFERENCE = (1920, 1080)

SHOTS = [
    ("ability-strip-locked", "every ability still to find"),
    ("ability-strip-held", "every ability held"),
]


def main():
    if not os.path.isdir(RAW):
        raise SystemExit(f"no frames in {RAW} - run capture-ability-strip.py")

    for name, what in SHOTS:
        src = os.path.join(RAW, name + ".png")
        if not os.path.exists(src):
            raise SystemExit(f"missing {src} - run capture-ability-strip.py")

        with Image.open(src) as im:
            frame = im
            if im.size != REFERENCE:
                print(f"{name}: {im.size[0]}x{im.size[1]}, scaling to "
                      f"{REFERENCE[0]}x{REFERENCE[1]} to crop", flush=True)
                frame = im.resize(REFERENCE, Image.LANCZOS)
            out = os.path.join(OUT, name + ".png")
            frame.crop(BOX).save(out)

        size = os.path.getsize(out)
        print(f"{name}.png: {BOX[2] - BOX[0]}x{BOX[3] - BOX[1]}, "
              f"{size // 1024} KB - {what}", flush=True)

    print(f"Done: {len(SHOTS)} strips written to docs/images", flush=True)


if __name__ == "__main__":
    main()
