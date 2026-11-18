# HGSS Save-State Reverse Engineering: Experimental Research Plan

**Purpose:** Use authentic Pokémon HeartGold/SoulSilver save files to discover undocumented flag/variable meanings, recover story-state dependencies, identify non-flag state that affects progression, and produce reproducible, independently verified regression fixtures for Kohjoh and Lotus.

**Status:** Research specification, not a claim that the questions below are already answered. **Prepared:** 2026-11-16. **Primary targets:** US HeartGold and SoulSilver (extend to other languages/regions after establishing a baseline).

**Main deliverables:** (1) HGSS save-state atlas; (2) searchable script-to-save dependency graph; (3) machine-readable evidence and symbol registry; (4) verified pre-/post-event save corpus; (5) executable regression scenarios; (6) a reproducibility report that separates observed behavior from inference.

## Executive decisions

1. **Use real gameplay saves as ground truth.** Start from an unmodified in-game save made on an NDS/3DS or a known-correct emulator; record its origin. Do not manufacture a story checkpoint by toggling flags and assume it is canonical.
2. **Use PKHeX.Core for semantic decoding, serialization and first-pass checksum validation.** Add an independent raw-block inspector for both partitions, counters and untouched byte ranges. Do not let a library's automatic rewrites become experimental observations.
3. **Use `pret/pokeheartgold` as a source of hypotheses, not absolute proof.** Parse C, assembly and field scripts into a reference graph. Follow calls and conditional branches; symbols named `UNK` do not imply that their effects are unknown, and descriptive names are not necessarily complete.
4. **Measure transitions, not just final saves.** Collect *before → event → immediate after → map reload → reload from save* observations. Some effects are temporary, time-dependent, reconstructed or stored outside the event-flag array.
5. **Ask causal questions.** A before/after diff tells you what changed, not which change caused an observed effect. Use one-factor interventions, negative controls, restores and repeated trials.
6. **Treat HG and SS independently at version-gated events.** Test both unless source shows the path is identical. A foreign public save, different language, altered ROM or unusual flash-cart/container format is a separate cohort.
7. **Do not rewrite captured baseline saves.** Keep originals immutable, SHA-256 indexed, with provenance and consent where needed. Work on copies; store readable JSON projections separately.

---

## 1. What is known, and what remains uncertain

The outer binary format is not the main blocker. PKHeX.Core already models the two `0x40000`-byte partitions in a `0x80000`-byte raw save, active general/storage blocks, counters, checksums, trainer/party/box data, inventory, most common miscellaneous structures, and the 2,912 persistent event-flag positions plus 368 variable positions. In `SAV4HGSS`, the general block is `0xF628` bytes, PC storage is `0x12310` bytes beginning at partition-relative `0xF700`, event variables begin at general-relative `0x0DE4`, and flags at `0x10C4`.

On the inspected `pokeheartgold` default branch, `include/constants/flags.h` still declares **1,133 `FLAG_UNK_*` symbols**, and `include/constants/vars.h` declares **249 `VAR_UNK_*` symbols**. These are *unresolved labels*, **not** 1,382 proven novel mechanisms: some can be inferred by script usage; some are dead, padding, unused, aliases, or state with no known in-game path. Neither the total number nor the presence of an `UNK` label tells us how many behaviorally relevant unknowns remain. Recompute these statistics at each pinned upstream revision and measure reference coverage separately.

Distinct, partially understood problems:

- **Field meaning:** What does one persistent flag or variable encode? What are its valid values and lifetime?
- **Joint meaning:** Which *combination* of saved fields makes an event possible, visible, complete, or replayable?
- **Transition meaning:** Which fields are tested/changed before and after script calls, native functions, battles, warps, menus or clock ticks?
- **Save/load meaning:** Which values survive a save/load, which are recomputed, cleared or mirrored, and which must never be injected as a persistent checkpoint?
- **Source coverage:** How do the script tables, map headers, temporary flags, saved map objects, item/party systems, roaming records, RTC, phone and other save substructures interact?
- **Game fidelity:** How does vanilla HGSS behave when given a valid but unusual state, and does Kohjoh reproduce that behavior?

**Confirmed lifecycle caveat:** `src/script_manager.c` implements `ClearTempFieldEventData`, which zeros 64 map-temporary flags and 32 temporary variables, while `ClearDailyFlags` resets a different flag range. `src/save_vars_flags.c` also has a separate in-memory temporary flag array for IDs at/above `TEMP_FLAG_BASE`. The saved variable array alone is therefore insufficient to recreate a running script. `src/unk_02055418.c` processes real-clock day advances and changes many systems beyond a flag table.

---

## 2. Evidence hierarchy and definitions

Every finding must say exactly which kind of evidence supports it.

| Evidence | Meaning | Can establish |
|---|---|---|
| `SOURCE` | Specific source lines/branch of `pokeheartgold`, or disassembly, with pinned commit | Code-path hypothesis and reachable operations |
| `SAVE_DIFF` | Authentic paired saves decoded and compared | What persistently changed under controlled conditions |
| `INTERVENTION` | Edited *copy* with one controlled alteration against a matched control | Causal impact, within tested contexts |
| `RUNTIME` | Log, screenshot/video or emulator trace from executing the relevant path | That a behavior actually occurred |
| `HARDWARE` | Same transition verified on retail NDS/3DS | Real-console agreement |
| `NEGATIVE` | Predicted event/field did *not* change under a specified control | Bounds on a claim; not proof of impossibility |

Status classes: **unexamined**, **source-inferred**, **observed**, **causally supported**, **cross-version verified**, **unresolved**, **contradicted**, **confirmed unused under bounded search**. Never label a symbol “unused” merely because a literal text search found nothing; aliasing, computed IDs, native access and indirect calls matter.

**What counts as a reproducible finding?** An immutable source save, ordered inputs, tool/source revision, original and modified semantic state, observation trace, expected vs actual outcome, a repeat or explicit single-shot limitation, and a named counterexample or negative control. For broad claims, run both HG and SS plus multiple progression contexts.

---

## 3. Acquisition and capture design

### 3.1 Collect real saves, not just isolated endgame files

Build a *checkpoint ladder* from at least one complete organic HG and one organic SS playthrough. Favor capture points **before and after** major events, rather than sparse checkpoints far apart. Minimum ladder:

1. New game, first save, starter selection, Egg delivery/return, early Pokédex/phone unlocks.
2. Before/after each Johto badge; before/after mandatory HM gates and travel unlocks.
3. Before/after Slowpoke Well, Rocket Hideout, Radio Tower takeover and resolution.
4. Before/after each Kimono Girls dependency, Dance Theater event and legendary summit/chamber sequences.
5. Before each Ho-Oh/Lugia event, immediately after battle outcome (catch/defeat/run/blackout where reachable), and after leaving/re-entering.
6. Before and after Elite Four, credits, return to world, Kanto access, Kanto badges, Mt. Silver unlock, Red.
7. Selected optional systems: roaming Pokémon, Safari Zone, Pokéathlon, Pokéwalker, Apricorn/Kurt processing, phone/rematches, Bug-Catching Contest, Mystery Gift and daily events.
8. Controlled save/load experiments in safe interior/exterior maps, including normal saving, PC modifications and repeated loads.

