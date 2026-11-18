# HGSS Save CLI — Engineering Design

**Purpose:** Build a headless, deterministic save-inspection and save-generation tool for Pokémon HeartGold/SoulSilver, primarily for Kohjoh regression tests and agent-driven debugging.

**Status:** Implementation specification, not a claim that the described tool has already been built or tested.

**Research baseline:** 16 November 2026. PKHeX.Core **26.10.18** (NuGet, .NET 10), with the current PKHeX source APIs cross-checked against `SAV4HGSS`, `SAV4`, and `SaveFile`. The `pret/pokeheartgold` repository is an **unstable WIP disassembly**; pin an exact source commit before generating symbols. Source links and caveats are at the end.

---

## 1. Decision and scope

Build a small **C#/.NET 10 CLI** whose binary read/write layer is **PKHeX.Core**, with a separate **source-indexing pipeline** that extracts symbolic names and script references from `pret/pokeheartgold`. The output is a raw 512 KiB HGSS save (`.sav`), plus optional machine-readable reports. An MCP server or Lotus integration invokes this same engine rather than reimplementing the format.

Do **not** make agents patch hexadecimal offsets in routine use. Raw offsets are still useful for investigation, but they are a privileged, explicitly requested escape hatch. Do **not** automate the PKHeX Windows Forms application: it is an unnecessary UI dependency for a headless workflow.

### Goals

1. Inspect an existing raw HGSS save and emit stable JSON: game, trainer, location, party, inventory, flags, variables, block validity and provenance.
2. Apply several typed edits **as one transaction**, with expected-original-state checks, source-save preservation, checksum repair, validation and an understandable diff.
3. Resolve `FLAG_*`, `VAR_*`, `MAP_*`, `ITEM_*` and other names against a **pinned** `pokeheartgold` revision; distinguish resolved names from aliases and unknowns.
4. Build reproducible named fixtures for major story and edge-case tests, while explicitly identifying assumptions that cannot be validated from the save alone.
5. Provide a predictable tool surface to coding agents, Lotus, and CI. One engine, one schema, no duplicate save-editing implementations.
6. Preserve the original save format and unrelated data wherever practical. Every output must be traceable to the base save and the exact requested mutation set.

### Non-goals for the first version

- Inventing valid story states automatically by setting every flag associated with a location.
- Emulating running script contexts or mid-battle state. Use Lotus savestates and/or script instrumentation for those.
- Supporting every DS-emulator proprietary container format. Start with **exactly `0x80000`-byte raw HGSS saves** and add adapters explicitly.
- Fully synthesizing legal PK4 data. Start with known-good PK4 templates and expose PKHeX functionality as needed.
- Reimplementing Gen IV checksums, box handling or Pokémon encryption in a second production save writer.

The most important distinction is **structural validity** versus **semantic validity**. PKHeX.Core can serialize a syntactically valid save; it cannot infer whether the correct NPC, warp, map object, cutscene condition and script variable combinations exist for a given story event.

---

## 2. Dependencies and source-of-truth boundaries

| Responsibility | Source of truth | Notes |
| --- | --- | --- |
| Binary parsing, redundant blocks, checksums, storage | `PKHeX.Core` | Use its `SAV4HGSS` implementation, not hard-coded raw offsets in agents. |
| Trainer, party, boxes, bag, Pokédex and most other save structures | `PKHeX.Core` | Prefer public typed APIs. For unavailable fields, add a narrowly scoped HGSS adapter with tests. |
| Symbolic flag/variable/map/item IDs | `pret/pokeheartgold` headers | Names are decompilation identifiers, not an immutable official API. |
| Which conditions scripts check/set | `pret/pokeheartgold` field scripts and code | Partial/unstable; static analysis is evidence, not an oracle. |
| Known safe spawns and whole-world progression | Curated test fixtures, successful in-game saves and verified script studies | Store provenance and confidence with each fixture. |
| Whether the target port behaves correctly | Kohjoh in Lotus; O3DS hardware for selected tests | Emulator success alone does not prove hardware correctness. |

**Upstream versions.** NuGet currently lists `PKHeX.Core 26.10.18`, targeting `net10.0`. Commit-pinned `pokeheartgold` source should be stored in the fixture metadata, not automatically updated when building a fixture. A later version of either upstream must trigger compatibility testing before adoption.

**Licensing.** The NuGet package declares **GPL-3.0-or-later**. Treat a distributed CLI linking PKHeX.Core as GPL-covered and review redistribution obligations. The decompilation does not have a clearly stated repository-wide software license in the materials checked: don't assume its files can simply be vendored and redistributed. Prefer storing extracted identifiers/provenance and linking to the source. Do not bundle copyrighted game ROMs or user saves into public releases.

---

## 3. Architecture

```text
                   ┌──────────────────────────────────────┐
                   │ Agents / shell / CI / Lotus MCP       │
                   └─────────────────┬────────────────────┘
                                     │ commands or JSON requests
                                     ▼
                   ┌──────────────────────────────────────┐
                   │ hgss-save CLI                         │
                   │ input/command/schema/error handling   │
                   └─────────────────┬────────────────────┘
                                     ▼
                   ┌──────────────────────────────────────┐
                   │ Operations + fixture compiler         │
                   │ preconditions, planning, postconditions│
                   └────────────┬───────────┬─────────────┘
                                │           │
                    names/IDs   ▼           ▼ save operations
                   ┌────────────────┐ ┌────────────────────┐
                   │ Symbol registry│ │ HGSS adapter       │
                   │ scripts/claims │ │ PKHeX.Core         │
                   └───────▲────────┘ └──────────┬─────────┘
                           │                     │
                   ┌───────┴────────┐ ┌──────────▼─────────┐
                   │ pokeheartgold  │ │ input .sav         │
                   │ offline indexer│ │ output .sav        │
                   └────────────────┘ └────────────────────┘
```

