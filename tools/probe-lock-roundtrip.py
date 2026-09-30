"""Lock every level, give everything back, and check it comes back as it was.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py [index ...] [--resume]
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py --all [--resume]

Needs the hand-test server up with a seed that holds every ability, and the
mod pointed at it (tools/handtest-level.py sets both up). Starts the game if
it is not running and restarts it every 20 levels. Deletes LogOutput.log on
each start, as release_e2e does.

Per level, driven through DevTools (`revoke:`, `boot:`, `state:`, `drawers:`):

  control   every ability held. A snapshot, one 3 s later, and one of a
            second load: whatever differs between those changes on its own
            and is ignored below. Then the drawer steps: every sliding drawer
            opened, a lock pass, closed again, all put back as they started
            (snapshot R), then all opened (snapshot O).
  configs   `all` (every ability withheld); on levels with drawers or doors
            also only Drawer / Gadgets withheld and all but Drawer / Gadgets
            withheld. Each: boot with it withheld, check what is locked, the
            same drawer steps with the lock on (a locked drawer is asked to
            move instead, and must refuse), give everything back, R and O.

Checked while locked, on every object the mod locks: frozen (collider off,
body stopped) or, for a cover, solid (collider and body as loaded); every
visible sprite the lock paints drawn grey; a locked drawer refuses to move
(where the game's own move moves it when free); a locked scrub object refuses
a drag (DevTools `scrub:`, which logs the mod's refusal); and what is locked
matches ObjectLock's and AbilityLocks' rules and the hand-tested cases in
EXPECT. After everything is given back: nothing locked or grey, a refused
drawer move made, and R and O equal to the control's R and O - the game's
own Selectable first, so a piece that can no longer be picked up fails and
one the game never let be picked up is reported.

Seen to fail on every fix it covers: each was broken in turn and the probe
run on the level it was found on (2026-09-27; docs/dev/testing.md has
the count and the two fallbacks no level shows any more). A sticker's peel
handle and a rag are in no controller's list; DevTools state: adds them with
`handleOf`, and they are expected locked as the pieces they act on are.

Writes testserver/logs/lock-roundtrip/results.jsonl, one line per level and
config, and keeps the snapshots of anything that did not pass.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import harness_env  # noqa: E402
import release_e2e as e2e  # noqa: E402

ROOT = HERE.parent
OUT = ROOT / "testserver" / "logs" / "lock-roundtrip"
SNAPS = OUT / "snaps"
RESULTS = OUT / "results.jsonl"
SEED = 424242               # randomizable levels boot with this seed, the same every load
RESTART_EVERY = 20
ONLY = set()                # --configs

LEVELS = json.load(open(ROOT / "apworld/alttl/data/levels.json", encoding="utf-8"))["levels"]
_ABIL = json.load(open(ROOT / "apworld/alttl/data/abilities.json", encoding="utf-8"))
CLASS_ABILITY = {}
for _a, _classes in _ABIL["abilities"].items():
    for _c in _classes:
        CLASS_ABILITY[_c] = _a
for _dlc in _ABIL["dlcAbilities"].values():
    for _a, _classes in _dlc.items():
        for _c in _classes:
            CLASS_ABILITY[_c] = _a
ALL_ABILITIES = sorted(set(CLASS_ABILITY.values()))

# ObjectLock (Core) and AbilityLocks, restated so the probe can say what
# SHOULD be locked. Restated, not imported: an instrument that imported the
# thing it measures would agree with it by construction.
ENCLOSURES = {"DrawerController", "DrawerExpandableController", "Cupboard"}
DRAWER_SETS = {"DrawerController", "DrawerExpandableController"}
DOOR_LEVELS = {"DLC1 Clock Cupboard", "DLC1 Tea Cabinet", "DLC1 Trophy Cabinet"}
# ObjectLock.LockedAs: (level, controller name) -> the class it locks as. Books
# (Randomized)'s symmetric seeds add Draggables over Shuffle's books.
LOCKED_AS = {("Books (Randomized)", "Draggables"): "Shuffleables"}

# Hand-tested answers: (level, pick, ability withheld, locked, solid, source).
# pick is an object name, "class:<holder class>" for every object it holds, or
# "handles" for every peel handle and rag (DevTools state: `handleOf`).
EXPECT = [
    ("DLC1 Sewing Box", "Box", "Drawer", False, None,
     "droha 2026-09-26: the box cannot move; locking it greyed everything in it"),
    ("DLC1 Jewelry Box", "Main Box", "Drawer", False, None,
     "screenshot 2026-09-26: the Main Box greyed the whole box"),
    ("DLC1 Daggers", "Box", "Drawer", True, True,
     "droha 2026-09-26: the box and its drawers lock together"),
    ("DLC1 Daggers", "Right Drawer Object", "Drawer", True, True, "as above"),
    ("DLC1 Daggers", "Left Drawer Object", "Drawer", True, True, "as above"),
    ("DLC1 Daggers", "Top Drawer Object", "Drawer", True, True, "as above"),
    ("DLC1 Daggers", "Bottom Drawer Object", "Drawer", True, True, "as above"),
    ("DLC1 Tea Cabinet", "class:AnimScrubbables", "Gadgets", True, True,
     "droha 2026-09-25: locked doors let the items behind be grabbed; now solid"),
    ("DLC1 Clock Cupboard", "class:AnimScrubbables", "Gadgets", True, True, "a door, as Tea Cabinet"),
    ("DLC1 Trophy Cabinet", "class:AnimScrubbables", "Gadgets", True, True, "a door, as Tea Cabinet"),
    ("Wilting Flowers", "class:AnimScrubbables", "Gadgets", True, False,
     "droha hand-tested: the flowers cannot be stood up without Gadgets"),
    ("DLC2 Sticky Drawer", "handles", "Sticking", True, False,
     "droha 2026-09-27: Stickables solved with Sticking missing, through the gum (peel handles)"),
    ("PawPrints", "handles", "Tidying", True, False,
     "droha 2026-09-27: the rag cleaned the locked paw prints and coffee spill"),
    ("Candles", "handles", "Gadgets", True, False,
     "droha 2026-09-27: the lit match grew and shrank the grey candles"),
]


def say(msg):
    print(msg, flush=True)


# ------------------------------------------------------------------ the game

class Game:
    """The running game, through the DevTools command file and the log."""

    def __init__(self):
        self.log = e2e.Log()
        self.buf = ""            # every log line read since the level started
        self.started = False

    def read(self):
        text = self.log.new()
        self.buf += text
        return text

    def running(self):
        return e2e.game_is_running()

    def start(self):
        if self.running() and self.started:
            return
        if self.running():
            harness_env.close_game(quiet=True)
        say("[game] starting it")
        self.log.before_launch()
        e2e.launch_game()
        end = time.time() + 150
        text = ""
        while time.time() < end:
            text += self.read()
            if "connected. " in text:
                break
            if "Archipelago refused" in text or not self.running() and time.time() > end - 140:
                break
            time.sleep(0.5)
        if "connected. " not in text:
            raise RuntimeError("the game did not connect to the hand-test server")
        time.sleep(3)
        self.dev("mute")
        self.dev("traps:off")
        self.started = True
        say("[game] up, muted, background traps off")

    def restart(self):
        harness_env.close_game(quiet=True)
        self.started = False
        self.start()

    def dev(self, cmd, timeout=30):
        """One DevTools command; False when the game never took it."""
        with open(e2e.CMD, "w", encoding="utf-8") as f:
            f.write(cmd)
        end = time.time() + timeout
        while time.time() < end:
            try:
                if os.path.getsize(e2e.CMD) == 0:
                    return True
            except OSError:
                pass
            time.sleep(0.1)
        with open(e2e.CMD, "w", encoding="utf-8") as f:
            f.write("")
        return False

    def ask(self, cmd, pattern, timeout=10):
        """Send cmd and return the first match of pattern in what it logs."""
        self.read()
        if not self.dev(cmd):
            return None
        end = time.time() + timeout
        got = ""
        while time.time() < end:
            got += self.read()
            m = re.search(pattern, got)
            if m:
                return m
            time.sleep(0.2)
        return None

    def settle(self, least=4.0, quiet=2.5, most=20.0):
        """Wait until the lock and the level have stopped logging."""
        start = time.time()
        last = start
        while time.time() - start < most:
            text = self.read()
            if re.search(r"abilities:|boot:|drawer", text):
                last = time.time()
            if time.time() - start >= least and time.time() - last >= quiet:
                return
            time.sleep(0.3)

    def snapshot(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.exists():
            path.unlink()
        m = self.ask(f"state:{path}", r"state: (\d+) object\(s\)|state: no level running", 15)
        if m is None or m.group(1) is None or not path.exists():
            return None
        snap = {}
        with open(path, encoding="utf-8") as f:
            for line in f:
                row = json.loads(line)
                if row.get("kind") == "obj":
                    snap[row["key"]] = row
        return snap


# ------------------------------------------------------------------ the rules

def is_cover_class(cls, level_id):
    return cls in ENCLOSURES or (cls == "AnimScrubbables" and level_id in DOOR_LEVELS)


def holder_classes(obj):
    return [h.split("|")[1] for h in obj.get("holders", [])]


def lock_classes(obj, level_id):
    """The classes the lock votes with: holder_classes through LOCKED_AS."""
    out = []
    for holder in obj.get("holders", []):
        name, cls = holder.split("|")[:2]
        out.append(LOCKED_AS.get((level_id, name), cls))
    return out


def rule(obj, loaded, level_id, withheld, snap=None):
    """(locked, solid, why) as ObjectLock and AbilityLocks decide it.

    `loaded` is the same object in the control, whose flags are the game's
    own: GameHoldsStill reads those. `snap` gives a handle (a peel handle or
    a rag, `handleOf`) the pieces it acts on, whose verdicts it votes with."""
    classes = lock_classes(obj, level_id)
    targets = [snap[k] for k in obj.get("handleOf", []) if snap and k in snap]
    targets = [t for t in targets if holder_classes(t)]
    if not classes and not targets:
        return False, False, "no controller holds it"
    by_group = group_free = enclosure_free = False
    by_cover = by_other = by_set = set_locked = False
    for cls in classes:
        locked = CLASS_ABILITY.get(cls) in withheld
        if is_cover_class(cls, level_id):
            by_cover = True
        else:
            by_other = True
        if cls in DRAWER_SETS:
            by_set = True
            set_locked |= locked
        if cls in ENCLOSURES:
            enclosure_free |= not locked
            continue
        by_group = True
        group_free |= not locked
    for target in targets:
        # AbilityLocks.NoteHandles: one group vote per piece it acts on,
        # locked as that piece is.
        by_other = by_group = True
        group_free |= not rule(target, None, level_id, withheld)[0]
    is_locked = not (group_free if by_group else enclosure_free)
    cover = by_cover and not by_other
    drawer = obj.get("drawer")
    slides = bool(drawer) and (drawer.get("travel") or 0) > 0.05
    follows = not is_locked and set_locked and slides
    if follows:
        is_locked = True
    if is_locked and cover and by_set and drawer is not None and not slides:
        flags = loaded or obj
        if not drawer.get("tray") or flags.get("prevSel") or not flags.get("inter"):
            return False, False, "a drawer piece the game holds still"
    why = "its drawer is locked" if follows else ("every group holding it is locked" if is_locked else "a free group holds it")
    return is_locked, cover or follows, why


def configs_for(level):
    classes = {c["type"] for c in level["controllers"]}
    cfgs = [("all", list(ALL_ABILITIES))]
    for ability in ("Drawer", "Gadgets"):
        if any(CLASS_ABILITY.get(c) == ability for c in classes):
            cfgs.append((f"only:{ability}", [ability]))
            cfgs.append((f"allbut:{ability}", [a for a in ALL_ABILITIES if a != ability]))
    return cfgs


# ------------------------------------------------------------------ checks

def finding(sev, what, obj=None, detail=""):
    return {"sev": sev, "what": what, "key": (obj or {}).get("key", ""),
            "name": (obj or {}).get("name", ""), "detail": detail}


def visible(sprite):
    """Drawn and not see-through: [where, draws, r, g, b, a, dim]."""
    return sprite[1] and (sprite[5] is None or sprite[5] >= 0.05)


def drawn_undim(sprites):
    return [s[0] for s in sprites if visible(s) and not s[6]]


def check_locked(snap, control, level_id, withheld, cfg):
    out = []
    for key, o in snap.items():
        loaded = control.get(key)
        exp_locked, exp_solid, why = rule(o, loaded, level_id, withheld, snap)
        got = o.get("locked")
        if got is None:
            out.append(finding("FAIL", "the mod does not answer IsLocked", o))
            continue
        if got != exp_locked:
            out.append(finding("REVIEW", "locked, the rules say free" if got else "free, the rules say locked",
                               o, why))
        if not got:
            continue
        if exp_solid:
            if loaded and loaded["col"] == "on" and o["col"] != "on":
                out.append(finding("FAIL", "a locked cover lost its collider", o))
            if loaded and loaded["body"] == "sim" and o["body"] != "sim":
                out.append(finding("FAIL", "a locked cover lost its body", o))
            if o["inter"] and not o["prevSel"]:
                out.append(finding("FAIL", "a locked cover can be selected", o))
        else:
            if o["col"] == "on":
                out.append(finding("FAIL", "locked, collider still on", o))
            if o["body"] == "sim":
                out.append(finding("FAIL", "locked, body still simulated", o))
            if o.get("extraLive", 0) > 0:
                out.append(finding("REVIEW", "locked, with other live colliders", o, str(o["extraLive"])))
        drawn = [s for s in o["paint"] if visible(s)]
        if loaded:
            # A sprite clear in the control must not be greyed into view:
            # Daggers' clear drawer masks drew grey boxes while locked.
            clear = {x[0] for x in loaded.get("paint", []) if x[1] and x[5] is not None and x[5] < 0.05}
            boxes = [x[0] for x in o["paint"] if x[0] in clear and visible(x)]
            if boxes:
                out.append(finding("FAIL", "locked, a clear sprite greyed into view", o, ", ".join(boxes[:6])))
        undim = drawn_undim(o["paint"])
        if undim:
            out.append(finding("FAIL", "locked, not grey", o, ", ".join(undim)))
        art = drawn_undim(o.get("art", []))
        if not drawn and art:
            out.append(finding("REVIEW", "locked, drawn only by sprites the lock does not paint", o, ", ".join(art[:6])))
        elif art:
            out.append(finding("REVIEW", "locked, some drawn sprites are not grey", o, ", ".join(art[:6])))

    for lvl, pick, ability, want_locked, want_solid, source in EXPECT:
        if lvl != level_id or ability not in withheld:
            continue
        if pick.startswith("class:"):
            picked = [o for o in snap.values() if pick[6:] in holder_classes(o)]
        elif pick == "handles":
            picked = [o for o in snap.values() if "handleOf" in o]
        else:
            picked = [o for o in snap.values() if o["name"] == pick]
        if not picked:
            out.append(finding("FAIL", f"hand-tested object not found: {pick}", None, source))
        for o in picked:
            if o.get("locked") != want_locked:
                out.append(finding("FAIL", "hand-tested answer broken: " + ("should be locked" if want_locked else "should not be locked"), o, source))
            if want_locked and o.get("locked") and want_solid is not None:
                solid = o["col"] == "on" and o["body"] != "stop"
                if solid != want_solid:
                    out.append(finding("FAIL", "hand-tested answer broken: " + ("should stay solid" if want_solid else "should be frozen"), o, source))
            if not want_locked and o.get("dim"):
                out.append(finding("FAIL", "hand-tested answer broken: greyed", o, source))
    return out


def movable_drawers(snap):
    return [(k, o) for k, o in snap.items()
            if o.get("drawer") and o["drawer"]["state"] in ("Open", "Closed")
            and (o["drawer"].get("travel") or 0) > 0.05]


def fields(o):
    f = {x: o.get(x) for x in ("active", "inter", "prevSel", "interField", "prevSelField", "selectable",
                               "col", "body", "extraLive", "locked", "dim", "parent")}
    for s in o.get("paint", []):
        f["paint:" + s[0]] = tuple(s[1:6])
    for s in o.get("art", []):
        f["art:" + s[0]] = tuple(s[1:6])
    d = o.get("drawer")
    if d:
        f["drawer.state"] = d["state"]
        f["drawer.saved"] = tuple(sorted((k, v) for k, v in d.get("saved", [])))
    return f


def same(a, b):
    if isinstance(a, tuple) and isinstance(b, tuple) and len(a) == len(b):
        for x, y in zip(a, b):
            if isinstance(x, float) or isinstance(y, float):
                if x is None or y is None or abs(float(x) - float(y)) > 0.011:
                    return False
            elif x != y:
                return False
        return True
    return a == b


def noisy(*pairs):
    out = set()
    for a, b in pairs:
        if a is None or b is None:
            continue
        for key in set(a) | set(b):
            if key not in a or key not in b:
                out.add((key, "*"))
                continue
            fa, fb = fields(a[key]), fields(b[key])
            for name in set(fa) | set(fb):
                if not same(fa.get(name), fb.get(name)):
                    out.add((key, name))
    return out


def compare(got, want, noise, step):
    out = []
    for key in sorted(set(want) | set(got)):
        if (key, "*") in noise:
            continue
        if key not in got or key not in want:
            o = got.get(key) or want.get(key)
            out.append(finding("REVIEW", f"{step}: only in the {'locked run' if key in got else 'control'}", o))
            continue
        g, w = fields(got[key]), fields(want[key])
        for name in sorted(set(g) | set(w)):
            if (key, name) in noise or same(g.get(name), w.get(name)):
                continue
            o = got[key]
            gv, wv = g.get(name), w.get(name)
            detail = f"{name}: control {wv}, after the round trip {gv}"
            sev, what = "REVIEW", f"{step}: differs"
            if name in ("locked", "dim") and gv:
                sev, what = "FAIL", f"{step}: still locked or grey after everything came back"
            elif name == "selectable":
                if wv and not gv:
                    sev, what = "FAIL", f"{step}: cannot be picked up after everything came back"
                else:
                    what = f"{step}: can be picked up where the game did not allow it"
            elif name in ("inter", "prevSel", "interField", "prevSelField"):
                # Selectable is the game's own verdict and is compared on its
                # own; a flag that differs while it agrees changes nothing a
                # player can do (a closed drawer's contents, for one).
                if g.get("selectable") == w.get("selectable"):
                    sev, what = "INFO", f"{step}: a flag differs, selectable the same"
            elif name == "col" and wv == "on" and gv != "on":
                sev, what = "FAIL", f"{step}: collider not back"
            elif name == "body" and wv == "sim" and gv != "sim":
                sev, what = "FAIL", f"{step}: physics not back"
            elif name.startswith("paint:"):
                sev, what = "FAIL", f"{step}: colour not back"
            elif name == "drawer.state":
                sev, what = "FAIL", f"{step}: drawer not where it was put"
            elif name == "drawer.saved":
                dead = [k for k, v in (gv or ()) if not v and (k, True) in set(wv or ())]
                if dead:
                    sev, what = "FAIL", f"{step}: the drawer saved a piece as not interactable"
                    detail = ", ".join(dead[:6])
            out.append(finding(sev, what, o, detail))
    return out


def check_log(text):
    out = []
    for line in text.splitlines():
        if "re-entered itself" in line:
            out.append(finding("FAIL", "a lock pass re-entered itself", None, line.strip()[:200]))
        elif "[Error" in line or "Exception" in line:
            out.append(finding("FAIL", "an error in the log", None, line.strip()[:200]))
        elif re.search(r"\] check: ", line):
            out.append(finding("REVIEW", "a check was sent", None, line.strip()[:200]))
        elif re.search(r"\] reset: .*unlocked mid-level", line):
            # ObjectLock.ResetOnUnlockLevels: the three stacked levels reload
            # when their pieces unlock, by design.
            out.append(finding("INFO", "the level reloaded when its pieces unlocked", None, line.strip()[:200]))
        elif re.search(r"\] trap: stopped animations", line):
            # Part of any reset; a cat trap also logs "cat(s) reset the puzzle".
            continue
        elif re.search(r"\] trap: ", line):
            out.append(finding("REVIEW", "a trap sprang", None, line.strip()[:200]))
    return out


# ------------------------------------------------------------------ one level

class Level:
    def __init__(self, game, row, tag):
        self.g = game
        self.row = row
        self.tag = tag
        self.id = row["levelId"]
        self.index = row["levelIndex"]
        self.dir = SNAPS / str(self.index)
        self.opens = set()          # drawers the control's own open request moved
        self.closes = set()         # and its own close
        self.scrub_moves = {}

    def boot(self, withheld):
        g = self.g
        g.ask("revoke:" + (",".join(withheld) if withheld else "none"), r"revoke: now revoked", 8)
        arg = f"{self.index}:{SEED}" if self.row.get("isRandomizable") else f"{self.index}"
        g.read()
        if not g.dev(f"boot:{arg}"):
            raise RuntimeError("the game did not take the boot command")
        g.settle()
        m = g.ask("livelevels", r"livelevels: (\d+) loaded.*active=([^\r\n]*)", 10)
        if m is None:
            raise RuntimeError("livelevels did not answer")
        return int(m.group(1)), m.group(2).strip()

    def snap(self, name):
        return self.g.snapshot(self.dir / f"{name}.jsonl")

    def scrub(self, key):
        """Drive one scrub object as a drag does; True when it moved."""
        m = self.g.ask(f"scrub:{key}", r"scrub: '.*' percent before=(\S+) during=(\S+) after=(\S+)|scrub: no ", 10)
        if m is None or m.group(1) is None:
            return None
        vals = [float(x) if x != "null" else 0.0 for x in m.groups()]
        return abs(vals[1] - vals[0]) > 0.02 or abs(vals[2] - vals[0]) > 0.02

    def drawer_steps(self, snap, withheld, found, label):
        """Open, pass, close; a locked drawer is asked to move and must not."""
        g = self.g
        refused = {}
        drawers = movable_drawers(snap)
        for key, o in drawers:
            if o.get("locked"):
                want_open = o["drawer"]["state"] == "Closed"
                g.dev(f"drawers:{'open' if want_open else 'close'}:{key}")
                refused[key] = want_open
            elif o["drawer"]["state"] == "Closed":
                g.dev(f"drawers:open:{key}")
        if not drawers:
            return refused
        time.sleep(2.0)
        mid = self.snap(f"{label}-moved")
        if label == "control":
            # Which drawers the game's own open moves at all: Daggers' four do
            # not while the box is shut, locked or not.
            self.opens = {k for k, o in drawers if o["drawer"]["state"] == "Closed"
                          and mid and mid.get(k, {}).get("drawer", {}).get("open")}
        for key, want_open in refused.items():
            if key not in (self.opens if want_open else self.closes):
                found.append(finding("INFO", "the game's own move does not move this drawer when free either",
                                     snap[key]))
            elif mid and key in mid and mid[key].get("drawer", {}).get("open") == want_open:
                found.append(finding("FAIL", "a locked drawer moved", mid[key]))
        g.ask("revoke:" + (",".join(withheld) if withheld else "none"), r"revoke: now revoked", 8)
        time.sleep(1.0)
        for key, o in drawers:
            if not o.get("locked"):
                g.dev(f"drawers:close:{key}")
        time.sleep(2.0)
        return refused

    def put_back(self, start, label):
        """Drawers as they started (R), then every one open (O)."""
        g = self.g
        if not movable_drawers(start):
            r = self.snap(f"{label}-R")
            return r, r
        now = self.snap(f"{label}-before-R") or {}
        if label == "control":
            self.closes = {k for k, o in movable_drawers(start) if o["drawer"]["state"] == "Open"
                           and now.get(k, {}).get("drawer", {}).get("state") == "Closed"}
        for key, o in movable_drawers(start):
            cur = now.get(key, {}).get("drawer")
            if cur and cur["state"] != o["drawer"]["state"]:
                g.dev(f"drawers:{'open' if o['drawer']['state'] == 'Open' else 'close'}:{key}")
        time.sleep(2.0)
        r = self.snap(f"{label}-R")
        for _ in range(2):
            cur = self.snap(f"{label}-before-O") or {}
            shut = [k for k, o in movable_drawers(start) if cur.get(k, {}).get("drawer", {}).get("state") == "Closed"]
            for key in shut:
                g.dev(f"drawers:open:{key}")
            if shut:
                time.sleep(2.0)
        o = self.snap(f"{label}-O")
        return r, o

    def run(self, done):
        """Every config of this level not in `done`; returns the result rows."""
        g = self.g
        rows = []
        g.buf = ""
        live, active = self.boot([])
        ctl0 = self.snap("control-0")
        if ctl0 is None or active != self.id:
            return [self.result("control", "SKIP", [finding("REVIEW", "did not load", None, f"active={active} live={live}")])]
        time.sleep(3.0)
        ctl0t = self.snap("control-0t")
        self.drawer_steps(ctl0, [], [], "control")
        g.ask("revoke:none", r"revoke: now revoked", 8)
        time.sleep(1.0)
        ctl_r, ctl_o = self.put_back(ctl0, "control")
        # A drag on every scrub object while free: the locked runs' refusals
        # mean something only where this moves it.
        self.scrub_moves = {k: self.scrub(k) for k, o in ctl0.items() if o.get("scrub")}
        self.boot([])
        ctl0b = self.snap("control-0b")
        noise = noisy((ctl0, ctl0t), (ctl0, ctl0b))
        drawers = len(movable_drawers(ctl0))
        say(f"{self.tag} control: {len(ctl0)} objects, {drawers} sliding drawer(s), {len(noise)} field(s) change on their own")
        ctl_log = check_log(g.buf)
        if ctl_log and "control" not in done:
            rows.append(self.result("control", "REVIEW", ctl_log, 0, len(ctl0)))
            say(f"{self.tag} control: REVIEW ({summary(ctl_log)})")

        for cfg, withheld in configs_for(self.row):
            if cfg in done or (ONLY and cfg not in ONLY):
                continue
            g.buf = ""
            found = []
            live, active = self.boot(withheld)
            if live != 1:
                found.append(finding("FAIL", "more than one level live after the boot", None, str(live)))
            locked = self.snap(f"{cfg}-locked")
            if locked is None:
                rows.append(self.result(cfg, "SKIP", [finding("REVIEW", "no snapshot while locked")]))
                continue
            found += check_locked(locked, ctl0, self.id, set(withheld), cfg)
            for key, o in locked.items():
                if not (o.get("scrub") and o.get("locked")):
                    continue
                g.read()
                moved = self.scrub(key)
                refused = re.search(re.escape(f"'{o['name']}' is locked - its scrub is refused"), g.buf)
                if not refused:
                    found.append(finding("FAIL", "a locked scrub object's drag was not refused", o))
                if moved:
                    found.append(finding("FAIL", "a locked scrub object moved when dragged", o))
                elif not self.scrub_moves.get(key):
                    found.append(finding("INFO", "the drag moves this scrub object when free neither", o))
            # A lock pass while locked, as play runs them: it sees whatever the
            # game has switched back on since the level loaded (PawPrints'
            # colliders) and must remember it as the game's.
            g.ask("revoke:" + ",".join(withheld), r"revoke: now revoked", 8)
            time.sleep(1.0)
            refused = self.drawer_steps(locked, withheld, found, cfg)
            g.ask("revoke:none", r"revoke: now revoked", 8)
            g.settle(least=2.0, quiet=1.5, most=8.0)
            back = self.snap(f"{cfg}-unlocked") or {}
            for key, o in back.items():
                if o.get("locked"):
                    found.append(finding("FAIL", "still locked after everything came back", o))
                elif o.get("dim"):
                    found.append(finding("FAIL", "still grey after everything came back", o))
            for key, want_open in refused.items():
                d = back.get(key, {}).get("drawer")
                if key in (self.opens if want_open else self.closes) and d is not None and d.get("open") != want_open:
                    found.append(finding("FAIL", "a refused drawer move was not made on unlock", back[key]))
            r, o = self.put_back(ctl0, cfg)
            if r is not None and ctl_r is not None:
                found += compare(r, ctl_r, noise, "drawers as they started")
            if o is not None and ctl_o is not None:
                found += compare(o, ctl_o, noise, "every drawer open")
            found += check_log(g.buf)
            nlocked = sum(1 for x in locked.values() if x.get("locked"))
            status = "FAIL" if any(f["sev"] == "FAIL" for f in found) else (
                "REVIEW" if any(f["sev"] == "REVIEW" for f in found) else "PASS")
            rows.append(self.result(cfg, status, found, nlocked, len(locked)))
            say(f"{self.tag} {cfg}: {status} ({nlocked} of {len(locked)} locked"
                + (f"; {summary(found)}" if found else "") + ")")
        return rows

    def result(self, cfg, status, found, nlocked=0, total=0):
        return {"index": self.index, "level": self.id, "config": cfg, "status": status,
                "locked": nlocked, "objects": total, "findings": found}


def summary(found):
    counts = {}
    for f in found:
        k = f"{f['sev']} {f['what']}"
        counts[k] = counts.get(k, 0) + 1
    return "; ".join(f"{v}x {k}" for k, v in sorted(counts.items()))


# ------------------------------------------------------------------ main

def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("indices", nargs="*", type=int)
    p.add_argument("--all", action="store_true", help="every level with a controller an ability locks")
    p.add_argument("--resume", action="store_true", help="skip level configs already in results.jsonl")
    p.add_argument("--configs", default="", help="only these configs, e.g. all,only:Drawer")
    p.add_argument("--results", default="", help="write results here instead of results.jsonl")
    args = p.parse_args()
    global RESULTS
    if args.results:
        RESULTS = Path(args.results)
    ONLY.update(c for c in args.configs.split(",") if c)

    by_index = {r["levelIndex"]: r for r in LEVELS}
    if args.all:
        todo = [r for r in LEVELS if any(CLASS_ABILITY.get(c["type"]) for c in r["controllers"])]
    else:
        todo = [by_index[i] for i in args.indices if i in by_index]
    if not todo:
        p.error("name level indices, or --all")
    todo.sort(key=lambda r: r["levelIndex"])

    OUT.mkdir(parents=True, exist_ok=True)
    done = {}
    if args.resume and RESULTS.exists():
        for line in open(RESULTS, encoding="utf-8"):
            row = json.loads(line)
            done.setdefault(row["index"], set()).add(row["config"])

    game = Game()
    game.start()
    totals = {"PASS": 0, "REVIEW": 0, "FAIL": 0, "SKIP": 0}
    since_restart = 0
    for n, row in enumerate(todo, 1):
        tag = f"[{n}/{len(todo)} {row['levelIndex']} {row['levelId']}]"
        wanted = {c for c, _ in configs_for(row) if not ONLY or c in ONLY}
        if wanted <= done.get(row["levelIndex"], set()):
            continue
        if since_restart >= RESTART_EVERY or not game.running():
            if not game.running():
                say(f"{tag} the game is not running - starting it again")
            game.restart()
            since_restart = 0
        since_restart += 1
        level = Level(game, row, tag)
        try:
            rows = level.run(done.get(row["levelIndex"], set()))
        except Exception as e:     # a level that breaks the game must not end the sweep
            alive = game.running()
            rows = [level.result("control", "SKIP", [finding("FAIL" if not alive else "REVIEW",
                    "the game died" if not alive else "the probe could not finish", None, str(e)[:200])])]
            say(f"{tag} SKIP: {e}")
            game.restart()
            since_restart = 0
        with open(RESULTS, "a", encoding="utf-8") as f:
            for r in rows:
                f.write(json.dumps(r) + "\n")
                totals[r["status"]] = totals.get(r["status"], 0) + 1
        if all(r["status"] == "PASS" for r in rows):
            shutil.rmtree(level.dir, ignore_errors=True)

    game.dev("revoke:none")
    harness_env.close_game(quiet=True)
    say(f"Done: {len(todo)} level(s); {totals['PASS']} config(s) passed, {totals['REVIEW']} to review, "
        f"{totals['FAIL']} failed, {totals['SKIP']} skipped - {RESULTS}")


if __name__ == "__main__":
    main()