**Important limitation:** Some cutscenes do not permit an in-game save in the middle. Capture the nearest legal pre/post save and use Lotus emulator savestates, input replay or instrumentation for mid-cutscene evidence. Do **not** call an emulator savestate an HGSS `.sav`.

### 3.2 Matrix and controlled conditions

| Variable | Baseline | Additional cohorts |
|---|---|---|
| Game | US HG and US SS | JP/KR/other language as needed |
| Runtime | Vanilla ROM on DS-compatible hardware; known-version emulator | Kohjoh and Lotus |
| Clock | Recorded date, time, weekday, time zone/RTC setup | Controlled midnight/day/week rollovers and clock rollback |
| Save operation | Game menu “Save” and full completion | Second no-op save, PC-edit save, power-interrupted experiments only on expendable copies |
| Party | Keep a fixed, ordinary legal party per local experiment | Empty/eggs/fainting/follower/form edge cases |
| Items | Record all items and key items | One controlled presence/absence change |
| Position | Same map, tile, facing, object context | Warp/return/location-sensitive checks |
| RNG/time | As close as possible and recorded | Repeated trials to classify natural variability |

Avoid altering trainer identity, TID/SID, MAC, play-time, RTC or Pokémon together with story flags unless they are the variables under test. Public third-party saves may contain personal names and hardware identifiers; do not publish raw copies without permission and sanitization review.

### 3.3 Canonical capture protocol

For a candidate event **E**:

1. **Pre-capture:** Save organically at the nearest legal point before E. Preserve raw file `S0`, its provenance and SHA-256; record game, map, coordinates, party, inventory, clock, variant and visible state.
2. **Control:** Reload `S0`, perform *no event*, save once under the same conditions as possible (`C1`). This exposes routine saves' counters, timestamps, housekeeping, changing blocks and RNG drift.
3. **Treatment:** Reload `S0`, trigger only E, complete the relevant branch, save (`T1`). If possible capture intermediate runtime telemetry and post-event state before saving.
4. **Stability:** Reload `T1`; observe if E persists/vanishes; leave and return; save again (`T2`). Compare `T1 → T2` to detect normalization and deferred writes.
5. **Branches:** Repeat from fresh `S0` for each reachable outcome (e.g. catch vs KO); repeat at least twice when RNG or timing affects results.
6. **Causal validation:** Take `S0` or `T1`, alter one candidate field at a time on an isolated copy, reserialize, validate, load and observe. Compare to an identically rewritten but unchanged control. Restore the copy between interventions.
7. **Cross-context:** Test the effect when one likely prerequisite is missing, in the other version, and across save/load or warp boundaries.
8. **Evidence:** Produce semantic and raw diff, condition/action trace, result, screenshot/video/log as available, and an explicit confidence statement.

If a valid save exists only *after* E, infer necessary transitions from source and collect a real earlier checkpoint; do not backsolve by clearing one completion bit and declare success.

---

## 4. What the save-diff tooling must actually compute

**Do not rank unknown flags by how frequently the raw `.sav` bytes changed.** An in-game save updates counters, CRCs, redundant partitions, PC bookkeeping, RTC, game records and possibly unrelated daily/RNG state. PKHeX export itself may mark all boxes modified. Use several comparison views.

1. **Raw integrity report:** file size/format, header/wrapper normalization status, two partitions, active general/storage copy, each auxiliary block, revision counters, calculated/stored CRC, uninitialized vs invalid. Never assume partition A is current, or that the general and storage active copies match.
2. **Canonical decoded state:** trainer, location and all known position/warp fields, 368 `u16` variables, 2,912 flag bits, party/PC species and metadata, inventory, badges, Dex, roamers, map objects, phone, RTC and other typed structures that PKHeX/decomp can decode.
3. **Semantic delta:** `flag 0x109: false→true`, `var 0x4077: 2→3`, `MAP`, `item TIDAL_BELL`, `roamer Raikou route`, etc., with field-specific paths, not offsets only.
4. **Uninterpreted byte-range delta:** changes left after removing known structural regions, checksums and counters. Retain old/new slices and local neighborhood with absolute + partition-relative offsets; do not silently discard unexplained changes.
5. **Control subtraction:** classify `S0→C1` as background variability and `S0→T1` as treatment; highlight treatment-specific changes but keep joint effects and overlaps. Subtraction is evidence prioritization, not proof of causation.
6. **Round-trip view:** unchanged `PKHeX.Core` load→write, original game reload→resave, and decompressor/serializer reparse. Identify changes caused by tools or in-game housekeeping separately.
7. **Replay assertion:** a schema of expected *behavior*, including absent behaviors, not just expected values in the output save.

Important pitfalls: a value may change in memory but not yet be saved; an ID may be tested through a helper with no literal `FLAG_` token; flags may be reset on map transition; the base save may encode an invalid combination; a PC save can write more box data than a normal field save; file wrappers such as NO$GBA or `.dsv` must be converted separately and validated, not “fixed” by arbitrary truncation.

### Recommended CLI surface (proposed, not existing commands)

```sh
hgss-save inspect pre.sav --all --json > pre.json
hgss-save diff pre.sav post.sav --semantic --raw-unknown --json > delta.json
hgss-save diff pre.sav control.sav --semantic --json > noise.json
hgss-save trace-symbol FLAG_UNK_109 --source ./pokeheartgold
hgss-save patch pre.sav --patch candidates/flag-109-on.json --out run/flag-109-on.sav
hgss-save validate run/flag-109-on.sav --strict
hgss-save corpus check --manifest corpus/manifest.yaml
hgss-save experiment report experiments/LUG-05.yaml --json
```

All writes must be atomic to a **new** path, fail on unsupported symbols/overflow, and report checksums, changed ranges and source/target SHA-256. `--strict` should reject unrecognized container format, invalid input blocks and unexpected mutations instead of trying to repair them silently.

---

## 5. Research-question bank

Each question is an experiment, not a claim that a specific flag necessarily behaves a certain way. **P0:** required for canonical story fixtures or avoiding false findings. **P1:** broad coverage. **P2:** obscure mechanics or long-tail validation. IDs are stable task identifiers. Work from the nearest *authentic* before/after save, use a no-event control, and attach source references and observed state as defined above.