The **source indexer is a build-time/offline tool**; the save editor doesn't need a checked-out decompilation to run. Compile the registry to a versioned JSON artifact checked in alongside the CLI, with its source commit recorded.

### Suggested repository layout

```text
hgss-save/
  README.md
  LICENSE
  global.json
  Directory.Packages.props
  src/
    HgssSave.Cli/            # console entry point; argument parsing; JSON I/O
    HgssSave.Core/           # application services, operation model, policy
    HgssSave.PKHeX/          # only package that knows PKHeX.Core types
    HgssSave.Symbols/        # name resolution, metadata, provenance
    HgssSave.SourceIndex/    # offline pokeheartgold index generator
  schemas/
    patch.v1.schema.json
    fixture.v1.schema.json
    report.v1.schema.json
    symbols.v1.schema.json
  registry/
    hgss-us.symbols.json
    source-manifest.json
  fixtures/
    recipes/                # human-readable inputs; no copyrighted save blobs
    checkpoints/            # local/gitignored base saves
    manifests/              # SHA-256 and fixture provenance
  tests/
    HgssSave.UnitTests/
    HgssSave.IntegrationTests/
    HgssSave.GoldenTests/
    fixtures-private/        # ignored
  docs/
    format-assumptions.md
    validation-policy.md
    symbol-generation.md
    story-fixture-guidelines.md
```

You can simplify the first iteration to a CLI project and a core project, then extract the indexer after the first round-trip is working. The architectural boundary is more important than the number of projects.

### The central design rule

Only `HgssSave.PKHeX` may directly touch `SAV4HGSS`, `Span<byte>` or physical save offsets. The public interface speaks domain objects and operations (`SetFlag`, `SetVariable`, `SetLocation`, `SetMoney`, `SetBagQuantity`, etc.). No agent-facing command should need to know that the flag array starts at `0x10C4`.

---

## 4. Command-line contract

Commands should be composable, have stable exit codes, and emit strict JSON on stdout when `--json` is specified. Diagnostics and progress go to stderr, never interleaved with JSON.

```text
hgss-save version --json
hgss-save inspect INPUT.sav --json
hgss-save validate INPUT.sav --level structural --json
hgss-save symbols lookup FLAG_CAUGHT_LUGIA --json
hgss-save symbols search lugia --kind flag,var,map,item --json
hgss-save script references FLAG_CAUGHT_LUGIA --json
hgss-save diff BEFORE.sav AFTER.sav --json
hgss-save patch INPUT.sav --recipe patch.json --out OUTPUT.sav --dry-run --json
hgss-save patch INPUT.sav --recipe patch.json --out OUTPUT.sav --json
hgss-save fixture build fixtures/recipes/whirl-islands-lugia.yaml --out OUTDIR --json
hgss-save fixture verify OUTDIR/manifest.json --json
```

Optional ergonomic single-field verbs (`flag get`, `flag set`, `var set`) can arrive later; implement them internally by constructing the **same patch transaction**. There should be exactly one mutation engine.

### Exit codes (proposed)

| Code | Meaning |
| --- | --- |
| `0` | Success, including a clean validation or no-op diff. |
| `2` | Bad arguments or invalid JSON/schema. |
| `3` | Unsupported save size/format/version. |
| `4` | Save structural verification failed. |
| `5` | Symbol name unknown or ambiguous. |
| `6` | A precondition failed or requested value is out of range. |
| `7` | Output-file conflict / I/O error. |
| `8` | Unsupported or unsafe semantic operation. |
| `9` | Internal error (include a diagnostic ID). |

Never use a successful exit status for a partially applied patch. `--continue-on-error` should not exist for save edits.

### Output/report shape

```json
{
  "schema": "hgss-save.report/v1",
  "operation": "inspect",
  "input": {
    "path": "heartgold.sav",
    "sha256": "<actual-computed-sha256>",
    "bytes": 524288
  },
  "format": { "family": "SAV4HGSS", "game": "HG", "region": "unverified" },
  "sources": {
    "pkhex_core_version": "26.10.18",
    "pokeheartgold_commit": "<pinned-commit>"
  },
  "validation": { "structural": "pass", "semantic": "not-evaluated" },
  "trainer": { "money": 1500 },
  "location": { "map_id": 76, "map_symbol": "MAP_GOLDENROD", "x": 0, "y": 0 },
  "warnings": []
}
```

Values above are **illustrative**; don't present them as observations from a real save. Report separately what was read from the save versus what was inferred from symbols.

---

## 5. Versioned patch format

Prefer typed `operations` with explicit expectations. Avoid mutable, ambiguous dictionaries where `null` might mean either delete or leave unchanged.

