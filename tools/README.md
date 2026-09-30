# tools/

Every script here, by what it is for. Look here before writing a new one.
"Game" says whether it launches or drives A Little To The Left. Anything that
does wraps the player's saves and configs in `harness_env` (snapshot, then
restore), and never runs while a player is mid-game on the same install.

The cost of each rung, and which question each answers first, is the test
ladder in the repo's `CLAUDE.md`.

## Checks CI runs (no game)

| Tool | What it checks |
|---|---|
| `check-docs.py` | Relative links resolve, one `<summary>` per member, no `file:line` citations in code, stated versions match the manifest |
| `check-devtools.py` | Every DevTools command is dispatched and documented in `docs/dev/devtools.md`, both ways |
| `check-patches.py` | Every Harmony class is patched and every tick is called by the plugin |
| `check-slot-defaults.py` | SlotData's defaults match the yaml option defaults |
| `check-version.py` | The version agrees in its three files; `--set X.Y.Z` writes all three |
| `check-release-assets.py` | The three shipped files, not their sources |

## Build and release

| Tool | Game | What it does |
|---|---|---|
| `deploy.sh` | writes | Build both plugins and copy them into the game (closes it first). `--no-kill`: compile only, never deploys |
| `ap-sync.ps1` | no | Copy `apworld/alttl` into the local `Archipelago/` clone for tests |
| `build_apworld.py` | no | Package the world as `alttl.apworld` |
| `package-release.py` | no | Build all three release assets and refuse if their versions disagree |

## The release gate and its paper tests

| Tool | Game | What it does |
|---|---|---|
| `release_e2e.py` | yes | The gate: install the release as a player would and play a short run. `--only-arrow`, `--quick` |
| `make-seed.py` | no | Generate the gate's base or DLC seed (`--dlc`) without the game |
| `test_harness_data.py` | no | The harness's data layer against a real seed |
| `test_scheduler.py` | no | The harness's slot choice, over synthetic worlds |
| `test_run_model.py` | no | Data and choice together: can this seed be cleared at all |
| `predict_gate.py` | no | The gate's verdict on paper; set `ALTTL_SEED_DIR` for the DLC seed |
| `harvest-forced-endings.py` | no | Which ending each level's forced completion reports, from the gate logs (writes `fixtures/forced-endings.tsv`) |
| `mutate-scheduler.py` | no | Put each scheduler bug back; the suite must catch it |
| `mutate-apworld.py` | no | Put each DLC bug back; the world's tests must catch it |
| `harness_env.py` | - | Snapshot and restore the player's saves, configs and display settings; `--restore-latest` after a hard kill |

## Hand tests (a person plays)

| Tool | Game | What it does |
|---|---|---|
| `handtest-level.py` | yes | One level, holding exactly the abilities named; `--grant` more mid-test, `--achievements` for achievement checks |
| `handtest-queue.py` | yes | The queue of levels still to check, and a driver for it |
| `handtest.py` | yes | Serve a seed (`--serve`) for a reachability hand test |
| `watch-handtest.py` | reads log | Wake on a crash, an error, or the step's `--until` line while someone plays; beside a probe, `--errors-only --wait-for-game 300` stays silent unless something breaks |
| `make-playtest-seed.py` | no | A broad seed for a human playtest, with what is in it |
| `record-unlocks.py` | yes | Record what unlocks what while a person plays one level |
| `analyse-unlocks.py` | no | Turn those recordings into edge proposals |
| `tested-levels.txt` | - | Levels a human has played in a run |

## Level data

| Tool | Game | What it does |
|---|---|---|
| `add-edges.py` | no | Add `dependsOn` edges to levels.json, reviewably |
| `merge-levels.py` | no | Splice swept rows into levels.json |
| `merge-drawers.py` | no | Backfill the `drawers` block onto older rows |
| `levelsweep.py` | yes | The DevTools level sweep with the mod parked |
| `classify-controllers.py` | no | Classify every controller; writes `docs/reference/controller-classes.tsv` |
| `check-game-facts.py` | no | Compare the level table with a dump from the running game |

## Probes (one question each, answered in game)

| Tool | The question |
|---|---|
| `probe-forceable.py` | Does every level behave as the gate's paper plan assumes? (writes `fixtures/forceability.jsonl`) |
| `probe-lock-roundtrip.py` | Lock every level, give everything back: does it come back as it was? |
| `probe-slots.py` | Can the harness handle each level of a seed, one at a time? |
| `probe-session.py` | The same levels in one session, the way the gate plays them |
| `probe-isolation.py` | The five gate assertions with no tool of their own |
| `probe-solved-at-load.py` | Which controllers are solved the moment a level opens? |
| `probe-ability-locks.py` | Do the locks lock anything, and what do levels need? |
| `probe-blocked.py` | What does the game itself refuse to let you touch? |
| `probe-dead-controllers.py` | Which controllers report solved, which are dead? |
| `probe-dead-dlc.py` | Which controllers register but never pay a check? |
| `probe-dlc.py` | Does a DLC puzzle play in a run end to end? |
| `probe-dlc-nav.py` | Where does a finished DLC puzzle send you? |
| `probe-instance-checks.py` | Does a generator drawn twice pay out both instances? |
| `probe-launch-count.py` | Does one harness launch produce one game? |
| `probe-level-requirements.py` | Which controllers does each level need to complete? |
| `probe-object-sharing.py` | Which ability gates can be bypassed? (writes `docs/reference/gate-sharing.md`) |
| `probe-toothless-gates.py` | Which ability gates gate nothing? (writes `docs/reference/toothless-gates.md`) |
| `probe-occlusion.py` / `analyse-occlusion.py` | Which groups sit under another group's objects? |
| `blocking.py` / `analyse-stuck.py` | Ability-locked objects on top of free ones; which stuck groups understate a requirement |
| `probe-offline-goal.py` | Does a run finished offline report its goal on reconnect? |
| `probe-skip-beaten.py` / `probe-skip-path.py` | What a Skip does on a beaten puzzle; can the harness get past an unforceable one |
| `probe-star.py` | What the save records when a level is finished |
| `probe-trap-window.py` / `repro-trap-freeze.py` | When a cat trap goes off: a settled puzzle, a load, a real straight-on finish, the retry panel, a level's own cat (the four `CatGrab` levels), the credits (own seed, muted, PASS/FAIL per case); the 0.3.1 trap freeze and its guard |
| `probe-unblock.py` / `probe-unlock.py` | Which solve stops the game blocking others; lock, grant, solve |
| `emptysoak.py` | Hammer level loads looking for the blank-level bug |
| `offline_test.py` / `offline-reconnect-test.py` | A run survives the server going away; Connect during an offline run |
| `playthrough.py` | Drive a whole run to the credits |
| `capture-ability-strip.py` / `crop-ability-strip.py` | The README's ability-strip pictures |
