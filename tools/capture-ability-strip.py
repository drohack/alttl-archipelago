"""Photograph the level select twice: abilities all locked, then all held.

The README needs to show what the level select looks like when a run is
carrying ability locks, and the strip only means anything as a pair - twelve
dim icons say "here is everything still to find", the same twelve lit say
"here is what you have". Either one on its own reads as a bug.

WHY A REAL GAME AND NOT A COMPOSITE. The icons are twelve PNGs in the mod and
it would be a five-minute job to paste them onto a canvas in two rows. That
picture would be a drawing OF the feature rather than the feature, and it
would go stale the first time a layout constant moved without anyone noticing.
This drives the actual game, so what lands in docs/images is what a player
sees.

ONE LAUNCH, TWO STATES. The server is started here with stdin held open, so
the twelve abilities can be cheated in between the two shots without
restarting anything. The locked shot has to come first - there is no way to
un-send an item - which is why the room's save file is moved aside on the way
in and put back on the way out.

FULL FRAMES, CROPPED SEPARATELY. This saves whole screenshots and stops. The
strip's position on screen depends on the window size, which belongs to
whoever plays this install and is not ours to set for a photograph, so the
crop is measured from the picture afterwards by crop-ability-strip.py rather
than guessed at here. It also means the framing can be redone without
launching the game again.

Run it with the game closed:

    py -3.13 tools/capture-ability-strip.py [path/to/seed.zip]

The seed defaults to the newest zip under Archipelago/output. Any seed works
as long as it has ability_locks on; the strip is drawn from the slot's
catalogue, not from what has been played. Both frames land in
docs/images/raw/, the game is closed, and harness_env puts the player's
config back.
"""
import os
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment, close_game, ensure_no_steam_relaunch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")

OUT = os.path.join(ROOT, "docs", "images", "raw")
SLOT = "droha"
PORT = 38281

ABILITIES = [
    "Swapping", "Stacking", "Ordering", "Gadgets",
    "Rotating", "Grids", "Tidying", "Containers",
    "Drawer", "Sticking", "Symmetry", "Jigsaw",
]


def say(step, what):
    print(f"[{step}/9] {what}", flush=True)