```json
{
  "schema": "hgss-save.patch/v1",
  "game": "SS",
  "base_sha256": "<actual-base-save-sha256>",
  "symbol_registry": "hgss-us@<pinned-commit>",
  "operations": [
    {
      "op": "flag.set",
      "id": "FLAG_CAUGHT_LUGIA",
      "value": false,
      "expect": false
    },
    {
      "op": "variable.set",
      "id": "VAR_PLAYER_STARTER",
      "value": 1
    },
    {
      "op": "trainer.money.set",
      "value": 50000
    },
    {
      "op": "inventory.quantity.set",
      "item": "ITEM_SILVER_WING",
      "quantity": 1
    }
  ],
  "assert": [
    { "field": "trainer.money", "equals": 50000 },
    { "field": "flag.FLAG_CAUGHT_LUGIA", "equals": false }
  ]
}
```

A real fixture must use an actual base-save SHA-256. The example is **not** a complete Lugia story-state preset. The `VAR_PLAYER_STARTER` value is illustrative and should not be changed casually: it has real game semantics.

### Design choices

- **Default operations are idempotent:** `quantity.set` instead of `give/add`, `flag.set` instead of `toggle`. Permit increment/append operations only with an explicit expected starting value.
- **All field names are typed:** `FLAG_*` must resolve to a persistent flag; `VAR_*` must resolve to a save-backed variable; `MAP_*` must resolve to a map. A `SPECIAL_VAR_*`/runtime-only value cannot be written to `.sav` as though it were persistent.
- **Numeric IDs are allowed**, but marked in the plan as `numeric/unreviewed` unless a matching symbol is found. A stricter fixture policy can reject them.
- **Preconditions operate before mutation.** Evaluate against the original parsed save, not against a save partially modified by prior operations. Reject conflicting writes to the same field unless explicitly and safely composed.
- **Postconditions** re-evaluate after serialization and reparsing.
- **Atomicity:** no result is written if any schema, reference, value, precondition or postcondition fails.
- **Default output is a new path**, never the source save. Require `--in-place --backup` for overwriting, and prohibit in-place editing in the MCP layer.
- **No shell or source-code execution** in patch expressions. This is a data format; no `eval`, arbitrary reflection, dynamic expressions or plugins requested by the patch file.

For a large regression corpus, this gives agents deterministic behavior and clear evidence when an edit is unsupported.

---

## 6. PKHeX.Core adapter: implementation

### Build baseline

```bash
dotnet new sln -n HgssSave
dotnet new classlib -n HgssSave.PKHeX -f net10.0
dotnet new console -n HgssSave.Cli -f net10.0
dotnet add HgssSave.PKHeX/HgssSave.PKHeX.csproj package PKHeX.Core --version 26.10.18
dotnet add HgssSave.Cli/HgssSave.Cli.csproj reference HgssSave.PKHeX/HgssSave.PKHeX.csproj
```

This is a build skeleton; add the other projects and test references as the boundaries settle. Commit the lockfile (or use Central Package Management with a pinned version), and use `global.json` to pin the .NET SDK version used in CI.

### Confirmed public APIs

`SAV4HGSS` subclasses `SAV4`, which subclasses `SaveFile`. Current upstream provides:

| Operation | Relevant public API |
| --- | --- |
| Load raw bytes | `new SAV4HGSS(Memory<byte>)` |
| Export finalized bytes | `sav.Write().ToArray()` |
| Verify checksums | `sav.ChecksumsValid`, `sav.ChecksumInfo` |
| Game version | `sav.Version` (`GameVersion.HG`/`SS`) |
| Trainer | `sav.OT`, `sav.TID16`, `sav.SID16`, `sav.Money`, `sav.Badges`, `sav.Badges16` |
| Location | `sav.M`, `sav.X`, `sav.Y`, `sav.X2`, `sav.Y2`, `sav.Z` |
| Flag | `sav.GetEventFlag(index)`, `sav.SetEventFlag(index, bool)` |
| Script variable | `sav.GetWork(index)`, `sav.SetWork(index, ushort)` |
| Party | `sav.PartyData`, `sav.GetPartySlotAtIndex()`, `sav.SetPartySlotAtIndex()` |
| PC boxes | `sav.GetBoxSlotAtIndex()`, `sav.SetBoxSlotAtIndex()` |
| Inventory | `sav.Inventory` (`PlayerBag4HGSS`), then `CopyTo(sav)` after mutation |
| Pokédex | `sav.Dex` / `sav.GetSeen()`, `sav.GetCaught()` |

These methods are verified in the linked current PKHeX source. Compile against the chosen NuGet package and treat any API drift as a build failure; the published package and the latest master branch need not be identical.

### Minimal working adapter pattern

```csharp
using PKHeX.Core;

public static class HgssIO
{
    public static SAV4HGSS LoadStrict(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length != 0x80000)
            throw new InvalidDataException("Expected 524288-byte raw HGSS save.");

        var sav = new SAV4HGSS(raw);
        if (sav.Version is not (GameVersion.HG or GameVersion.SS))
            throw new InvalidDataException("Not an HGSS save.");
        if (!sav.ChecksumsValid)
            throw new InvalidDataException(sav.ChecksumInfo);
        return sav;
    }

    public static void SetPersistentFlag(SAV4HGSS sav, int flagId, bool value)
    {
        if ((uint)flagId >= (uint)sav.EventFlagCount)
            throw new ArgumentOutOfRangeException(nameof(flagId));
        sav.SetEventFlag(flagId, value);
    }

    public static void SetSaveVariable(SAV4HGSS sav, int varId, ushort value)
    {
        // pokeheartgold names save-work variables starting at VAR_BASE 0x4000.
        int index = varId - 0x4000;
        if ((uint)index >= (uint)sav.EventWorkCount)
            throw new ArgumentOutOfRangeException(nameof(varId));
        sav.SetWork(index, value);
    }

    public static byte[] SerializeAndCheck(SAV4HGSS sav)
    {
        byte[] bytes = sav.Write().ToArray();
        var parsed = new SAV4HGSS(bytes);
        if (!parsed.ChecksumsValid)
            throw new InvalidDataException(parsed.ChecksumInfo);
        return bytes;
    }
}
```