### A. Save mechanics, active-block selection and serializer correctness

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **FMT-01** | P0 | Does the game always load the general block with the newest valid revision, or can a bad CRC force fallback to the older copy? | Two-partition authentic save; corrupt *one copy only* on disposable file; observe accepted player data and repair behavior; record counters/CRCs. |
| **FMT-02** | P0 | Can the active general and PC blocks come from different partitions after normal gameplay? | Compare sequential genuine saves with and without PC edits; log independent selection and changed-box flags. |
| **FMT-03** | P0 | Which bytes change on an ordinary no-op save, and which are deterministic? | `S0→C1→C2` with no movement or progression; repeat at fixed RTC and with time advance. |
| **FMT-04** | P0 | How does the game treat a PKHeX.Core no-op export compared with an untouched save? | Load and immediately export copy; raw/semantic diff; game load-resave each independently; compare retention. |
| **FMT-05** | P0 | Can editing only general state inadvertently undo an independently newer PC block? | Arrange distinct active copies, patch a single flag, write/reload; verify every box and both main-block selections. |
| **FMT-06** | P1 | Which auxiliary chunks are created only after Hall of Fame, Battle Hall or battle-video activity, and how are they recovered after a bad copy? | Pre/post records of each feature; intentionally invalidate one aux copy on duplicate, not original; observe fallback. |
| **FMT-07** | P1 | Do repeated game saves normalize or overwrite externally edited but valid metadata? | Patch an innocuous field, save twice in game; compare footer, changed-box mask, backup data, unknown bytes. |
| **FMT-08** | P2 | What exact recovery path occurs when one partition's main-block checksum is wrong versus when both are wrong? | Matrix of isolated CRC faults; distinguish load-warning, fallback and refusal; protect originals. |

### B. Lifetimes: map-temporary, persistent, daily and runtime state

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **LIFE-01** | P0 | Which of the first 64 map-temp flags are ever written by scripts, and exactly when are they reset? | Generate static references; capture in-memory before/after map transition; save when legal and test reload. |
| **LIFE-02** | P0 | Which of `VAR_TEMP_x4000`–`VAR_TEMP_x401F` survive a save and which are cleared before any consumer reads them? | Trace `ClearTempFieldEventData`, instrument read/write on transition, compare save data and runtime state. |
| **LIFE-03** | P0 | What is the actual lifetime of special variables beginning at `0x8000` versus saved `0x4000` variables? | Attempt to set both during scripts; observe script-local results, raw save absence/presence and reload. |
| **LIFE-04** | P1 | Which flags reset at midnight/day advance and which reset only on map reload or interacting with an NPC? | Same save, clock held/advanced, with/without warp; trace `ClearDailyFlags` and resulting data. |
| **LIFE-05** | P1 | Does saving immediately before midnight then loading after midnight change story-relevant variables before map scripts run? | Compare RTC fixed versus day advance from identical pre-save; capture order of reset and read. |
| **LIFE-06** | P1 | How does clock rollback/penalty affect expected “new day” systems and checkpoint reproducibility? | Separate forward, backward and penalty states; record phone, Kurt, Safari, Pokéathlon, daily flags. |
| **LIFE-07** | P1 | Can a temporary scene stage be serialized at a legal save point but silently cleared on load? | Pick script with `VAR_TEMP_` stage, pause near legal save boundary, save/reload; verify entry path. |
| **LIFE-08** | P2 | Which saved bits are effectively runtime caches rather than authoritative game state? | Flip candidate on copy, load then resave without interaction; mark values reverted or recomputed. |

### C. Positions, warps, map objects and event entry points

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **MAP-01** | P0 | What is the minimum consistent set of fields required to place a player at a safe specific tile? | Compare organic saves with one-step movement, warp and flight; inspect current/entrance/previous/special/dynamic warp. |
| **MAP-02** | P0 | Does editing PKHeX `M/X/Y/X2/Y2/Z` alone produce the same reload as a real warp? | Clone a known-valid same-map and cross-map save; compare facing, camera, script trigger, follower and objects. |
| **MAP-03** | P0 | Which entrance or map-header scripts fire on reload, on a real warp and on a field transition? | Same map reached by three paths, with script log/instrumentation; compare flag writes. |
| **MAP-04** | P1 | Which NPC/object visibility changes persist only as flag bits, and which are stored in `SAVE_MAP_OBJECTS`? | Move/hide NPC through normal script, save/reload/warp; compare object list and flags. |
| **MAP-05** | P1 | How do facing, Z/elevation and warp indexes affect events that use movement or scripted camera setup? | Real pre-event saves on adjacent tiles/facing; run identical event; compare failures/cutscene. |
| **MAP-06** | P1 | Which areas recompute followers, weather or map objects on load and could break teleported fixtures? | Organic vs injected entry into caves, cities, scripted interiors, water; collect object/render logs. |
| **MAP-07** | P1 | Can map-specific progress survive exiting the area and returning if no explicit completion flag was set? | Pre/post local puzzle/NPC interactions, reload same map, leave/reenter, compare. |
| **MAP-08** | P2 | What happens for otherwise structurally valid out-of-bounds tiles, forbidden warps and unexpected map IDs? | Only disposable saves/emulator; classify blank map, fallback, crash, recovery; document rejected locations. |

### D. Johto storyline, badges and Kanto progression

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **STORY-01** | P0 | Exactly which fields change on starter choice, first trainer battle and Elm's early requests? | Capture before/after each discrete interaction; identify `VAR_PLAYER_STARTER`, party, Dex, flags and save notes. |
| **STORY-02** | P0 | What differentiates “Gym Leader defeated,” “badge awarded,” and “post-badge world state”? | Pre-battle/post-battle/post-dialogue saves when possible; inspect badge bytes, trainer flags, route blockers. |
| **STORY-03** | P0 | Which conditions unlock Goldenrod's Radio Tower takeover and determine its stage? | Organic saves at each stage; compare `VAR_SCENE_ROCKET_TAKEOVER`, visibility flags, phone behavior. |
| **STORY-04** | P0 | Which exact values of `VAR_SCENE_ROCKET_TAKEOVER` are reachable and what happens in stages 2, 3 and 4? | Capture stage transitions and reconstruct graph; source `Save_VarsFlags_IsInRocketTakeover` recognizes 2–4. |
| **STORY-05** | P0 | What post-Rocket conditions make the Kimono Girls' sequence available, and which persist after it? | Same version with missing/present prerequisites; Ecruteak Dance Theater and legendary location before/after. |
| **STORY-06** | P1 | Which saved fields distinguish defeating Elite Four, watching credits and the first postgame load? | Capture before final battle, after Hall of Fame/credits, first playable postgame; inspect `FLAG_GAME_CLEAR`, profile, timestamps. |
| **STORY-07** | P1 | What causes Kanto access, map unlock expansion, additional flypoints and postgame NPC movement? | Compare after ship access, arrival, map unlock and badge milestones; modify each candidate independently. |
| **STORY-08** | P1 | Which separate prerequisites actually unlock Mt. Silver and the Red encounter? | Collect stages across 16 badges, Oak, relevant flags and location scripts; test omit-one prerequisite. |
| **STORY-09** | P1 | What makes a story event replayable or permanently consumed after a defeat, blackout or escape? | Branch from identical checkpoint, change only battle outcome, save/reload/return and trace flags. |
| **STORY-10** | P2 | Which major scene variables have undocumented intermediate values and whether those values are ever saveable? | Scan every `VAR_SCENE_*` and related `VAR_UNK_*` comparison; enumerate observed values with source and saves. |

