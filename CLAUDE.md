# ALTTL Archipelago - how to work in this repo

BepInEx IL2CPP mod for A Little To The Left (`src/`) plus its Archipelago world
(`apworld/alttl/`). The user is droha. They test by playing; they read chat in the
VS Code extension, which shows no background task output and no status bar.

This file is the playbook. It beats anything a plugin, skill or old doc says.
Keep it short: add a rule here as one line, never as a story.

## 1. Keep working

- Decide, say which in one line, continue. Do not ask a question the task
  already answers. Do not propose reverting as a first response to trouble.
- End a turn ONLY when one of these is true, and make the last line say which:
  - `Done.` - the task is finished and verified.
  - `Waiting on you: <exact thing to do>` - droha has to play or decide.
  - `Running in background: <what>, updates every 2 min.` - and nothing
    independent is left to do meanwhile.
- A summary is at most ~8 lines and is never the end of the work.
- "Set it up" means do all of it (server, seed, connection, launch settings)
  without asking. droha never runs commands for you.
- Only droha's call: commits/pushes, changing `levels.json` logic without a
  hand-test, anything needing them at the keyboard.

## 2. Simplest thing first

- If the question is "can a player do X in level Y", **ask droha to play it**.
  One playthrough beats any probe: on 2026-09-22 three of four new detectors
  gave confident wrong answers and every real fix came from droha playing.
- Setup for that: `py -3.13 tools/handtest-queue.py --build` then `--next`
  (or `tools/handtest-level.py <index> [ability ...]` for one level), then give
  numbered steps and the exact question. Record the verdict with `--answer`.
- Before writing a new tool, look in `tools/` (60+ scripts). Do not write a new
  probe when a hand test answers it. Never build more than one new instrument
  per task without saying why.
- Prefer one Read/Grep over an inline `py -c` script.

## 3. Test ladder - smallest rung that answers the question

| Question | Command | Cost |
|---|---|---|
| Core logic | `dotnet test src/ALTTLArchipelago.Core.Tests` | ~1 s |
| apworld / generation | `powershell tools/ap-sync.ps1` then in `Archipelago/`: `py -3.13 -m unittest discover -s worlds/alttl/test -t .` | ~7 s |
| Fill still works | `ALTTL_STRESS_SEEDS=25 py -3.13 -m unittest worlds.alttl.test.test_fill_stress` (in `Archipelago/`) | ~30 s |
| Harness logic | `py -3.13 tools/test_scheduler.py`, `tools/test_harness_data.py` | ms |
| One level in game | `tools/handtest-level.py`, `tools/probe-slots.py` | ~2 min |
| Did I break a run | `tools/release_e2e.py --quick` | ~4 min |
| Release sign-off | `tools/release_e2e.py` (full) | ~15 min |

- **Full runs are release gates, never debuggers.** That includes any probe
  sweep over many levels. Fix it at the lowest rung that can see the bug.
- A new instrument runs on ONE level with a known answer before any sweep,
  and a sweep is scoped to levels that can carry the bug.
- A test is not trusted until it has been seen to fail on the bug.
- Before a gate, name the offline test that would go red if the change were
  wrong. If there is none, write it first.

## 4. Background runs - one recipe, every time

1. Start the job with `run_in_background`, stdout to a file, `2>/dev/null`
   (or its own `.err` file). Never pipe it through grep/tail/head.
2. In the same message arm a Monitor: a loop that every 120 s prints ONE
   self-contained line from the output (`[n/total ID] step: detail`), prints
   `STALLED <n>s` if the file stopped growing, and exits when the file has
   not grown for two polls. `timeout_ms: 1800000`; re-arm on expiry.
   Description = what is running, e.g. `probe-blocked 51 levels`.
3. On each monitor event, reply with that one line as plain text. droha sees
   only the description, never the event body.
4. Keep doing independent work meanwhile. Stop the monitor with TaskStop when
   the job ends. Say `Nothing is running.` when that is true.

## 5. The game

- Exe: `G:\Games\Steam\steamapps\common\A Little To The Left\A Little To The Left.exe`.
  Start it directly with NO arguments (never `steam://`: family-shared, "no
  license"; Unity args pop a dialog). Both failures look like a hang.
- Window: 720p for automated tests via DevTools `setres:1280x720`. The game
  ignores the registry for size, so registry reads are not evidence. Never
  fullscreen, never 4K.
- DevTools commands go in `<game>/BepInEx/alttl-devtools-commands.txt`; see
  `docs/devtools.md`. Log: `<game>/BepInEx/LogOutput.log`.
- Say before you launch or close the game. If droha is about to play, set
  everything up and let THEM open it. Close it when your testing is done.
- Screenshots: take one to check a level state instead of arguing about it;
  delete the file right after reading it.

## 6. Test server

- `tools/handtest.py --serve` / `handtest-level.py` stand one up correctly;
  prefer them over doing it by hand.
- By hand: check port 38281 is free first
  (`powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort 38281"`),
  pick the seed zip deliberately, feed MultiServer an EMPTY command file
  (`testserver/servercmd.txt` holds old cheats), confirm "server listening"
  and "No save data found". Stop it when done.
- Never touch droha's live archipelago.gg session.

## 7. Level-data facts

- Understating a requirement softlocks a seed; overstating is safe. Never
  remove a requirement without hand-test evidence.
- Level shapes droha named: **PROGRESSIVE** (later phases appear by playing,
  e.g. Tupperware Nesting, Tupperware Tower, Desktop Computer), **COVERING** (a
  drawer or cupboard sits over items, e.g. chalk, Clock Cupboard), **NEITHER**
  (alternate arrangements; not a gating bug, e.g. Fruit Stickers).
- Hand-test results go in `apworld/alttl/data/proven-requirements.json` and
  edges in `apworld/alttl/data/levels.json` (via `tools/add-edges.py`). That
  is the single place; do not scatter findings into docs.
- Level-scoped edges stay level-scoped (e.g. Music Box Organizer -> Ordering).

## 8. Docs

Read when relevant: `README.md`, `docs/installation.md`, `docs/devtools.md`,
`docs/release-testing.md`, `docs/in-game-testing.md`.

History, do NOT read by default (large, mostly past incidents):
`CHANGELOG.md`, `docs/verification-log.md`, `docs/research-findings.md`,
`docs/superpowers/`, `docs/data/*.md`.

- Answer chat questions in chat. Do not create a new `.md` file unless asked.
- Commit messages: subject + a few lines. Not essays.
- Docstrings say what a tool does and how to run it; war stories go nowhere.

## 9. Git

Global rules apply (never commit/push without an explicit "commit"/"push").
Run `git add` and `git commit` as separate commands, never chained with `&&`.
Never commit interop assemblies derived from the game.