This demonstrates the integration boundary, not the complete production CLI. Wrap errors in typed application errors; validate source hashes, transactional postconditions and output paths separately.

**Crucial indexing detail:** `SetWork` takes a **zero-based storage index**, not the decompilation's variable ID. For `VAR_PLAYER_STARTER = 0x4030`, the index is `0x30`. `SetEventFlag` takes the **flag number** directly. Do not apply `-0x4000` to flags.

### Save-block behavior and safeguards

HGSS raw saves have two `0x40000`-byte partitions. The active **general** block and **PC storage** block can come from different partitions. `SAV4` chooses them independently using revision counters. Don't select one half and patch both structures yourself.

`SaveFile.Write()` calls finalization and checksum updates. `SAV4HGSS.GetFinalData()` additionally sets all 18 changed-box bits (`0x3FFFF`) to avoid box edits being dropped on the next in-game save. A zero-edit call to `Write()` can therefore change bookkeeping bytes; **do not promise byte-identical no-op exports**. If a patch makes no logical changes, either explicitly return the original bytes or document that export normalization occurred.

PKHeX validates checksums, but `SAV4` chooses active main blocks by comparing their counters rather than first filtering every candidate by CRC. Add a **preflight scanner** that records the integrity and revision of both general/storage copies before constructing an editable session. For v1, reject damaged/ambiguous files rather than silently repairing or choosing a possibly invalid copy. Recovery can be a separate command with explicit backup-selection evidence.

For raw-save writes:

1. Validate exact size, game identity and source SHA-256.
2. Snapshot original bytes and logical values.
3. Resolve all symbol IDs, run static policy and preconditions.
4. Apply requested edits to one in-memory `SAV4HGSS`.
5. Serialize via `Write()`; reopen and confirm checksums and postconditions.
6. Run byte-diff auditing: expected domain regions plus documented metadata/checksum writes only. Unknown modifications are errors in strict mode.
7. Write a sibling temporary file and rename into a **new** output path after successful validation. Refuse collisions unless explicitly allowed. Write a manifest and JSON diff with the result.

Never quietly strip `.dsv`, emulator metadata, or appended proprietary formats; implement recognizable input adapters only after unit tests exist.

### Inventory: don't forget `CopyTo`

`SAV4HGSS.Inventory` constructs a `PlayerBag4HGSS` from the saved bytes. Its pouches are **loaded objects**; mutating the returned items without copying the bag back won't necessarily update the save.

Implementation strategy:

```csharp
var bag = sav.Inventory;
// Resolve item ID and desired pouch using ItemStorage4HGSS.
// Modify the matching InventoryPouch4.Items[] entry,
// or use the pouch's GiveItem(...) when the semantics match.
bag.CopyTo(sav); // essential after editing the bag object
```

For `quantity.set`, find the correct pocket, replace existing quantity or occupy the first free slot; reject wrong-pocket, no-space, invalid count or conflicting duplicate entries. Prefer explicit idempotent quantity semantics rather than the additive `GiveItem` method. Clamp only when the recipe specifically opts into clamping; default is an error, not silent data loss.

### Party and PC editing

Use PKHeX's `PK4` and save-slot APIs. **Phase 1** should support inspect/diff only, while **Phase 2** adds importing a known-good `.pk4`/`PK4` template and setting a party/box slot. Verify template format, encryption/decryption expectations, species, level, moves, trainer details, party extension and checksum after exporting. Avoid constructing an arbitrary Pokémon solely from a species ID; Game Freak's and PKHeX's validity rules are more involved than that.

---

## 7. Source-indexing `pokeheartgold`

This is not a runtime dependency on `pokeheartgold`. Write one offline tool that transforms a **pinned checkout** into a versioned symbol registry and evidence index.

### Candidate inputs

| Data | Relevant path |
| --- | --- |
| Persistent/temporary flag names | `include/constants/flags.h` |
| Script variable names and ranges | `include/constants/vars.h` |
| Map identifiers | `include/constants/maps.h` |
| Items, species, moves, trainers | `include/constants/items.h`, `species.h`, `moves.h`, `trainers.h` |
| Save variable/flag access semantics | `include/save_vars_flags.h`, `src/save_vars_flags.c` |
| Saved location/map-object structures | `src/save_local_field_data.c`, corresponding headers |
| Script sequences | `files/fielddata/script/scr_seq/*.s` |
| Per-script/event aliases | Headers included from `fielddata/script/scr_seq/event_*.h` |
| Broader event logic | `src/*.c` and relevant assembly/overlays |