### E. Ho-Oh/Lugia and Kimono Girls — immediate Kohjoh regression target

Known, source-grounded candidate variables/flags include `FLAG_CAUGHT_LUGIA` (`0x117`), `FLAG_HIDE_WHIRL_ISLAND_LUGIA` (`0x243`), `FLAG_ENGAGING_STATIC_POKEMON` (`0xA4`), `FLAG_UNK_109` (`0x109`), `FLAG_UNK_10B` (`0x10B`), and `VAR_TEMP_x4003` / `VAR_TEMP_x400B`. **Do not rename `UNK_109` or `UNK_10B` based on this table alone.** The script uses them in multiple areas and branches.

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **LUG-01** | P0 | Which **version-specific** items, badges, flags and story stages permit the Lugia encounter and arrival cutscene? | Real HG and SS saves immediately before shrine/chamber; systematically withhold one prerequisite in copies. |
| **LUG-02** | P0 | What is the exact chronological persistent mutation sequence during the five-Kimono-Girls Lugia arrival script? | Script command trace + legal pre/post saves; mark each write's timing and whether it is preserved. |
| **LUG-03** | P0 | Does `FLAG_UNK_10B` denote completion of one animation phase, overall arrival, or a broader state? | Separate observed source writes and re-entry reads; toggle only after natural pre/post captures; test other maps. |
| **LUG-04** | P0 | What behavior actually depends on `FLAG_UNK_109` after the battle? | Compare genuine caught, defeated and interrupted/fled paths; test chamber and Ecruteak script branches. |
| **LUG-05** | P0 | How does `FLAG_HIDE_WHIRL_ISLAND_LUGIA` interact with `FLAG_CAUGHT_LUGIA` and the engaging-static bit? | Small factorial test (each on/off) on safe same-stage save; inspect sprite visibility, battle trigger, return. |
| **LUG-06** | P0 | Which temp variables are required for camera/animation synchronization, and can an ordinary save ever recreate the middle of this sequence? | Annotate script writes, use Lotus runtime checkpoint, attempt legal save only at allowed points; compare. |
| **LUG-07** | P0 | Which exact scripted command or rendering transition preceded the observed Kohjoh full-console crash? | Vanilla reference + Kohjoh/Lotus run with script PC, opcode, phase, texture/particle/model logs, frame hash if feasible. |
| **LUG-08** | P1 | Does the battle's catch/KO/run/blackout path write distinct values, and what determines later respawn behavior? | Controlled outcome branches from identical source save, returning after relevant milestones if required. |
| **LUG-09** | P1 | What differs between Ho-Oh/Bell Tower and Lugia/Whirl Islands across HG/SS, including alternate legendary encounters? | Parallel script CFGs and save deltas in both games; test alternate-game legendary access late in game. |
| **LUG-10** | P1 | Does entering the chamber via teleport versus the legitimate route omit camera, follower, object or story setup? | Identical intended chamber location, one organic and one synthetic entry; compare saved/runtime states. |
| **LUG-11** | P1 | Which Kimono Girl visibility flags are coupled to legendary arrival, post-battle cleanup and Dance Theater state? | Follow scene through reload/warp; compare `FLAG_HIDE_WHIRL_ISLANDS_BOTTOM_KIMONO_GIRLS` and related flags. |
| **LUG-12** | P2 | Which visual phases (Clear/Tidal Bell, shimmer, waves/leaves, eye glimmer, camera pan, entry) can be independently regression-tested? | Script-annotated input playback or savestates at phase boundaries; compare reference/hardware and Kohjoh. |

### F. Unknown flags and variables: actual semantic recovery

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **SYM-01** | P0 | Which `FLAG_UNK_*` are referenced in *reachable* story scripts, and which are written only by native helpers? | Generate complete use graph from script assembly and C/assembly; classify direct/indirect and branch reachability. |
| **SYM-02** | P0 | Which `VAR_UNK_*` are numeric stages, boolean sentinels, counters, object graphics IDs or cached results? | For each referenced ID, extract operations/comparisons/assignments; correlate with paired saves. |
| **SYM-03** | P0 | Can two unknown flags represent different phases of the same event rather than independent achievements? | Find co-writes/co-reads, then intervene separately before/after the event and compare. |
| **SYM-04** | P1 | Which unknown flags are never observed as nonzero in the full story corpus but are read in optional branches? | Static index + targeted save fixtures that force branch prerequisites; retain uncertainty if untested. |
| **SYM-05** | P1 | Which flags are only hiding map objects, and which also gate interactions or global progression? | Compare hide/unhide at two unrelated maps; run interaction scripts and check secondary reads. |
| **SYM-06** | P1 | Which unknown variables have only one reachable value, a bounded enum, or a valid range? | Enumerate constants and writes in CFG; record organic values and test boundary/out-of-range on copies. |
| **SYM-07** | P1 | Are any numeric flag/var IDs aliased, calculated, stored in tables or obtained through generic helper APIs? | Search machine-level references and dynamic script opcode parameters, not just preprocessor symbol names. |
| **SYM-08** | P1 | Which apparent changes are irrelevant side effects rather than necessary prerequisites? | One-factor revert from authentic post-event save; retest intended event and subsequent world stability. |
| **SYM-09** | P2 | Which unreferenced positions are genuinely unused in the studied retail ROM/version? | Exhaust direct, table, computed and runtime reference routes; document negative search scope, never absolute proof. |

### G. Pokémon, follower behavior, battles and party coherence

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **PKM-01** | P0 | What exact party/follower state is saved when a following Pokémon enters a scripted scene? | Organic saves before/after follower hide/show, party reorder and map warp; inspect party, object list, follower flags. |
| **PKM-02** | P0 | Does editing party slots alone recreate the same follower presentation as natural in-game party changes? | Matched party contents via game versus PKHeX.Core; compare behavior on first load and map transition. |
| **PKM-03** | P1 | Which party statuses or empty/egg/all-fainted combinations can make an otherwise reachable story checkpoint invalid? | Modify one party attribute from known valid save; classify menu, follower and forced-battle behavior. |
| **PKM-04** | P1 | What differs in save state between a static legendary battle and an ordinary wild encounter? | Before/after outcomes, separate event bits, Pokémon captured data, game records, Dex and roamer/static records. |
| **PKM-05** | P1 | Are battle outcome flags always set on successful capture, or only after post-battle script processing? | Runtime write trace around battle return; compare nearest legal post-battle save. |
| **PKM-06** | P2 | Which special forms/pokéball/walking-mood fields are normalized on boxing, unboxing, trading or reload? | Real transfers and edited copy round trips; track PK4 and non-PK4 state. |
| **PKM-07** | P2 | Which event scripts depend on exact party species, moves, happiness, gender or eggs in addition to flags? | Source cross-reference of Pokémon checks; matched party interventions; produce reusable predicates. |

