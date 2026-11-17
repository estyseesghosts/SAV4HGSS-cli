# hgss-save

Headless read/write CLI for raw Pokémon HeartGold/SoulSilver saves (`SAV4HGSS`), built on PKHeX.Core. For Kohjoh regression testing: inspect saves, apply typed story/script/event-flag edits as atomic transactions, and diff saves.

Scope: HG/SS raw `0x80000` saves only. Out of scope: DPPt (`SAV4DPPt`), any MCP server, party/box importing, script-reference indexing, curated story fixtures, and emulator runtime testing (Level D stays manual in melonDS).

## Build and test

The .NET SDK lives outside `PATH` on this machine; prepend it per shell:

```powershell
$env:DOTNET_ROOT='C:\Users\julie\AppData\Local\Microsoft\dotnet'
$env:PATH='C:\Users\julie\AppData\Local\Microsoft\dotnet;'+$env:PATH
dotnet build HgssSave.slnx -c Release
dotnet test HgssSave.slnx
```

Pinned dependencies: PKHeX.Core 26.10.18 (Central Package Management), .NET 10 (`global.json`). Compiling against the NuGet package is the API-drift check.

## Commands

Stdout is always one deterministic JSON document (`schema: hgss-save.report/v1`). Diagnostics go to stderr. Exit codes follow the design doc: 0 success, 2 bad arguments, 3 unsupported save, 4 structural failure, 5 symbol failure, 6 precondition failure, 7 output conflict / I/O error, 8 unsafe operation, 9 internal error. A patch that cannot fully apply never exits 0 and never writes output.

The symbol registry (`registry/hgss-us.symbols.json`) is found via `--symbols PATH`, `$HGSS_SYMBOLS`, the CLI directory, `./hgss-us.symbols.json`, or `./registry/hgss-us.symbols.json`. Without it, numeric ids still work and names degrade (`"named": false`, map symbols null).

```powershell
hgss-save version
hgss-save inspect INPUT.sav [--symbols R] [--flags-limit N]
hgss-save validate INPUT.sav [--level structural]
hgss-save diff BEFORE.sav AFTER.sav [--max-list N] [--max-ranges N]
hgss-save patch INPUT.sav --recipe patch.json --out OUT.sav [--dry-run] [--require-symbols]
hgss-save flag get INPUT.sav FLAG_CAUGHT_LUGIA
hgss-save flag set INPUT.sav FLAG_CAUGHT_LUGIA true --out OUT.sav [--expect false]
hgss-save var get INPUT.sav VAR_PLAYER_STARTER
hgss-save var set INPUT.sav VAR_PLAYER_STARTER 2 --out OUT.sav
hgss-save bag set INPUT.sav ITEM_POTION 10 --out OUT.sav
hgss-save trainer money set INPUT.sav 50000 --out OUT.sav
hgss-save party export INPUT.sav 0 --out slot0.pk4
hgss-save dex set INPUT.sav 25 --seen true --caught true --out OUT.sav
hgss-save location set INPUT.sav 73 459 246 3 --out OUT.sav [--safe-spawns PATH]
hgss-save party import INPUT.sav 0 slot0.pk4 --out OUT.sav [--expect-species 25] [--no-dex]
hgss-save party clear INPUT.sav 0 --out OUT.sav
hgss-save box export INPUT.sav 0 0 --out box00.pk4
hgss-save box import INPUT.sav 0 0 box00.pk4 --out OUT.sav [--expect-species 25] [--no-dex]
hgss-save box clear INPUT.sav 0 0 --out OUT.sav
hgss-save flags list INPUT.sav [--limit N]
hgss-save vars list INPUT.sav [--limit N]
hgss-save symbols lookup FLAG_CAUGHT_LUGIA [--kind flag]
hgss-save symbols search lugia [--kind flag,var,map,item] [--limit N]
hgss-save script index --scripts 3DS/pokeheartgold --commit <sha> --out registry/hgss-us.script-refs.json
hgss-save script references FLAG_CAUGHT_LUGIA [--script-refs R] [--limit N]
hgss-save fixture build fixtures/recipes/ss-violet-safe-money.json --bases <checkpoint-dir> --outdir <out>
hgss-save fixture verify <out>/ss-violet-safe-money.manifest.json --outdir <out>
hgss-save container probe FILE
hgss-save container unwrap FILE.dsv --out OUT.sav
hgss-save blank --game HG|SS --out OUT.sav [--trainer-name N] [--tid N] [--sid N] [--gender 0|1] [--language 2]
```