**Critical distinction:** the decompilation includes map-temporary flags, ordinary save flags and special/runtime variables. Don't expose them all as writable `.sav` fields. Build a `storage_kind` classification (`persistent_flag`, `temporary_flag`, `save_work`, `special_runtime_var`, `alias`, `unknown`) and reject nonpersistent edits.

### Reliable numeric extraction

Don't rely on a regex that assumes every `#define` contains a literal. Headers include aliases and expressions. Two workable approaches:

1. **Preferred:** enumerate macro *names* by scanning the pinned headers, generate a tiny C source that `#include`s those headers and prints their numeric values, then compile/run it using the project's supported C toolchain. Resolve conditional compilation using the real include paths. The generated program references only known symbol names; it does not execute arbitrary user patch expressions. Build this in the **trusted source indexing environment**.
2. **Fallback:** run `clang -E -dM` with the correct include paths, then evaluate only a restricted subset of integer macro expressions (numeric literals, parentheses, known aliases, arithmetic and bitwise operators). Report unresolved macros instead of making up values. **No general `eval`.**

Guard against same-name/different-value collisions, different-name/same-value aliases, conditional definitions, and codes that overlap categories. Use deterministic output ordering. Keep source-file/line or commit/path evidence for every entry.

Example generated entry:

```json
{
  "name": "FLAG_CAUGHT_LUGIA",
  "kind": "persistent_flag",
  "id": 279,
  "hex": "0x117",
  "source": "include/constants/flags.h",
  "revision": "<pinned-pokeheartgold-commit>",
  "aliases": [],
  "confidence": "source-defined"
}
```

The flag number `0x117` is verified in the current header. The source revision must be filled with the **actual pinned commit** during generation. Do not claim that a symbol implies a complete story-state recipe.

### Script indexing: read references, do not claim full semantic comprehension

Make a second pass over the field scripts and build a database of symbol references and command sequences. Initially record:

- Definitions (`ScrDef` entry points, script name, source file and label).
- Reads: `GoToIfSet` / `GoToIfUnset`, `CheckFlag`, `Compare` and `HasItem` where recognizable.
- Writes: `SetFlag`, `ClearFlag`, `SetVar`, `GiveItem*`, `TakeItem*` and other known commands.
- Branches, calls and returns; `WildBattle`, trainer battles, warps, cutscenes and animations as **behavior markers**.
- Which commands the indexer doesn't recognize, with source location, instead of silently ignoring them.

A graph with condition edges is useful, but script commands can call native routines, set flags indirectly, and depend on state outside the `.sav`; a simplistic linear scan will misinterpret conditional writes as unconditional. **Every derived precondition needs a confidence level** (`direct-read`, `inferred-from-branch`, `curated-and-tested`, `unknown`).

Index map-to-script relationships through the actual map headers/events instead of assuming a filename maps to a particular `MAP_*` constant. The script file suffix (for example, `D40R0107`) is valuable evidence, but isn't itself proof that a symbolic map ID has been resolved.

### Keep HG/SS and region differences explicit

`pokeheartgold` documents the **US** HeartGold/SoulSilver disassembly. Flag and map naming may be shared across versions, but don't silently assume all symbols, script branches and progression behavior apply to Japanese, Korean or other regional ROMs. Add region and game selectors to fixture provenance. HG/SS event paths also branch on game version; one preset must not silently apply to both without review.

---

## 8. Story-state fixtures

A fixture is a **curated recipe plus a known-valid base save**, not a list of random bits to flip. The recipe identifies what it tests and how to reproduce it, and the CLI compiles the recipe to a raw `.sav` plus manifest and expectations.

```yaml
schema: hgss-save.fixture/v1
id: ss-whirl-islands-lugia-pre-encounter
game: SS
region: US
symbols: hgss-us@<pinned-commit>

base:
  path: ../checkpoints/ss-whirl-islands-verified.sav
  sha256: <actual-base-sha256>

position:
  mode: preserve-base
  # Change map/X/Y only through a separately reviewed safe-spawn record.

patch:
  operations:
    - op: flag.set
      id: FLAG_CAUGHT_LUGIA
      value: false
      expect: false

expect:
  - kind: save.checksums-valid
  - kind: field.equals
    field: flag.FLAG_CAUGHT_LUGIA
    value: false

runtime:
  target: lotus
  input_trace: ../../traces/ss-lugia-reach-trigger.json
  checkpoints:
    - overworld-loaded
    - cutscene-started
    - encounter-transition
    - battle-return

review:
  status: requires-runtime-verification
  notes: >-
    This is illustrative; the actual prerequisite flags, items, actors,
    and script path must be derived and tested before using it as a fixture.
```

An ideal base is a **legitimately played save at the last stable point before the event**, rather than an early save teleported into the chamber. Patching from a nearby checkpoint usually preserves scripts' implicit assumptions about actors, warps, followers and scene state.

### Save-level dependency model

For each supported event, maintain:

- `requires`: game version, map, items, badges, variables, flags, party conditions, prerequisites.
- `effects`: expected persistent changes after a successful event.
- `alternatives`: catch/defeat/run/whiteout or version-specific branches.
- `unsafe_if`: conditions known to break the intended script path.
- `runtime_only`: facts that can't be encoded in `.sav`, such as in-progress script state, collision transitions and animation progress.
- `evidence`: specific script lines, field code paths and validated fixture results.

Run a contradiction detector before editing: an event cannot simultaneously be marked completed and pending if the selected scenario requires otherwise; a required key item cannot be absent unless the script supports that branch.