### H. Inventory, key items, access gates and HM consequences

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **ITEM-01** | P0 | Which key-item gates check only item presence and which also check an associated story flag or stage? | Select known gates; give/remove relevant item on authentic saves with different progression. |
| **ITEM-02** | P0 | Does acquiring the same item through script and through save editing produce the same long-term game state? | Compare organic gift, edited bag and future dialogue/visibility/reload; identify event receipts. |
| **ITEM-03** | P1 | Which prerequisite differs between HG's Clear Bell/Rainbow Wing path and SS's Tidal Bell/Silver Wing path? | Parallel checkpoints and game-version branches; source `scr_seq_0884_T24R0801.s` as one observable gate. |
| **ITEM-04** | P1 | Does possession of HM, badge, learned move or helper flag actually determine access to a particular field action? | Omit each factor one at a time from a genuine accessible save; test Surf, Waterfall, Flash, etc. |
| **ITEM-05** | P1 | What save records besides the bag change when receiving Apricorn Balls, key items or gifts? | One organic item grant versus equivalent edited bag; compare counters, events, phone and inventory order. |
| **ITEM-06** | P2 | Are item pouches normalized, reordered or capped after the first game save, and can unusual but legal order affect scripts? | Insert known item with multiple layouts, reload and resave; verify lookup and quantity. |

### I. Time, phone, daily systems and other clocks

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **RTC-01** | P0 | Which saved fields change merely from advancing one in-game day with no player action? | Controlled one-day jump from single checkpoint; compare against fixed-clock save, repeat, trace `sub_02055508`. |
| **RTC-02** | P1 | Does a clock rollback trigger, skip or postpone any daily progression or rematch checks? | Compare forward/backward movements from identical save; record RTC penalty and later normal day. |
| **RTC-03** | P1 | How does Rocket takeover stage affect Buena's radio/phone calls independently of the local map? | Same checkpoint with alternate `VAR_SCENE_ROCKET_TAKEOVER` values; verify phone script branch and time. |
| **RTC-04** | P1 | What triggers a registered Pokégear caller's rematch availability and persistence? | Matched saves before/after call, relevant weekday/hour and defeat; compare phonebook and flags. |
| **RTC-05** | P1 | What triggers and consumes Apricorn/Kurt daily processing? | Before collection, after collection, after deposit, before/after next-day retrieval; controlled clock. |
| **RTC-06** | P1 | What is the saved versus recalculated part of Safari Zone day-based area unlocks? | Day-progress saves with no RTC manipulation, then controlled day jump; inspect Safari object unlock fields. |
| **RTC-07** | P1 | Which Bug-Catching Contest state is saved, runtime only or reset after elapsed time? | Capture eligible/entered/completed states and timer changes; test save/reload when legally allowed. |
| **RTC-08** | P2 | Which Pokémon/Pokéathlon/Pokéwalker timers and gifts are reset at midnight independently of daily script flags? | Compare one-day controls; record `PokeathlonSave_ResetDailyItemReceivedFlags` and related structures. |

### J. Roamers, Safari Zone, Pokéathlon and miscellaneous structures

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **SYS-01** | P1 | Which values in Raikou/Entei/Latias/Latios records represent active, encountered, defeated, captured and new location? | Organic and branch outcomes; cross-check radio/map tracker and roamer record. |
| **SYS-02** | P1 | Does changing a roamer flag without its dedicated record change behavior, and which field wins on reload? | Controlled one-field and paired-field edits; observe map tracker and encounter. |
| **SYS-03** | P1 | How is Safari Zone block placement represented, and which changes are saved versus derived from elapsed days? | Edit Safari in game with 1 action; save/diff/reload; compare `SafariZone4` and area layout. |
| **SYS-04** | P1 | Which Safari Zone unlock states can be injected independently of Baoba's narrative flags? | Real staged saves and omit-one changes; test usable Safari controls and progression. |
| **SYS-05** | P2 | Are Pokéathlon records relevant to any external NPC/menu unlock, or isolated to Pokéathlon? | Before/after medals/records, trace all reads and global flags; test crossing back to overworld. |
| **SYS-06** | P2 | Which Pokéwalker pairing/course/counter fields survive disconnect, resync and import, and which are platform-specific? | Only with owned hardware and consenting save; save before/after device sync; no fabricated pairing data. |
| **SYS-07** | P2 | Which Mystery Gift receipts are independent of item/flag possession and block gift re-delivery? | Authentic gift redemption before/after, receiving/item removal on copy, delivery-man and Wonder Card state. |
| **SYS-08** | P2 | Can game records or friend-group state alter event randomness independently of story flags? | Paired no-event and targeted action saves, controlled RNG/RTC, source inspection of consumers. |

### K. World coherence, reachability and post-event consequences

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **COH-01** | P0 | What is the **minimum** semantic edit set needed to convert a real checkpoint into a valid adjacent story stage? | Start from genuine earlier save; iteratively add only necessary edits; compare to organic target and future play. |
| **COH-02** | P0 | Are all minimum sets unique, or do multiple distinct valid configurations reach the same scene? | Two different organic histories and minimal edits; test same target script outcomes. |
| **COH-03** | P0 | Does a checkpoint that passes an immediate event still remain coherent through the next two major milestones? | Run following story chain organically from edited and real reference; compare events, flags, reachable maps. |
| **COH-04** | P1 | Which seemingly optional flags become mandatory only when returning to earlier locations? | After synthetic jump, revisit prior gym/town/story map, reload twice, note NPC and triggers. |
| **COH-05** | P1 | What happens if completion and visibility flags disagree for the same NPC/legendary? | Controlled crossed-bit matrix from consistent source; observe before/after reload and game self-correction. |
| **COH-06** | P1 | Can an event be triggered twice without corruption, and what is the exact lockout mechanism? | Genuine post-event save with one lockout reversed; note rewards, duplicate objects, crashes or safe rejection. |
| **COH-07** | P1 | Which saved fields must move together when changing game version or language? | Same-stage organic HG/SS saves; symbolic comparison, **never** blind version-byte mutation as conversion. |
| **COH-08** | P2 | How should truly unsupported/contradictory but checksum-valid states be classified by the CLI? | Curate counterexamples; assign `reject`, `warn`, `experimental`, `safe` based on demonstrated game behavior. |

### L. Measurement and source-tracing reliability

