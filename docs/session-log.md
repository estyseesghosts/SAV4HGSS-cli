# Session log

Running record of what was tried, learned, and fixed. Each entry is
written at the end of the session it describes. Technical details
live in `story-state-field-knowledge.md`; this file records the
decisions, mistakes, and recoveries.

## 2026-11-17 — d-pad exonerated, play-to-fill proven, Olivine trek started

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