**Do not automatically "fix" contradictions** using heuristic guesses. Explain what conflicts and require an explicit preset or a deliberate override.

### Concrete Lugia investigation

`files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s` contains the Whirl Islands Lugia encounter/cutscene sequence. Its script includes `FLAG_ENGAGING_STATIC_POKEMON`, `FLAG_HIDE_WHIRL_ISLAND_LUGIA`, `FLAG_CAUGHT_LUGIA`, a `WildBattle`, checks on its result, and a cutscene beginning with `LegendCutsceneClearBellAnimBegin` / `LegendCutsceneClearBellRiseFromBag`. Another script, `scr_seq_0884_T24R0801.s`, checks relevant bell/wing items and branches on version. Those are concrete entry points for research, **not** a complete proven prerequisite graph.

For the reported console crash during the Lugia-related event, build at least three fixtures: immediately before the prerequisite interaction, immediately before the cutscene trigger, and after the encounter. A fourth test should use a Lotus savestate at the exact failing instruction or cutscene frame. Crosscheck the `GetGameVersion` branch, saved actor flags, temporary variables and whether the current code path is even reached in the port.

---

## 9. Safety, validation, and auditing

Think of each mutation as a database transaction with a read set, write set and postconditions.

### Level A — structural

Required for **every** output:

- Exactly supported input length/format (v1: raw `0x80000` HGSS `.sav`).
- Valid HG/SS identification and credible primary/secondary block selection.
- Correct CRCs for both active main blocks; report integrity of auxiliary/backup copies.
- Noncorrupt initialized Pokémon in edited slots; valid Pokémon internal checksums.
- Values fit target field widths and PKHeX/game limits; party <= 6; box slot and inventory capacity correct.
- Output reopens through PKHeX.Core with `ChecksumsValid == true`.

### Level B — invariants

Required for supported typed edits:

- Coordinates and mirrored coordinate fields remain consistent; map ID is recognized and spawn is verified where policy requires it.
- Item pocket/type and maximum count are coherent; key-item duplication is rejected unless explicitly requested.
- Party count matches the constructed party slots, and empty slots are consistently represented.
- A game-specific field is not assigned values from the wrong HG/SS branch.
- Persistent IDs are not confused with temporary/special variables.
- A patch's `base_sha256`, expected values and registry revision match the actual inputs.

### Level C — story semantics

For a **curated named fixture** (not every low-level flag edit), validate its documented script prerequisites and known contradictions. On unproven assumptions, emit warnings and mark the fixture `needs-runtime-verification`. Never report `semantic: pass` solely because the CRC passes.

### Level D — runtime

Launch the target save in Kohjoh/Lotus with a reproducible input trace. Confirm map load, expected actors/trigger, transition, event completion and ability to save again. Preserve logs, frame timing, crash information and the resulting save. Repeat representative tests on original hardware, especially for rendering and memory-sensitive bugs.

### Useful diff model

Emit **two** complementary diffs:

1. `semantic`: known domain values changed (`flag FLAG_CAUGHT_LUGIA false -> true`, `money 1000 -> 50000`, party species, bag quantities, etc.).
2. `binary`: byte ranges changed, annotated as `requested-data`, `PKHeX-export-bookkeeping`, `checksum`, `unclassified`.

The binary diff is diagnostic, not the intended primary agent interface. Changes outside allowlisted regions are errors in strict mode. Expect `sav.Write()` to update changed-box bits and CRCs, possibly in more than one initialized partition; whitelist those **only after source and tests confirm the specific behavior**.

### Determinism and provenance

A generated fixture manifest should store:

```json
{
  "schema": "hgss-save.manifest/v1",
  "fixture_id": "ss-whirl-islands-lugia-pre-encounter",
  "input_sha256": "<computed>",
  "patch_sha256": "<computed>",
  "output_sha256": "<computed>",
  "pkhex_core_version": "26.10.18",
  "pokeheartgold_commit": "<pinned>",
  "tool_commit": "<pinned>",
  "game": "SS",
  "region": "US",
  "structural_validation": "pass",
  "semantic_validation": "needs-runtime-verification",
  "runtime_result": null
}
```

Make output byte-for-byte reproducible for the same input/version/patch. Don't write wall-clock timestamps into `.sav` unless the recipe explicitly requests them. Runtime test timestamps belong in separate reports, not in the deterministic fixture bytes.

---

## 10. Agent and MCP integration

**The CLI is the stable contract.** MCP is a thin transport wrapper that runs the same application service and returns the same JSON schemas. It should never maintain its own offset table or second mutation implementation.

Recommended read tools:

```text
hgss.inspect(save_ref)
hgss.validate(save_ref, level)
hgss.diff(before_ref, after_ref)
hgss.symbol_lookup(name)
hgss.symbol_search(query, kind, limit)
hgss.script_references(symbol, game)
hgss.fixture_list()
hgss.fixture_explain(id)
```

Recommended write tools:

```text
hgss.patch_plan(save_ref, patch)      # no mutation
hgss.patch_apply(save_ref, patch)     # new output; validated
hgss.fixture_build(id, base_ref)      # new output + manifest
```

Write outputs go to a restricted workspace path and return a **new artifact reference**, hashes, semantic diff and validation status. Never grant a model a generic `write_bytes(offset, bytes)` tool through the default MCP server. Put that under a separate audited research/debug capability.