| ID | Priority | Research question | Decisive experiment / artifact |
|---|---|---|---|
| **TRACE-01** | P0 | How complete is the flag/variable reference index across `.s`, `.c`, tables, macros and indirect routines? | Compare static index against instrumented actual reads/writes on major story sequences; log misses. |
| **TRACE-02** | P0 | What percentage of observed pre/post semantic changes can be attributed to a specific source-side writer? | Join save diffs with traced program counters/opcodes; label unmapped changes and cause. |
| **TRACE-03** | P0 | Which emulator behavior diverges from hardware before any Kohjoh changes are considered? | Same genuine save and input trace on real NDS/3DS vs vanilla emulator; compare accessible outcomes. |
| **TRACE-04** | P1 | Do independent PKHeX.Core and original-game round trips agree on all tracked semantic fields? | Hash and compare before/after decoded JSON; isolate tool-induced effects and explain them. |
| **TRACE-05** | P1 | Which story branches are missing from the organic save corpus? | Compute reachability graph and observed-branch coverage; create prioritized capture queue. |
| **TRACE-06** | P2 | Which findings are region/language/version specific, and which appear universal? | Re-run highest-value tests across second game and available language cohorts. |

**Question count:** 8 FMT + 8 LIFE + 8 MAP + 10 STORY + 12 LUG + 9 SYM + 7 PKM + 6 ITEM + 8 RTC + 8 SYS + 8 COH + 6 TRACE = **98 falsifiable research questions**.

---

## 6. Deep-dive specimen: Silver/Tidal Bell and Lugia

Use **LUG-01 through LUG-07** to demonstrate the entire methodology before indexing every event.

### Source-grounded starting points

- `files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s`: Lugia chamber event, arrival effects, battle and aftermath. It contains `FLAG_UNK_109`, `FLAG_UNK_10B`, `FLAG_HIDE_WHIRL_ISLAND_LUGIA`, `FLAG_CAUGHT_LUGIA`, `VAR_TEMP_x4003` and `VAR_TEMP_x400B` references.
- `files/fielddata/script/scr_seq/scr_seq_0103_D40R0104.s`: earlier Whirl Islands NPC interactions; reads `FLAG_CAUGHT_LUGIA`, `FLAG_UNK_106`, `FLAG_UNK_109`, and `FLAG_UNK_105` in game-version branches.
- `files/fielddata/script/scr_seq/scr_seq_0928_T27R0501.s`: Ecruteak Dance Theater script; reads `FLAG_UNK_108`, `FLAG_UNK_109` and other conditions.
- `files/fielddata/script/scr_seq/scr_seq_0884_T24R0801.s`: an example of HG/SS Bell/Wing gate text; distinguishes `ITEM_CLEAR_BELL`/`ITEM_RAINBOW_WING` from `ITEM_TIDAL_BELL`/`ITEM_SILVER_WING` by game version.
- `include/constants/flags.h` and `include/constants/vars.h`: symbolic IDs. These are *inputs to the investigation*, not complete state graphs.

### Practical experiment series

**Experiment 1 — real natural branches.** Capture legal SS pre-arrival (`S0`), after arrival (`S1`), immediately following the Lugia battle (`S2`), after leaving and returning (`S3`), and after resaving/reloading (`S4`). Collect separate `S2` branches for catch/defeat/other outcomes where the game permits. Repeat equivalent phases in HG as a *separate* cohort because its legendary story order differs. For non-saveable cutscene boundaries, collect runtime traces, not fake `.sav` snapshots.

**Experiment 2 — authoritative semantic delta.** For each adjacent pair, record all 2,912 flags and 368 variables, party, key items, object visibility, map/warps, Dex, game records, game-clear/badge state and unknown regions. Also compare the no-op control. Ask whether `0x10B` is present before arrival, first appears during the arrival, persists to `S4`, and is ever cleared in related scripts. Do the same for `0x109` and `0x243`.

**Experiment 3 — one-factor causality.** From one authentic and coherent stage, create copies toggling only `0x109`, `0x10B`, `0x243`, or `0x117`. Recalculate checksums and round-trip the same edit through a no-change copy as control. Observe whether chamber objects appear, which dialogues fire, whether battle starts, whether a second entrance/warp changes state, and which bits self-correct on a normal resave. Use a small crossed-factor test only after single edits identify interactions; mark invalid combinations explicitly.

**Experiment 4 — crash localization.** Replay the real pre-arrival save on vanilla HGSS, Lotus and Kohjoh with the same button sequence. Trace source script command, in-game scene phase, resource loads and render/audio calls. Distinguish script dispatch bugs from rendering/particle bugs and save-state bugs. If a synthetic save crashes but the authentic pre-event save does not, the generator is suspect until proven otherwise.

**Acceptance:** A verified pre-Lugia `.sav` produces the correct scripted entry and post-event progression on vanilla reference; the same fixture and controlled inputs test Kohjoh; the meaning/limits of each implicated unknown flag are supported by specific scripts plus save/runtime evidence. No evidence is discarded when a hypothesis fails.

---

## 7. Automated source indexing and graph construction

**Input repos:** a pinned revision of `pret/pokeheartgold` and a pinned revision/package of `PKHeX.Core`. Also record the game ROM region/build used by the experiment. The decomp repository itself states that work is incomplete and symbols can change. Version the symbol database independently of its printed names.

### 7.1 Extract definitions and references

- Parse `include/constants/flags.h`, `include/constants/vars.h`, `include/constants/maps.h`, `include/constants/items.h` (and included definitions), recording canonical numeric ID, aliases, source path, line and commit.
- Preserve numeric IDs as identities; names are annotations and may change. Distinguish `FLAG_UNK_xxx` from known semantic names, but do not let names control the parser.
- Index `files/fielddata/script/scr_seq/*.s` and included script macros: direct flag reads/writes, `SetVar`/`AddVar`/`Compare`, `Call*`/`GoTo*`, battles, item grants, object visibility, warps, map transitions and data-dependent commands.
- Build per-script control-flow graphs with entry labels, branch conditions, call targets and sequence of side effects. Do not treat commands after a `GoTo` as unconditional; account for calls, returns and version branches.
- Join script banks to actual maps/events through the decomp's generated map events, headers and event mapping. Preserve uncertainty when mapping is incomplete.
- Index native C and assembly routines that call `Save_VarsFlags_*`, `GetScriptVar`, `SetScriptVar`, object/save accessors, RTC callbacks and battle-outcome handlers. Track **indirect** numeric reads and table-driven accesses separately from proven literals.
- For each field, generate a list of *read sites*, *write sites*, *reset sites*, *observed values*, and *reachable entry-point hypotheses*.

### 7.2 Minimal graph model

```text
GameVersion ─has→ Map ─has→ ScriptEntry
ScriptEntry ─guards_on→ Predicate(Flag/Var/Item/Badge/Time/Party/Map)
ScriptEntry ─calls→ ScriptEntry | NativeFunction
ScriptEntry ─writes→ FieldMutation
FieldMutation ─affects→ PersistentField | RuntimeState | SavedMapObject
ObservedRun ─starts_from→ SaveArtifact
ObservedRun ─traverses→ ScriptEntry
ObservedRun ─produces→ SaveArtifact
ObservedRun ─supports_or_refutes→ Hypothesis
```

