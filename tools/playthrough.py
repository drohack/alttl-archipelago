"""Drive a whole run to the credits, through the game's own controls.

Not a substitute for playing: solve: flips a controller's solved flag rather
than moving pieces, so this exercises the PROGRESSION loop - checks fire, the
server returns packs and abilities, more of the track opens - and not the
puzzles themselves. That loop is where every bug this session found lived.

The loop is Play -> solve -> Menu -> Play, which is deliberately the set of
routes that have been verified to work:

- Play answers Gameplay_GameState.GetLevelIndex and opens the farthest slot
  with something doable, so it advances on its own as the run progresses.
- The post-completion screen offers only Continue, Retry and Menu. Continue is
  NOT used: when the next slot is a daily-pool level the game routes it by kind
  and drops the run onto the Daily Tidy page, which is a real bug and not
  something the driver should paper over by accident.
- Clicking track cards is avoided too. It works, but only from a level select
  that is actually up, and after a completion it is not.

Controls are pressed with `press:`, which dispatches a real pointer click.
`clickbutton:` invokes Button.onClick and is not the same thing: the tutorial
modal's confirm IS a Button, has nothing on onClick, and reported four
successful clicks while staying on page 1 of 3.
"""
import os
import time

GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")

MAX_LEVELS = 40


def dev(cmd, settle=0.0):
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


class Log:
    def __init__(self):
        self.pos = os.path.getsize(LOG)

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
        self.pos = size
        return text

    def wait(self, needles, timeout=20.0):
        got = ""
        end = time.time() + timeout
        while time.time() < end:
            got += self.new()
            if any(n in got for n in needles):
                return got
            time.sleep(0.4)
        return got


def current_level(log):
    log.new()
    dev("state", 0.4)
    out = log.wait(["state: gameState"], 8)
    for line in out.splitlines():
        if "state: gameState" in line:
            if "Gameplay_GameState" not in line:
                return None
            for part in line.split():
                if part.startswith("activeLevel="):
                    return line.split("activeLevel=", 1)[1].split(" index=")[0].strip()
            return "?"
    return None


def main():
    log = Log()
    done = []
    credits_seen = False
    stuck = 0

    for step in range(1, MAX_LEVELS + 1):
        tag = f"[{step}/{MAX_LEVELS}]"

        # Clear anything modal, get to the title, then press Play.
        #
        # Both, in that order, because neither alone is enough. The Menu
        # button is the real route out of the post-completion RetryUI, and
        # setting Title_GameState does NOT escape that screen - Play then
        # picked a slot and the game never left the retry UI, so it looked like
        # Play was broken. But the Menu button does not exist on the Daily Tidy
        # page, which is where a daily-pool level dumps the run, and there only
        # menu:title gets out. Pressing a control that is absent is harmless.
        dev("press:Confirm Button", 1.5)
        dev("press:Menu Button", 3.0)
        dev("menu:title", 2.5)
        dev("play", 9.0)

        name = current_level(log)
        if name is None:
            stuck += 1
            print(f"{tag} step 1/3: Play opened nothing (strike {stuck}/3)", flush=True)
            if stuck >= 3:
                print("Done: Play stopped opening levels, giving up", flush=True)
                break
            continue
        stuck = 0

        log.new()
        dev("controllers", 0.8)
        out = log.wait(["controllers: "], 8)
        count = 0
        for line in out.splitlines():
            if "controllers: " in line and " registered on " in line:
                try:
                    count = int(line.split("controllers: ")[1].split(" ")[0])
                except (IndexError, ValueError):
                    count = 0
        print(f"{tag} step 2/3: {name}, {count} group(s), {len(done)} done", flush=True)

        log.new()
        finished = False
        for i in range(max(count, 1)):
            dev(f"solve:{i}", 0.6)
            out = log.wait(["LevelComplete ", "no level running"], 4)
            if "LevelComplete " in out or "no level running" in out:
                finished = True
                break

        tail = log.wait(["beaten:", "check:", "credits:"], 6)
        if "credits: unlocked" in tail:
            credits_seen = True
        if finished:
            done.append(name)

        print(f"{tag} step 3/3: {name} {'beaten' if finished else 'NOT beaten'}"
              f" (total {len(done)})", flush=True)

        if credits_seen:
            print("CREDITS UNLOCKED", flush=True)
            break

    print(f"Done: {len(done)} level(s) beaten, credits "
          f"{'unlocked' if credits_seen else 'NOT unlocked'}", flush=True)


if __name__ == "__main__":
    main()
