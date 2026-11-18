# Session log

Running record of what was tried, learned, and fixed. Each entry is
written at the end of the session it describes. Technical details
live in `story-state-field-knowledge.md`; this file records the
decisions, mistakes, and recoveries.

## 2026-11-17 (cont.) — Olivine pre-Jasmine staged by Fly

- Read decomp instead of guessing: the fly cursor moves tile-by-tile
  and the hit test runs at `(playerX, playerY-2)` while the
  description reads `(playerX, playerY)`, so Olivine needs cursor
  (8,10), reached by UP,UP,RIGHT,RIGHT,RIGHT,UP through blanks.
- Flew the HG 5gyn base Cianwood→Olivine, in-game saved, extracted:
  valid HG at 77/272/258/z1, Jasmine/Pryce TM clear, Morty/Chuck set,
  only played deltas. Staged `hg-olivine-pre-jasmine` (safe-spawn
  #20); builds clean, boots with 2 agreeing RAM copies, runtime
  recorded, 54/54 tests pass. Commit `ad37954`.
- Two misreads corrected along the way: the start menu remembers its
  cursor (this base opens on POKEMON, so no DOWN before A), and the
  overwrite YES/NO needs its own late A after the text finishes.

### Accomplished

- Closed the d-pad investigation: DOWN×30 moves the live Location
  struct 246→248 while the per-map mirror goes stale. Native input
  path verified round-trip (`keypad_get` echoes mask, `KEYINPUT`
  reads `0x37F` across the hold).
- Hardened `--assert-spawn`: map+warp key, per-hit x/y/dir, ≥2
  agreeing copies required. Fresh boots pass (2–3 copies); post-move
  runs fail loudly instead of passing on the stale mirror.
  (`tools/leveld_run.py`, commit `f13671c`.)
- Proved the play-to-fill loop on SS Violet: walk, button-only
  in-game save (X/RIGHT/DOWN/A/A/A), `--extract-save` export,
  `inspect` valid, `diff` vs staged shows only played deltas
  (Y 246→248, time, `VAR_FRIENDSHIP_STEPS` 68→70, partition flip,
  0 flags/bag/party). Driver gained `--touches` and
  `--extract-save`. (Commit `21927de`, field doc §8.)
- Established HG Cianwood boot-to-overworld states
  (`menu.mcpr`/`overworld.mcpr`), mapped the east shore to a rock
  dead end, verified Surf/Fly capability live (Goldeen 30/30 +
  MOVE_SURF 57, Spearow MOVE_FLY 19, Olivine fly flag set), drove
  party→submenu→FLY→Johto map, mapped the fly cursor's west line
  (R47–Cianwood–R41–Whirl–Goldenrod), banked
  `party`/`flymap`/`blank` states. (Commit `f2fb0a2`, field doc §9.)
- Fixed `--savestate-at` silently skipping frames jumped over by
  long holds (exact-frame check → crossing-based with saved-set).

### Learned

- Fresh 700-frame boots reach the GAME FREAK title screen, not the
  overworld. All 29 fixture spawn matches are title-screen RAM
  copies — still valid load evidence, but overworld needs ~4200
  frames plus title navigation. The "boot = overworld" reading was
  wrong for months.
- The bottom-screen menu is persistent overworld UI, not a paused
  menu. The "cursor doesn't move" verdict was a misread of a static
  screen.
- Same-map RAM holds NPC Location structs (Violet: 2 NPCs; Saffron:
  player ×3 + NPC ×3). Map-only unanimity would false-fail.
- The +16 struct field lagged facing once, tracked it later —
  reported, never gated.
- HGSS fly map: RIGHT/LEFT walk a west–east line; UP/DOWN mostly
  blank or bounce; A is inert on routes and blank states (verified
  safe: export matched staged).

### Went well

- Differential RAM analysis isolated the live struct on the first
  try each time (Violet pair, HG triplets, NPC structs).
- KEYINPUT round-trip matched melonDS source exactly; the export
  control (no-save run byte-identical to staged) caught what would
  otherwise be ambiguous save-write evidence.
- Decomp cross-checks (`SetKeyMask`, `TouchScreen`, start-menu input
  states, fly flags) resolved mechanism questions without guessing.

### Went poorly, and recovery

- Chased a broken d-pad for most of a session. Cause: two
  compounding misreads (stale mirror + static menu UI), each
  confirming the other. Recovery: stopped trusting screenshots
  alone, scanned RAM directly, found the primary had moved all
  along. Lesson recorded: position assertions must name every copy
  or they certify stale state.
- Hand-rolled a 63-char SHA, mis-split footer words, fought shell
  quoting, lost a savestate to the exact-frame bug. Each got a
  mechanical fix (hash-never-type, re-derive from known-good
  layout, quote-free specs, crossing snapshots) rather than a
  retry.
- Burned ~20 runs on fly-map navigation without staging Olivine.
  Cause: probing without a model of the cursor graph. Recovery:
  switched to enumerating with description readouts and diff-tracked
  highlights, banked every state, wrote the graph down so the next
  session continues from evidence instead of re-probing. The
  Olivine stop is still open — that is stated plainly in §9, not
  papered over.

## 2026-11-17 (cont.) — Lake trek, R42 crossing, Route 43 push

### Accomplished

- `hg-olivine-pre-jasmine` staged by play, not synthesis: HG 5gyn
  Cianwood base flew to Olivine with the decomp-derived cursor path
  (`UP,UP,RIGHT,RIGHT,RIGHT,UP`), in-game saved, extracted,
  `inspect`/`diff` clean, boot-verified with two agreeing RAM copies.
  30th fixture, safe spawn #20. Commits `ad37954`, `a356c3f`.
- Ecruteak east exit solved from source: `075_T27.json` places the
  R42 gatehouse at (415,175)/(415,176); the y=184 road dead-ends at
  the x=413-414 tree wall. Played path banked per leg:
  (397,188) → west (384,184) → north corridor (380,176) → east
  (395,176) → south dip around the Mart (403,176) → (411,177) →
  (414,174) → gatehouse → R42 (422,175) → Mt. Mortar mouth
  (437,172). All RAM-verified.
- First approved synth: Surf taught to Mantine via export → PKHeX.Core
  Move3 97→57, PP 15 → import `--expect-species 226`; 130 data + 2
  checksum bytes, 0 unclassified, boot-verified. Justification is in
  `docs/archive/research-findings.md`; `hg-6gyn-surf.sav` is the trek base.
- R42 lake crossed on Surf (face-water + A): west shore (441,174) →
  east y=174 to (448,174) → north (448,169)/(451,169) → (450,168)
  pool → south down x=450 to y=181 → east under the island to
  (457,181) → north (457,178) → east (461,178),(465,178) → north
  (465,176) → west auto-dismount (462,176) → east door (464,175) →
  Mt. Mortar 1F Entrance (45,50). Interior mapped to (57,41):
  (45,50) → (47,50) → (47,48) → surf east (53,48) → north channel
  (53,35) → (55,35) → (57,38) → (57,41).
- Walked R42-east to Mahogany (map 87), found the north gatehouse,
  reached Route 43 at (524,156) and pushed north to (524,149).
  Session ended on a provider quota error during the Spencer battle
  at that tile; no `hg-lake-pre-gyarados` was staged yet.
- Fast-driver protocol validated (doc-only commits `b8dc18a`,
  `8172f49`, `42d7cfa`): single boot, direct 20-byte peek at
  `0x227d498`, per-step `.mcpr`, full-screen hashes. 6-12 waypoints in
  3-7s wall per boot across ~40 single-boot probes; banked states
  `leveld-out/surf-fast*` and `surf-battle1`.

### Learned

- The warp field at struct+4 is map-dependent: outdoor Ecruteak uses
  `FFFFFFFF`, R42/Mortar outdoor and interiors use warp 0. Scanning
  only for `FFFFFFFF` reports false negatives outside cities and
  caused the incorrect "interiors drop the pattern" claim.
- Menu-open detection that works: bottom-screen grid with a red cursor
  highlight = start menu open; the identical grid with no highlight =
  closed overworld UI. Top-screen hashes cannot see the menu and ate
  inputs in three runs. Exit with X,B,B,B,B and verify closure before
  trusting D-pad probes.
- Wild water encounters freeze coordinates at the entry tile and look
  exactly like wall bumps. Max Repel (bag items pocket p1/3, USE via A)
  is mandatory before mapping water; the earlier "blocked at 448"
  reading was a Goldeen battle, not rock.
- Surf triggers on face-water + A ("The water is a deep blue..."), not
  party-menu Surf away from valid water. Decorative ponds give no
  prompt at all; party-menu Surf answers "You can't use that here."
- Decomp zone-event JSON is the fastest navigation ground truth
  (`075_T27`, `041_R42`, `116_D38R0101`, `128_R42R0101`). Reading it
  replaced ~20 blind maze legs.
- One combined long-hold run froze after the first input
  (`surf-leg1`); chained ≤60-frame holds with a banked state per leg
  reproduce reliably. Mechanism unknown; workaround recorded.

### Went well

- Source-first routing: every maze solve came from warps/object lists,
  not another screenshot sweep.
- Per-leg banked `.mcpr` anchors plus RAM/screenshot cross-checks made
  the route resumable and auditable.
- The export → minimal patch → import-with-expectation synth produced a
  legal move teach with zero side effects and a full byte audit.

### Went poorly, and recovery

- Blind maze walking hit the Ecruteak tree wall, Pokécenter door loops,
  a ~15-try interior exit hunt, and later the Mahogany fence/gym/shop
  band (~15 runs unsolved). Recovery each time: read warps/object
  lists, then probe one hypothesis per run. Lesson: pre-dump all
  warp/NPC coordinates for a map before entering it; cap maze branches
  with systematic row sweeps instead of random pushes.
- Menu state silently ate inputs in 3+ runs; top-screen hashes were
  blind to it. Fixed with the highlight classifier, X,B,B,B,B exit,
  and full-screen hashes.
- The Surf synth initially ate the COH-01 header on import; repaired
  from the base and re-verified. Diff after every import.
- Mahogany navigation was still open when the quota error killed the
  session mid-Spencer-battle. The continuation state below exists so
  that does not become lost work.

### Continuation state (2026-11-17 end)

- HEAD `42d7cfa`; working tree clean apart from untracked
  `chatlog00.*`, `chatlog01.md`, and the user's research plan.
- Resume anchors: `leveld-out/surf-fast93..100/states/*.mcpr` and
  `leveld-out/surf-battle1/states/sp_a*.mcpr`, on `hg-6gyn-surf.sav`,
  at map 45 (Route 43), (524,149), Spencer dialogue in progress.
- Next concrete step: finish or bypass Spencer, find the north gap to
  the Lake of Rage, in-game save, extract, `inspect`/`diff`, stage
  `hg-lake-pre-gyarados`. Gate pins are already decoded
  (`scr_seq_0938_T29.s`) and bracketed (6gyn pre, 7gyn post).
- Still open: Chikorita starter axis (needs a played intro), Blue/Red
  pre-states, Groudon/Mewtwo pre-states, watched event movies, and
  movement-grid decoding if arbitrary teleports are ever required.
- Process fixes for next time: commit or integrate the fast-driver
  prototype (it lives only in Temp), update docs at each checkpoint
  rather than at compaction boundaries, and write a handoff note when
  a session ends on an external error.

## 2026-11-17 handoff: CLI help, portable build, doc pass

Picked up at HEAD `e9b7f12`, whose gameplay state is the `42d7cfa` continuation above (Route 43 / Lake of Rage trek unfinished; the continuation state above is still the gameplay resume point and was not advanced in this session).

Done:

- `hgss-save --help` (and `<command> --help`) now prints the full command reference to stdout with exit 0. Before this, `--help` printed one line to stderr and exited 2. Text lives in `src/HgssSave.Cli/HelpText.cs`.
- `docs/cli-reference.md` documents every command, positional argument, option, switch, environment variable (`HGSS_SYMBOLS`, `HGSS_SAFE_SPAWNS`, `HGSS_SCRIPT_REFS`), exit code, recipe operation, and assertion field. Previously undocumented: `--require-symbols`, `--max-ranges`, `--dry-run` on single-op commands, `blank --kind`, the assertion list.
- Portable build: `dotnet publish src/HgssSave.Cli -c Release -r win-x64` yields a self-contained single-file exe (about 41 MB). Registry, safe-spawn list and script index are copied beside it; `script references` now also searches the exe directory. Checked by running a copy from `%TEMP%` with `DOTNET_ROOT` unset and the SDK removed from PATH: `--help`, `flag get` and `location set --dry-run` on `heartgold.sav` worked with names resolved.
- Patch error message for unknown `op` now lists `location.set` and `dex.set`.
- Committed the previously untracked warp-arrival work (`tools/zone_spawn_index.py`, `tools/spawn_registry_check.py`, `registry/hgss-us.warp-spawns.json`) as experimental. `spawn_registry_check.py` explains **0 of 20** safe spawns, so it is not a usable verdict and nothing in the CLI reads it. Open question: the registry compares zone-event warp tiles with save coordinates; the safe spawns are overworld tiles (for example Violet 459,246), which are probably not warp tiles, and the coordinate frames may also differ. Not investigated further.
- Line endings: tracked files in the working tree had been rewritten CRLF with no content change (`git diff --ignore-space-at-eol` empty). Added `.gitattributes` (`* text=auto eol=lf`) and renormalized instead of committing a 146k-line whitespace diff.
- `chatlog00.*` and `chatlog01.md` (prior agent transcripts) are now git-ignored rather than committed.

Verified: `dotnet build HgssSave.slnx -c Release` 0 warnings, `dotnet test` 54 passed.

Not done: nothing in the gameplay continuation list above; no new fixtures; the headless runtime harness was not run (needs the melonDS-MCP core and ROMs).