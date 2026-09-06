"""Press Connect during an offline run, against a server that is still down.

The one claim tools/offline-test.py cannot make, because it needs the pane.

What is being tested is an invariant, not a repaired crash. Inventory clears
the item list at the top of every connection ATTEMPT, which is right when the
attempt is the start of a session and wrong when it is not - and the list is
now written to disk as the offline cache, so it must never be briefly empty
while a live run holds items. The clear is deferred to the first thing the NEW
session produces, so an attempt that fails leaves the run alone.

PASS is: no `track: 0/` line anywhere in the attempt window, and the attempt
visible in the log so this cannot pass by never having tried.

THREE CONTROL RUNS, because the first green result meant nothing:

 1. Immediate clear PLUS a recount, deliberately built. First run PASSED - the
    test was reading only the log AFTER "connecting to localhost", and the
    wipe is logged just BEFORE it. The evidence was inside the window the test
    threw away. Fixed to read both windows; the same control then FAILED, as
    it must.
 2. The EXACT original clear, with no recount. PASSED, and that is the honest
    answer rather than a disappointing one: nothing recounts, so nothing
    visibly changes. There was no dramatic bug here, and the comment that
    claimed one has been corrected.
 3. The deferred clear that ships. PASSES.

So this test detects a wipe, and is known to, because it has been shown
failing. It is not evidence that the immediate clear broke a player's run.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/offline-reconnect-test.py 2>/dev/null
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment, close_game

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
AP = os.path.join(REPO, "Archipelago")
SEED = os.path.join(REPO, "testserver", "out-hint")
PORT = 38281
SLOT = "droha"


def say(step, msg):
    print(f"[step {step}/5] {msg}", flush=True)


class Log:
    def __init__(self):
        self.pos = self._size()

    def _size(self):
        try:
            return os.path.getsize(LOG)
        except OSError:
            return 0

    def rewind(self):
        self.pos = self._size()

    def new(self):
        size = self._size()
        if size < self.pos:
            self.pos = 0
        if size == self.pos:
            return ""
        with open(LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(self.pos)
            text = f.read()
        self.pos = size
        return text

    def wait(self, needles, timeout, step, what):
        got = ""
        end = time.time() + timeout
        last = 0
        while time.time() < end:
            got += self.new()
            if any(n in got for n in needles):
                return got
            if time.time() - last > 5:
                last = time.time()
                say(step, f"waiting for {what} ({int(end - time.time())}s left)")
            time.sleep(0.5)
        return got


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


def line_after(text, marker):
    for line in text.splitlines():
        if marker in line:
            return line
    return ""


def start_server():
    zipname = next(f for f in sorted(os.listdir(SEED)) if f.endswith(".zip"))
    err = open(os.path.join(REPO, "testserver", "logs", "reconnect-test.err"), "w")
    proc = subprocess.Popen(
        [sys.executable, "MultiServer.py", "--port", str(PORT),
         os.path.join(SEED, zipname)],
        cwd=AP, stdout=subprocess.DEVNULL, stderr=err, stdin=subprocess.DEVNULL,
        # See offline-test.py: without this MultiServer PROMPTS to install a
        # requirement and dies on EOF, looking exactly like a refused port.
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    import socket
    for _ in range(60):
        with socket.socket() as s:
            s.settimeout(0.5)
            if s.connect_ex(("127.0.0.1", PORT)) == 0:
                return proc, err
        time.sleep(1)
    raise SystemExit("MultiServer never opened the port")


def main():
    log = Log()
    results = []

    with Environment("offline-reconnect") as env:
        env.configure(Host="localhost", Port=PORT, SlotName=SLOT,
                      AutoConnect="true")

        # ---- 1. one real connection, to lay down a cache ------------------
        proc, err = start_server()
        log.rewind()
        subprocess.Popen([EXE], cwd=GAME)
        out = log.wait(["connected. "], 90, 1, "the connection")
        if "connected. " not in out:
            say(1, "FAIL never connected; nothing to cache")
            return 1
        say(1, line_after(out, "connected. ").split("] ")[-1])
        time.sleep(8)
        close_game()
        time.sleep(2)
        proc.terminate()
        proc.wait(timeout=15)
        err.close()
        time.sleep(3)

        # ---- 2. offline run, with the server gone -------------------------
        log.rewind()
        subprocess.Popen([EXE], cwd=GAME)
        out = log.wait(["offline: resumed"], 120, 2, "the offline start")
        resumed = line_after(out, "offline: resumed")
        if not resumed:
            say(2, "FAIL no offline run started")
            close_game()
            return 1
        say(2, resumed.split("] ")[-1])
        before_packs = resumed.split(" - ")[-1]
        before_track = line_after(out, "track: ")
        say(2, before_track.split("] ")[-1])

        # ---- 3. open the pane and press Connect ---------------------------
        # The server is still down. This is the press that used to empty the
        # inventory for as long as the attempt took to fail.
        # Both names were found by probing, not guessed, and the first guess
        # was wrong: "Archipelago" is the LABEL on the menu entry, and press:
        # matches GameObject names. The button is ApConnectButton, and the
        # modal's action is the game's own "Confirm Button" in its footer.
        log.new()
        dev("press:ApConnectButton", 3.0)
        dev("press:Confirm Button", 1.0)
        out = log.wait(["connecting to localhost"], 25, 3, "the attempt")
        tried = "connecting to localhost" in out
        # KEEP this text. Attempt() clears the inventory BEFORE it logs
        # "connecting to localhost", so the evidence of a wipe is inside this
        # window, not after it. The first version of this test dropped `out`
        # on the floor and looked only at what came later - which is why a
        # negative control with the bug deliberately put back still passed.
        attempt_window = out
        say(3, "attempt started" if tried else
               "NO ATTEMPT - the pane was not reachable")
        results.append(("3 the press actually started an attempt", tried))

        # ---- 4. while it is in flight, the run must be untouched ----------
        #
        # The assertion is the ABSENCE of one exact log line, and it is exact
        # on purpose. Track.SetPacksHeld logs only when the count CHANGES:
        #
        #     track: 0/2 packs, N puzzles open (+0)
        #
        # An emptied inventory recounts to zero packs and produces that line.
        # A run left alone produces nothing at all, because nothing changed.
        # So "no track: 0/ line between the attempt starting and the attempt
        # failing" is the bug's own signature, not a proxy for it.
        out2 = log.wait(["staying offline", "no server at", "retrying in",
                         "attempt cancelled", "not retrying"], 40, 4,
                        "the attempt to fail")
        failed = any(m in out2 for m in
                     ("staying offline", "no server at", "retrying in",
                      "not retrying"))
        # Both windows, for the reason above.
        whole = attempt_window + out2
        wiped = "track: 0/" in whole
        restarted = "offline: resumed" in whole
        say(4, f"attempt finished: {failed}; inventory wiped: {wiped}; "
               f"run restarted: {restarted}")
        results.append(("4 the attempt actually failed (not still pending)",
                        failed))
        results.append(("4 the offline run survived the failed attempt",
                        not wiped and not restarted))

        # ---- 5. and it is still the same run afterwards -------------------
        log.new()
        dev("state", 0.5)
        after = log.wait(["state: gameState"], 8, 5, "the game state")
        still_up = "Gameplay" in after or "Title" in after or after.strip() != ""
        say(5, "game still running and responsive" if still_up
               else "GAME UNRESPONSIVE")
        results.append(("5 the game is still responsive", still_up))

        close_game()

    passed = sum(1 for _, ok in results if ok)
    for name, ok in results:
        print(f"  {'PASS' if ok else 'FAIL'}  {name}", flush=True)
    print(f"Done: {passed}/{len(results)} checks passed", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