### Agent workflow

1. Inspect the base save; list available symbols or script references.
2. Construct a patch or fixture referencing a particular base hash.
3. Call `patch_plan`. Read resolved numeric values, warnings, intended writes and contradictory assumptions.
4. Call `patch_apply`. Inspect semantic and binary diffs and validation results.
5. Tell Lotus to load the **new save artifact**. Run input trace and capture logs.
6. Attach `fixture_id`, hashes, emulator commit and test outcome to a reproducible failure report.

This avoids unreviewed tool chaining where an agent writes directly to a live emulator save directory.

### Lotus and Kohjoh save adapters

Do not assume that Kohjoh's 3DSX and CIA modes expose exactly the same underlying file path or bytes as a raw DS cartridge save. Define a `SaveTransport` adapter:

```text
RawHgssFile       <-> hgss-save
Kohjoh3dsxSave    <-> adapter <-> raw HGSS data
KohjohCiaSavedata <-> adapter <-> raw HGSS data
Lotus test fixture <-> session/mount adapter <-> raw HGSS data
```

An adapter owns only extraction/injection and transport metadata, not script semantics or structural editing. Keep the original format/container intact. Verify its mapping from Kohjoh's actual implementation before building automatic injection.

---

## 11. Testing plan

| Test class | What to prove |
| --- | --- |
| Parser smoke tests | Both HG and SS correctly identified; valid raw saves accepted, unrelated game saves rejected. |
| Counter-selection tests | General and PC block can be active in opposite partitions; no wrong-half writes. |
| Corruption tests | Truncated, wrong-sized, CRC-damaged, implausible revision and ambiguous input rejected safely. |
| Symbol tests | ID round trip, aliases, missing names, temporary/special classification, header drift. |
| Patch transaction tests | Expected-base mismatch, conflicting operations, missing prerequisites: no output. |
| Write/read tests | `SAV4HGSS` writes valid CRCs, selected field values survive reparse. |
| Inventory tests | Correct pocket, no duplicates, capacity and quantity rules, `CopyTo` persistence. |
| Pokémon tests | Party/box PK4 templates survive edit, export and in-game save cycle. |
| Idempotency tests | Applying the same `set` operation twice has the same logical result. |
| Diff-policy tests | No unexplained binary ranges outside expected data/metadata regions. |
| Runtime tests | Game loads, performs expected event, and saves back to a parseable file. |
| Regression fixtures | Early-game, Goldenrod, wild/trainer battles, caves/Flash, Kimono Girls, Whirl Islands, Bell Tower, Kanto. |

### Golden corpus

Keep a small **private**, provenance-tagged collection of legally obtained raw saves: HG and SS, early/mid/end progression, different active-block partition combinations, populated boxes, unusual party composition, and a few optional language/region variants. Don't make private saves or Pokémon game files a public repo requirement; test synthetic/minimized structures where feasible, and allow local fixture paths via environment variables.

### Round-trip acceptance test

For a representative fixture:

```text
base.sav
  -> hgss-save patch
  -> exported.sav
  -> PKHeX.Core reparse + schema assertions
  -> Kohjoh/Lotus boot and test trace
  -> in-game save
  -> final.sav
  -> PKHeX.Core reparse + expected invariants
```

The **in-game save** is essential. A file that loads once but loses PC edits, breaks future scripted interactions or corrupts on the next save is not a successful fixture.

Automated tests should run in Windows and Linux CI. macOS is useful for development if supported by the installed .NET release. Add hardware testing as a separate gated tier, not a blocker for every CLI change.

---

## 12. Build and distribution

Keep the first version as an ordinary framework-dependent `.NET 10` console executable. It is simpler to debug and supports the actual PKHeX.Core package target framework.

```bash
dotnet build -c Release
dotnet test -c Release
# Framework-dependent publish:
dotnet publish HgssSave.Cli/HgssSave.Cli.csproj -c Release -r win-x64 --self-contained false
```

Later, consider a self-contained single-file publish if distributing to agents without .NET installed. **Do not assume Native AOT will just work**: test dependency trimming, reflection, resources and PKHeX.Core behavior first. AOT is an optimization, not a project prerequisite.

Include in `version --json` the exact CLI, PKHeX.Core, symbol-registry schema and registry-commit revisions. Support `--no-color`, deterministic stdout JSON, path-safe behavior and a standard `--help` hierarchy.

---

## 13. Prioritized implementation plan

### Phase 0 — Lock references and collect valid test saves

- Pin PKHeX.Core to NuGet `26.10.18`; pin a `pokeheartgold` commit (record Git SHA).
- Establish project/license policy and keep ROMs/private saves out of source control.
- Capture two or three known-good HGSS saves, including one where different blocks have different active partitions if possible.
- Write the domain schema and error codes **before** implementing mutators.

**Exit criterion:** specification and fixtures are reproducible and inputs are traceable.

### Phase 1 — Read-only CLI

- `inspect`, `validate`, `diff` (initially trainer, location, flags/vars, bag/party summary).
- Independent preflight partition/CRC report, with no mutation.
- JSON-output contract and tests for invalid/truncated data.

**Exit criterion:** agents can reliably identify save state and structural problems without touching files.

### Phase 2 — Safe mutation engine