A graph edge sourced solely from static search is **not** equivalent to observed execution. For each edge, store provenance and confidence.

### 7.3 Example machine-readable research record

```yaml
schema: hgss-research-question/v1
id: LUG-04
title: Meaning and consumers of FLAG_UNK_109
priority: P0
scope: [soulsilver, heartgold]
symbols:
  - {kind: flag, id: '0x109', source_name: FLAG_UNK_109}
hypotheses:
  - id: h1
    text: 'Flag changes observable post-encounter dialogue and/or replay gating'
    status: source-inferred
    falsifier: 'No dialogue/replay difference on matched one-factor tests and no executed read path'
source_evidence:
  - path: files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s
    revision: '<pinned-commit>'
  - path: files/fielddata/script/scr_seq/scr_seq_0103_D40R0104.s
    revision: '<pinned-commit>'
required_saves:
  - ss/whirl-before-event
  - ss/whirl-after-battle
  - ss/whirl-reentered
controls:
  - same-save-no-event-resave
  - same-save-no-op-pkhex-write
interventions:
  - from: ss/whirl-after-battle
    operation: {set_flag: {id: '0x109', value: false}}
observations:
  - 'NPC dialogue branch'
  - 'Lugia object visibility'
  - 'battle trigger availability'
  - 'state after warp and reload'
status: unexamined
```

A full observation row then attaches the exact source save SHA-256, output save SHA-256, run logs and clock details. Avoid stuffing per-run evidence into a manually edited symbol-definition file.

---

## 8. Corpus layout, artifacts and tooling

Suggested repository structure:

```text
hgss-state-research/
  README.md
  sources.lock.yaml               # pinned PKHeX/pokeheartgold/ROM revision metadata
  symbols/
    generated/flags.json
    generated/variables.json
    generated/maps.json
    curated/semantic-annotations.yaml
  graph/
    scripts.jsonl
    predicate-edges.jsonl
    field-accesses.jsonl
  corpus/
    manifest.yaml                 # raw bytes separately stored/read-only
    authentic/hg/...
    authentic/ss/...
    controls/...
    edited/...
  questions/
    FMT-01.yaml
    ...
    LUG-04.yaml
  runs/
    LUG-04/<run-id>/manifest.json
    LUG-04/<run-id>/save-diff.json
    LUG-04/<run-id>/trace.jsonl
    LUG-04/<run-id>/outcome.json
  fixtures/
    verified/hg/...
    verified/ss/...
    experimental/...
  reports/
    coverage.md
    unknowns.md
    divergences.md
  tools/
    extract-symbols/
    index-scripts/
    hgss-save/
    fixture-runner/
```

Where public distribution of raw saves is not authorized, use **private or local corpus storage**, and publish only manifests, hashes, semantic test specs and sanitized excerpts. Keep Pokémon/trainer identity and real MAC identifiers out of public diagnostic output by default. Do not distribute ROMs.

### Save manifest minimum

```yaml
id: ss/whirl-before-event
sha256: '<64 hex digits>'
provenance: 'captured from own cartridge; original file retained'
game: soulsilver
region: US
language: en
rom_hash: '<hash or private reference>'
toolchain:
  extractor: '<tool and version>'
  pkhex_core: '<pinned package or commit>'
  pokeheartgold: '<commit>'
capture:
  date_utc: '<timestamp>'
  rtc_local: '<DS/3DS clock>'
  platform: 'original-hardware'
  method: 'in-game-save'
  description: 'nearest save before intended Whirl Islands event'
state:
  map_id: '<decoded ID>'
  x: '<decoded X>'
  y: '<decoded Y>'
  story_stage: '<curated label or unknown>'
quality:
  parsed: true
  checksums_valid: true
  load_tested: true
  reference_behavior_verified: false
```

### Tooling priorities

- `capture-ingest`: verify input, checksum, block selection, file/container family, SHA-256; make originals immutable.
- `state-decode`: JSON snapshot of known fields and exhaustive flags/vars, with `unknown_bytes` region summaries.
- `state-diff`: semantic delta, control subtraction and raw unexplained-range report; exports Markdown/JSON.
- `symbol-index`: gather definition plus use/writer sites, with comments and source links; support renames without losing historical identity.
- `script-graph`: parse script opcodes/control flow, bind map entry points, assign uncertainty to indirect or unresolved code.
- `patch`: edit only declared fields on copies, using PKHeX.Core methods; enforce preconditions including expected original values and expected source hash.
- `runtime-runner`: orchestrate Lotus/emulator and input replay; collect script PC/read-write traces and observed outcomes. A hardware test may be manually recorded where API instrumentation is unavailable.
- `report`: merge evidence into question statuses; report the unmapped fraction and all contradictions.

Recommended CLI interfaces use JSON on stdout, diagnostics on stderr, stable exit codes and deterministic output. Agents should query `symbol explain`, `source references`, `question open`, and `run compare` instead of guessing hex addresses.

---

## 9. Causal experiment templates

### 9.1 One-field ablation (minimum requirement)

```
A: authentic S0 -> unchanged PKHeX re-export -> load -> observe
B: authentic S0 -> change one candidate flag -> load -> observe
C: authentic S0 -> no change -> original game resave -> load -> observe
D: authentic S0 -> change candidate flag -> normal in-game save -> load -> observe
```

A/B isolates candidate effect from serializer. A/C isolates original-game housekeeping. B/D tests persistence and normalization. Keep runtime/game version/clock matched.

### 9.2 Compound predicates

If the source reads `flag A && var B >= 3 && item C`:

- Construct the full natural state `A=1, B=3, C=1` from an authentic checkpoint.
- Remove exactly one condition at a time (three negative controls).
- Test boundary `B=2` versus `B=3`; confirm whether `B=4` takes a different branch.
- Observe both entry behavior and future playability; record which conditions are *necessary* versus merely correlated.
- Do not conclude A, B and C are *sufficient* until the event works from the proposed fixture with all other assumed context documented.

### 9.3 State minimization and counterfactuals

Start from a genuine post-event state. Revert candidate fields to their pre-event values one at a time, optionally binary-searching groups as a hypothesis generator. Always repeat single-field interventions after a multi-field screen, since fields can compensate for each other. Stop “minimizing” when a state becomes behaviorally invalid, not only when the immediate scene stops loading.

### 9.4 Failure-class taxonomy