`--force` overwrites an existing `--out`. There is no in-place editing; writing onto the input path is refused.

## Patch recipes

```json
{
  "schema": "hgss-save.patch/v1",
  "game": "HG",
  "base_sha256": "<input-sha256>",
  "symbol_registry": "hgss-us@<commit>",
  "operations": [
    {"op": "flag.set", "id": "FLAG_CAUGHT_LUGIA", "value": true, "expect": false},
    {"op": "variable.set", "id": "VAR_PLAYER_STARTER", "value": 2},
    {"op": "trainer.money.set", "value": 50000},
    {"op": "inventory.quantity.set", "item": "ITEM_POTION", "quantity": 10},
    {"op": "party.slot.import", "slot": 0, "file": "lugia.pk4", "expect_species": 249},
    {"op": "box.slot.clear", "box": 0, "slot": 0}
  ],
  "assert": [{"field": "trainer.money", "equals": 50000}, {"field": "party.0.species", "equals": 249}]
}
```

Ids accept names or numbers (`279`, `0x117`). Operations are idempotent `set` semantics with expected-original-state checks; conflicting writes to one field are rejected. Every operation re-reads from the exported save before the report is accepted. Party/box imports take known-good `.pk4` templates (136-byte stored or 236-byte party, PK4 checksum verified, species 1..493); they are never synthesized from a bare species id. File paths resolve relative to the recipe directory.

## Safety model

1. Strict open: exact size, HG/SS identity, valid checksums, intact active general/storage copies (independent partitions, revision-counter selection). Anything else is refused, never repaired.
2. Plan against the pristine save (game/base-sha/registry checks, preconditions, conflict detection). `--dry-run` stops here.
3. Apply to one in-memory save, serialize via PKHeX, re-parse, re-check postconditions and assertions.
4. Byte audit: every changed byte must be a requested write, a measured no-export-bookkeeping byte, or a recomputed checksum footer. Anything else fails the run. Output commits atomically (temp file + rename into a new path).

`validate` reports structural validity only (`"semantic": "not-evaluated"`). A CRC-valid save is not a story-valid save: flags/variables/items are persistent fields, but correct story state also needs actors, warps, and script context the save cannot encode. Start fixtures from legitimately played saves near the event, not teleported early saves.

## Symbol registry

`registry/hgss-us.symbols.json` maps `FLAG_*`, `VAR_*`, `MAP_*`, `ITEM_*`, `SPECIES_*`, `MOVE_*` names to ids, compiled from `pret/pokeheartgold` at commit `4e9563397ab4e5c407271472935f9e270ac6a804` (local checkout at `3DS/pokeheartgold`). Writable kinds are `persistent_flag` and `save_work` only; map-temp flags (`FLAG_MAPTEMP_*`), `FLAG_TEMP_x*`, temp/object-gfx vars (`0x4000-0x402F`), and special vars (`0x8000+`) are classified and rejected as writes. Rebuild offline with:

```powershell
hgss-save symbols build-registry --headers 3DS/pokeheartgold --commit <sha> --out registry/hgss-us.symbols.json
```

The builder evaluates `#define` integer expressions with a restricted evaluator (no C toolchain, no eval). Non-integer or unresolvable macros are listed under `skipped`, never guessed. `source-manifest.json` records the commit and per-kind counts.

## Script references

`registry/hgss-us.script-refs.json` indexes every `FLAG_*`/`VAR_*`/`ITEM_*` read or write in `files/fielddata/script/scr_seq/*.s` at the pinned commit, with file:line evidence and the enclosing `ScrDef` label. Access is `read` (`GoToIfSet`, `Compare`, ...), `write` (`SetFlag`, `SetVar`, `GiveItem`, ...), or `check` (`HasItem`, ...). Rebuild offline with:

```powershell
hgss-save script index --scripts 3DS/pokeheartgold --commit <sha> --out registry/hgss-us.script-refs.json
```

Example evidence used by the Lugia fixture: `SetFlag FLAG_CAUGHT_LUGIA` in `scr_seq_0104_D40R0107.s:112`, `GoToIfSet FLAG_CAUGHT_LUGIA` in `scr_seq_0103_D40R0104.s:34`, `HasItem ITEM_SILVER_WING` in `scr_seq_0884_T24R0801.s:46`.

## Fixtures