class Log:
    """Tail of LogOutput.log, from wherever it stood when we started."""

    def __init__(self):
        self.pos = os.path.getsize(LOG) if os.path.exists(LOG) else 0

    def new(self):
        try:
            size = os.path.getsize(LOG)
        except OSError:
            return ""
        if size < self.pos:
            self.pos = 0
        if size == self.pos:
            return ""
        with open(LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(self.pos)
            text = f.read()
        self.pos = os.path.getsize(LOG)
        return text

    def wait(self, needles, seconds, step, what):
        """Read until one of `needles` shows up. Returns everything read."""
        end = time.time() + seconds
        text = ""
        last = 0.0
        while time.time() < end:
            text += self.new()
            if any(n in text for n in needles):
                return text
            if time.time() - last > 10:
                last = time.time()
                say(step, f"still waiting for {what}, "
                          f"{int(end - time.time())}s left")
            time.sleep(0.5)
        raise SystemExit(f"gave up waiting for {what} after {seconds}s")


def dev(cmd, settle=0.0):
    """Hand one command to DevTools and wait for it to take the file back."""
    with open(CMD, "w", encoding="utf-8") as f:
        f.write(cmd)
    for _ in range(240):
        try:
            if os.path.getsize(CMD) == 0:
                break
        except OSError:
            pass
        time.sleep(0.25)
    if settle:
        time.sleep(settle)


def newest_zip():
    out = os.path.join(ROOT, "Archipelago", "output")
    zips = [os.path.join(out, f) for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        raise SystemExit(f"no seed zip in {out} - generate one first")
    return max(zips, key=os.path.getmtime)


def to_level_select(log, step):
    """Open the level select, and do not believe it until the game says so.

    menu:levels once is not enough. The mod rebuilds the title screen after
    the connection lands - Archipelago button, hidden Daily Tidy, hidden
    Archive - and a navigation issued into the middle of that is undone by the
    rebuild finishing. The first run of this script photographed the title
    screen for exactly that reason, having logged "on the level select" on no
    evidence but its own optimism.

    So ask. `state` reports the live gameState, and Levels_GameState is the
    only answer that means the strip is on screen.
    """
    for attempt in range(1, 9):
        dev("menu:levels", 2.5)
        log.new()
        dev("state", 1.0)
        text = log.wait(["state: gameState="], 15, step, "the game state")
        if "gameState=Levels_GameState" in text:
            say(step, f"on the level select after {attempt} attempt(s)")
            time.sleep(2.5)          # let the strip finish its first paint
            return
        where = text.split("gameState=", 1)[1].split(",")[0].strip()
        say(step, f"attempt {attempt}: still in {where}, asking again")
        time.sleep(2.0)
    raise SystemExit("never reached the level select")


def shoot(name, step):
    """Take one full-window frame and move it into docs/images/raw."""
    # Unity writes the file itself, asynchronously, so the destination is
    # inside the game folder and the wait is for the file to stop growing.
    tmp = os.path.join(GAME, f"ap-{name}.png")
    if os.path.exists(tmp):
        os.remove(tmp)

    dev(f"shot:{tmp}|1")
    size = -1
    for _ in range(60):
        time.sleep(0.5)
        try:
            now = os.path.getsize(tmp)
        except OSError:
            continue
        if now > 0 and now == size:
            break
        size = now
    else:
        raise SystemExit(f"the {name} screenshot never appeared at {tmp}")

    dest = os.path.join(OUT, f"{name}.png")
    shutil.move(tmp, dest)
    say(step, f"saved {os.path.relpath(dest, ROOT)} ({size // 1024} KB)")
    return dest


def main():
    zip_path = sys.argv[1] if len(sys.argv) > 1 else newest_zip()
    save = zip_path[:-4] + ".apsave"
    parked = save + ".parked"

    os.makedirs(OUT, exist_ok=True)
    close_game()

    # A room that has already handed out the abilities cannot show them
    # locked, so the previous run's save goes out of the way first.
    if os.path.exists(save):
        shutil.move(save, parked)
        say(1, f"parked the room save for {os.path.basename(zip_path)}")
    else:
        say(1, f"{os.path.basename(zip_path)} has no room save, already fresh")

    server = subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(PORT), zip_path],
        cwd=os.path.join(ROOT, "Archipelago"),
        stdin=subprocess.PIPE,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        text=True,
    )

    try:
        with Environment("capture-ability-strip") as env:
            env.configure(
                Host="localhost", Port=str(PORT),
                SlotName=SLOT, AutoConnect="true",
            )
            say(2, f"server up on {PORT}, launching the game")

            ensure_no_steam_relaunch()
            log = Log()
            subprocess.Popen([EXE], cwd=GAME)
            log.wait(["connected. ", "Archipelago refused"], 180, 3,
                     "the mod to connect")
            # The rebuild that follows the connection is what undid the first
            # attempt's navigation. Wait for its last line before moving.
            log.wait(["toasts: overlay ready"], 60, 3, "the title to settle")
            time.sleep(3.0)
            say(3, "connected, title settled")

            to_level_select(log, 4)

            shoot("ability-strip-locked", 5)

            for ability in ABILITIES:
                server.stdin.write(f"/send {SLOT} {ability}\n")
            server.stdin.flush()
            say(6, "cheated in all twelve abilities")

            log.wait([f"received item: {ABILITIES[-1]}"], 90, 7,
                     "the abilities to arrive")
            time.sleep(4.0)          # the strip repaints on a one-second poll
            say(7, "abilities arrived")

            shoot("ability-strip-held", 8)
    finally:
        try:
            server.stdin.write("/exit\n")
            server.stdin.flush()
        except Exception:
            pass
        try:
            server.wait(timeout=10)
        except Exception:
            server.kill()
        # The room is the harness's doing, not the player's - put the save
        # back the way parking it found things.
        if os.path.exists(parked):
            if os.path.exists(save):
                os.remove(save)
            shutil.move(parked, save)
        close_game()

    say(9, "Done: both full frames in docs/images/raw, game closed")


if __name__ == "__main__":
    main()