| Failure | Interpretation | Next action |
|---|---|---|
| Input file fails CRC | Format/serializer failure | Repair generator, not story hypotheses |
| Loads but player spawns incorrectly | Location/warp/context mismatch | Compare organic map-entry state |
| Event absent | Missing precondition or wrong game/version | Trace predicate reads before event |
| Event starts then deadlocks | Runtime scene state, script, camera, follower or missing effect | Inspect script PC, object state and runtime logs |
| Event finishes but later map breaks | Incomplete persistent side effects | Run next-two-milestones coherence test |
| Only edited fixture crashes | Invalid synthetic state until disproven | Compare with authentic reference fixture |
| Vanilla works, Kohjoh fails | Port regression candidate | Same save/input, isolate subsystem |
| Emulator differs from hardware | Reference/model fidelity issue | Hardware corroboration and separate emulator issue |
| No semantic difference, raw bytes differ | Save metadata, encrypted counters, noise or unmapped field | Narrow raw ranges and repeat controls |

---

## 10. Coverage metrics and research triage

Track these over a pinned source revision:

1. **Defined positions:** number of persistent flags/variables with a source label.
2. **Source-reference coverage:** positions with direct read/write/reset sites, computed accesses or indirect-only evidence; unknown is not unreferenced.
3. **Semantic coverage:** positions with accepted meaning, valid-value domain and lifecycle, not merely a renamed symbol.
4. **Transition coverage:** named story events with genuine pre/post saves and source graph paths.
5. **Causal coverage:** transitions where one-factor interventions confirm their gating or effect.
6. **Lifecycle coverage:** transitions checked through warp, game resave, reload and clock changes.
7. **Version coverage:** number of events replicated in HG and SS.
8. **Kohjoh regression coverage:** fixture passes vanilla reference and automatically exercises Kohjoh; distinguish rendering/script failures.
9. **Unexplained state delta:** bytes changed in event-specific contrasts that remain unattributed after control subtraction, grouped by save region.
10. **Contradictions:** hypotheses with failing or conflicting evidence; keep them visible rather than silently accepting a new name.

Recommended prioritization for unknowns: `priority = story_relevance × reachability × failure_impact × source_uncertainty`, with an adjustment for whether a real pre/post save can be captured. Do not prioritize by raw `UNK` count alone. An unknown flag in the core Lugia sequence is higher priority than a likely-unused late-region flag.

### Suggested initial queue (order matters)

| Wave | Tasks | Exit condition |
|---|---|---|
| 0 — correctness | FMT-01–05, LIFE-01–03, TRACE-01/04 | Confident that save differences are real, not parser artifacts |
| 1 — immediate crash | LUG-01–07, MAP-02/03/04, COH-01 | Authentic playable pre-Lugia fixture, script-phase localization and candidate-flag semantics |
| 2 — story backbone | STORY-01–08, SYM-01–03, ITEM-01–04 | Dependency graph sufficient for core Johto progression fixtures |
| 3 — robustness | STORY-09/10, COH-02–07, RTC-01–04, PKM-01–05 | Multi-milestone tests show no silent broken-state jumps |
| 4 — long tail | Remaining RTC/SYS/SYM/FMT questions | Optional mechanisms covered with explicit uncertainty bounds |

Avoid an infinite “research all flags” project. Ship a verified slice—e.g., all Lugia preconditions and aftermath—as a usable schema and regression suite before expanding to the next quest chain.

---

## 11. Definition of done

A **question** is resolved when its answer includes a falsifiable statement, exact relevant symbols and versions, source references, evidence from at least one authentic event transition (or a documented reason none exists), appropriate control(s), intervention results where causality is claimed, an account of reload/warp behavior when relevant, and remaining uncertainties.

A **story fixture** is verified when it originates from a known authentic base, its patch is deterministic and reviewed, its semantics validate after serialization, it loads on a vanilla reference, the targeted script runs as intended, two subsequent milestones remain coherent, and the test is repeatable in Lotus/Kohjoh. Hardware confirmation is recorded separately; it is not claimed when only emulator evidence exists.

The project succeeds when an agent can answer: **“What exact real-save evidence supports the flags, variables, objects, items and runtime assumptions for this event?”**—and can generate a checkpoint without inventing an unsupported world state.

---

## 12. Verified upstream research entry points

The following files were checked against the current default-branch sources during preparation. Pin commits in `sources.lock.yaml` before running an experiment; filenames, symbol names and decomp coverage evolve.

**PKHeX:**
- [`SAV4HGSS.cs`](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4HGSS.cs): HGSS block sizes/offsets, event region, inventory, map, PC changed-bitmask and game-specific substructures.
- [`SAV4.cs`](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4.cs): common block resolution, checksums, flags/work, party, mystery gifts and records interface.
- [`SAV4BlockDetection.cs`](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/SAV4BlockDetection.cs): active main/aux block selection.
- [`Record4.cs`](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/Record4.cs): game-record crypto and layout.
- [`SafariZone4.cs`](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/Safari/SafariZone4.cs): Safari Zone flags/progress, separate from ordinary script flags.

**pokeheartgold:**
- [`README.md`](https://github.com/pret/pokeheartgold): explicitly identifies the project as an incomplete, unstable decompilation.
- [`include/constants/flags.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/flags.h), [`include/constants/vars.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/vars.h), [`include/constants/maps.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/maps.h), [`include/constants/items.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/items.h): symbolic definitions, aliases and numerical IDs.
- [`src/save_vars_flags.c`](https://github.com/pret/pokeheartgold/blob/master/src/save_vars_flags.c), [`include/save_vars_flags.h`](https://github.com/pret/pokeheartgold/blob/master/include/save_vars_flags.h): flag/variable storage and temp-flag handling.
- [`src/script_manager.c`](https://github.com/pret/pokeheartgold/blob/master/src/script_manager.c): clears map-temporary flags/variables and daily flags.
- [`src/sys_vars.c`](https://github.com/pret/pokeheartgold/blob/master/src/sys_vars.c): example native consumer of a story-stage variable (`VAR_SCENE_ROCKET_TAKEOVER`).
- [`src/unk_02055418.c`](https://github.com/pret/pokeheartgold/blob/master/src/unk_02055418.c): clock changes, daily updates, phone, roamers, Apricorn, Safari and Pokéathlon reset paths.
- [`src/save_local_field_data.c`](https://github.com/pret/pokeheartgold/blob/master/src/save_local_field_data.c): current/entrance/previous/dynamic warp, saved map objects and restoration.
- [`src/save.c`](https://github.com/pret/pokeheartgold/blob/master/src/save.c): original-game save/write/recovery implementation and PC box bookkeeping.
- [`files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s`](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s): Lugia arrival and encounter.
- [`files/fielddata/script/scr_seq/scr_seq_0103_D40R0104.s`](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0103_D40R0104.s): Whirl Islands companion dialogue conditions.
- [`files/fielddata/script/scr_seq/scr_seq_0928_T27R0501.s`](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0928_T27R0501.s): Ecruteak Dance Theater conditions.
- [`files/fielddata/script/scr_seq/scr_seq_0884_T24R0801.s`](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0884_T24R0801.s): version-specific Bell/Wing checks in a field script.

*No save corpus was supplied for this document, and no live-game experiments were performed. All experiment questions, observed outcomes and acceptance criteria are proposed work; the source-based facts above are separate from those hypotheses.*