- `patch --dry-run`, transactional `patch`, `flag.set`, `variable.set`, `trainer.money.set`, `inventory.quantity.set`.
- Output to new file only, SHA-256 precondition, reparse, postcondition check, atomic commit and diff audit.
- Test raw input → patch → export → reload.

**Exit criterion:** a valid input produces a new validated save with only intended logical changes.

### Phase 3 — Symbols and script evidence

- Header-based registry generator, aliases and storage classes.
- `symbols search/lookup`; named inputs with source paths and pinned revisions.
- Initial script reference index, including uncertainty labels.

**Exit criterion:** agents need neither numeric IDs nor raw source browsing for routine flag/variable edits.

### Phase 4 — Story fixtures

- Recipe schema; source-save provenance; curated early-game and Goldenrod fixtures.
- Whirl Islands/Lugia script dependency audit and two or three pre/post-event fixtures.
- Map-safe spawn database; party/template and PC editing as needed.

**Exit criterion:** fixtures are useful to test real story and rendering edge cases, not merely CRC-valid.

### Phase 5 — Lotus/CI integration

- MCP read/write tools constrained to an artifact workspace.
- Load generated save into Lotus, run a scripted input trace and collect logs/savestates.
- Regression manifest, semantic diff and failure artifact bundle.
- Select O3DS verification runs.

**Exit criterion:** an agent can generate, run, inspect and reproduce an HGSS story-event test end to end.

---

## 14. Major engineering risks

| Risk | Mitigation |
| --- | --- |
| An export is CRC-valid but story-invalid | Require curated preconditions and runtime tests; never equate CRC with script correctness. |
| Different active general/storage partitions | Let PKHeX.Core select each; independently report their revision/checksum status. |
| Corrupted input silently repaired by editor behavior | Preflight + strict refusal; separate explicit recovery mode. |
| Incorrect saved map location | Prefer verified nearby checkpoint and safe-spawn registry; check actor/warp context. |
| Misclassified temporary variables/flags | Registry storage-kind classification; reject runtime-only IDs. |
| PKHeX.Core updates alter write behavior | Pin package; binary diff/golden tests before updating. |
| `pokeheartgold` names/semantics change | Pin commit; regenerate index and diff symbol changes; review renamed/unknown symbols. |
| Different HG/SS or regional scripts | Record game and ROM region; allow game-specific recipes and evidence. |
| PK4 or bag edits don't survive in-game save | Use public APIs correctly, copy bag back and always perform round-trip runtime tests. |
| Agent writes arbitrary data to user save | No in-place writes by default, base-hash preconditions, restricted MCP filesystem access. |
| Licenses/IP complicate redistribution | Honor PKHeX GPL license; avoid vendoring unlicensed disassembly and copyrighted save/ROM data without permission. |

---

## 15. Authoritative source references

### PKHeX / library API

- [PKHeX.Core on NuGet — 26.10.18, `net10.0`, GPL-3.0-or-later](https://www.nuget.org/packages/PKHeX.Core/26.10.18)
- [PKHeX `SAV4HGSS.cs`: HGSS structure, coordinate fields, inventory, Pokéwalker and box bookkeeping](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4HGSS.cs)
- [PKHeX `SAV4.cs`: active-block logic, event flags/work, checksums](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4.cs)
- [PKHeX `SaveFile.cs`: `Write()`, party and PC slot API](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SaveFile.cs)
- [PKHeX `SAV4BlockDetection.cs`: revision comparisons](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/SAV4BlockDetection.cs)
- [PKHeX `PlayerBag4HGSS.cs`: inventory layout and copy-back](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Items/Bags/PlayerBag4HGSS.cs)
- [PKHeX `InventoryPouch4.cs`: item representation](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Inventory/Pouch/InventoryPouch4.cs)
- [PKHeX `ItemStorage4HGSS.cs`: valid item groups](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Items/ItemStorage4HGSS.cs)

### HGSS source and story logic

- [pret/pokeheartgold README — WIP/disassembly warning](https://github.com/pret/pokeheartgold/blob/master/README.md)
- [Variables and offsets: `include/constants/vars.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/vars.h)
- [Persistent/temporary flags: `include/constants/flags.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/flags.h)
- [Maps: `include/constants/maps.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/maps.h)
- [Items: `include/constants/items.h`](https://github.com/pret/pokeheartgold/blob/master/include/constants/items.h)
- [Flag/variable storage and access: `src/save_vars_flags.c`](https://github.com/pret/pokeheartgold/blob/master/src/save_vars_flags.c)
- [Local field data / saved objects: `src/save_local_field_data.c`](https://github.com/pret/pokeheartgold/blob/master/src/save_local_field_data.c)
- [Lugia event and cutscene script](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s)
- [Bell/wing item checks script](https://github.com/pret/pokeheartgold/blob/master/files/fielddata/script/scr_seq/scr_seq_0884_T24R0801.s)

### Practical implementation caveat

Use a commit SHA rather than `master` when implementing this specification. The URLs above deliberately point to discoverable upstream source files; the fixture manifests should point to **their exact frozen commits**.

**Recommended first commit:** a bare `HgssSave.Cli` + `HgssSave.PKHeX` solution with `inspect`, `validate`, and a single validated, non-overwriting `flag.set` patch. Once that round-trip passes in HG and SS, add the symbol registry and fixtures; don't build an elaborate script interpreter before the save writer is proven.