`fixtures/recipes/*.json` stage curated story states from legitimately played base saves (never committed; resolve via `--bases DIR`). `fixtures/safe-spawns.json` records verified map/x/y spawns taken from real saves (Violet, Azalea, Mahogany, Blackthorn, New Bark, Route 38 gatehouse, Dragons Den Shrine, Cherrygrove). Build and verify with:

```powershell
hgss-save fixture build fixtures/recipes/ss-violet-safe-money.json --bases <checkpoint-dir> --outdir <out> [--force]
hgss-save fixture verify <out>/ss-violet-safe-money.manifest.json --outdir <out>
hgss-save fixture record-runtime <out>/ss-violet-safe-money.manifest.json --report leveld-out/<id>/report.json
```

Level D runs headless via `tools/leveld_run.py` on the melonDS-MCP native core (boot matrix + continue-to-overworld for Violet, Lugia, Kanto in `docs/runtime-results.md`); the manual checklist in `docs/runtime-validation.md` remains for event-movie observation.

## Format notes

- Active general and storage blocks can live in opposite partitions; selection uses major/minor revision counters (`SAV4.cs:180-184`, `SAV4BlockDetection.cs:19-33`), not checksums.
- `SetWork` takes a zero-based slot (`varId - 0x4000`, `SAV4.cs:552-553`); `SetEventFlag` takes the flag number directly (`SAV4.cs:545-550`).
- `sav.Inventory` builds a detached bag; writes require `CopyTo` (`PlayerBag4HGSS.cs:26-31`). HMs cap at 1 and over-full writes are errors, never clamps (`PlayerBag4HGSS.cs:33-38`).
- PKHeX pouch array lengths differ from physical pocket sizes (192 general ids for 165 slots, 13 battle ids for 30 slots); the CLI clamps to physical slots and ignores phantom general indices.
- `Write()` normalizes exports (box-changed bits `0x3FFFF`, backup CRCs, extra-block footers), so no-edit exports are not byte-identical; the audit derives this baseline from PKHeX itself.
- Containers: raw `0x80000` is the only editable input. DeSmuME `.dsv` (raw + `0x7A` footer ending `|-DESMUME SAVE-|`) can be probed/unwrapped explicitly, never stripped implicitly.

## Verification

`dotnet test` runs 54 tests: strict open/reject paths, flag/var/money/bag/party/box/location/dex round-trips, aliasing, transaction policy, byte audit, containers, fixture parsing, manifest runtime round-trips, safe-spawn gating, and registry spot checks. Real saves in `3DS/hgss-saves` verify inspect/patch/diff and all twenty-eight committed fixtures on the same-trainer M@ster/Master lineage plus early/post-game, HG and event-distribution files (Falkner, Bugsy, Whitney, Morty, Chuck, Jasmine, Violet, Azalea, Goldenrod, Burned Tower pre-state, Ecruteak, Cianwood, Mahogany, Pryce, Radio Tower, E4 pre-challenge, Blackthorn/Clair, Kimono dance, Lugia pre-event, Ho-Oh rebattle, E4 Champion, Kanto gyms, Kanto events, Kanto complete, legendary cluster, Dragons Den shrine, Celebi time-travel pre-state, Arceus event pre-state, Spiky-Pichu event pre-state, Enigma Stone pre-state, Rotom appliance-room pre-state); Pearl/Platinum/White saves are correctly rejected with exit 3. Every fixture boots headless via `tools/leveld_run.py` (Violet, Lugia, Kanto continue to overworld with live-spawn asserts against the RAM Location struct; all others boot under their ROMs with live-spawn asserts; menu/overworld `.mcpr` savestates capture and restore); `fixture record-runtime` writes the headless result back into each manifest's `runtime_result`. The script index (9960 refs, 965 symbols) cross-checks Lugia/Ho-Oh/bell/wing/Kimono/Celebi/Arceus/Pichu/Enigma/Rotom/Burned Tower story paths, and `fixtures/event-prereqs.json` records per-beat reads/writes with file:line evidence plus observed values (12 beats). Runtime in-game behavior (boot, event trigger, re-save round-trip in melonDS) remains a manual checklist per fixture (docs/runtime-validation.md).

## License

Links PKHeX.Core (GPL-3.0-or-later); treat distributed builds as GPL-covered. The registry stores extracted identifiers plus provenance, not decompilation sources. Do not redistribute ROMs or user saves.
