# hgss-save command reference

Every command, argument, option, environment variable, and exit code. The same content is printed by `hgss-save --help`. Source of truth for parsing: `src/HgssSave.Cli/Program.cs` (`Args`) and `src/HgssSave.Cli/Commands.cs`.

## Conventions

- Stdout is one JSON document, schema `hgss-save.report/v1`, except `script index` and `symbols build-registry`, which print a one-line summary. Diagnostics go to stderr. Errors also emit a JSON error document on stdout.
- Options take `--name value` or `--name=value`. Options may appear anywhere after the command. `--help` / `-h` anywhere prints the reference and exits 0.
- Options that take a value (parsing treats the next token as that value): `--recipe --out --outdir --bases --report --symbols --safe-spawns --seen --caught --script-refs --kind --kinds --limit --level --game --trainer-name --tid --sid --gender --language --headers --scripts --commit --registry-id --project --max-list --flags-limit --expect --expect-species --max-ranges`. Everything else starting with `--` is a boolean switch: `--force --dry-run --require-symbols --no-dex`.
- `ID` for flags, variables, and items accepts a symbol name (`FLAG_CAUGHT_LUGIA`), a decimal number, or `0x` hex. All other numeric arguments are decimal integers. Booleans accept `true|false|1|0`.
- Writing commands never edit in place. The input is read, the result is written to `--out` atomically (temp file in the target directory, then rename). An existing `--out` is refused unless `--force` is given. Every writing command requires `--out`, including with `--dry-run`.
- A patch that cannot fully apply exits non-zero and writes nothing.

## Exit codes

| Code | Name | Meaning |
|---|---|---|
| 0 | Success | |
| 2 | BadArguments | Missing/invalid argument, unknown command or kind |
| 3 | UnsupportedSave | Not a raw HG/SS save (Pearl/Platinum/White, wrong size, wrong identity) |
| 4 | StructuralFailure | Checksum or block-integrity failure |
| 5 | SymbolFailure | Name did not resolve, or registry missing where required |
| 6 | PreconditionFailed | `--expect` mismatch, range error, base-sha/game mismatch, conflicting writes |
| 7 | OutputConflict | Output exists without `--force`, or file I/O error |
| 8 | UnsafeOperation | Refused by policy (unsupported validate level, non-allowlisted spawn, non-writable symbol class) |
| 9 | InternalError | Unhandled exception |

## Files the tool looks for

| File | Lookup order | Used by |
|---|---|---|
| `hgss-us.symbols.json` | `--symbols PATH`, `$HGSS_SYMBOLS`, beside the exe, `./hgss-us.symbols.json`, `./registry/hgss-us.symbols.json` | every command that reads or writes a save; required by `symbols lookup/search` |
| `hgss-us.warp-spawns.json` | `--warp-spawns PATH`, `$HGSS_WARP_SPAWNS`, beside the exe, `./hgss-us.warp-spawns.json`, `./registry/hgss-us.warp-spawns.json` | `location warp`, `location.warp` recipes |
| `safe-spawns.json` | `--safe-spawns PATH`, `$HGSS_SAFE_SPAWNS`, beside the exe (also `fixtures/` under it), `./safe-spawns.json`, `./fixtures/safe-spawns.json` | `location set`, `patch` recipes with `location.set`, `fixture build` |
| `hgss-us.script-refs.json` | `--script-refs PATH`, `$HGSS_SCRIPT_REFS`, beside the exe, `./hgss-us.script-refs.json`, `./registry/hgss-us.script-refs.json` | `script references` |

Without a symbol registry, numeric ids still work and names degrade (`"named": false`). The portable build bundles all three files beside the exe.

## Read-only commands

### `version [--symbols PATH]`
Prints CLI version, PKHeX.Core version, loaded registry revision, and the command list.

### `inspect INPUT [--symbols PATH] [--flags-limit N]`
Identity (game, trainer, TID/SID), location, money, party, boxes, bag, dex counts, structural validation, and up to N set flags (default 200). Exit 4 if the save fails structural validation.

### `validate INPUT [--level structural] [--symbols PATH]`
Structural validation only (size, identity, checksums, active-copy selection). `--level` accepts only `structural`; anything else exits 8. The report states `"semantic": "not-evaluated"`: a CRC-valid save is not a story-valid save.

### `diff BEFORE AFTER [--max-list N] [--max-ranges N] [--symbols PATH]`
Semantic diff (flags, variables, trainer, party, boxes, bag, location) plus byte-range diff by region. `--max-list` caps listed semantic changes (default 200); `--max-ranges` caps listed byte ranges (default 40). Always exits 0 on two readable saves, whether or not they differ.

### `flag get INPUT ID [--symbols PATH]`
Reads one persistent flag. `ID` must resolve to a persistent flag. Temp and map-temp flags are rejected (exit 5).

### `var get INPUT ID [--symbols PATH]`
Reads one save-work variable (`VAR_*`, slots `0x4000+`). Special and temp variables are rejected.

### `flags list INPUT [--limit N] [--symbols PATH]`
Every set persistent flag. Default limit 5000.

### `vars list INPUT [--limit N] [--symbols PATH]`
Every non-zero save variable. Default limit 500.

### `party export INPUT SLOT --out FILE.pk4 [--force] [--symbols PATH]`
Writes the 236-byte party-format Pokémon in SLOT (0..5) to FILE.pk4.

### `box export INPUT BOX SLOT --out FILE.pk4 [--force] [--symbols PATH]`
Writes the 136-byte stored-format Pokémon in BOX (0..17), SLOT (0..29).

## Writing commands

All of these accept the common patch options listed at the end of this section. Each one builds a single-operation `hgss-save.patch/v1` recipe, so behavior is identical to `patch` with one operation.

### `patch INPUT --recipe PATCH.json --out OUT [common options]`
Applies a recipe (format in the README). File paths inside the recipe resolve relative to the recipe's directory. Recipe-level `base_sha256`, `game`, and `symbol_registry` are checked against the input and loaded registry before anything is applied.

### `flag set INPUT ID true|false --out OUT [--expect true|false]`
`--expect` refuses unless the flag currently has that value. Only persistent flags are writable.

### `var set INPUT ID VALUE --out OUT [--expect N]`
VALUE 0..65535. `--expect` is the required current value. Only save-work variables are writable.

### `bag set INPUT ITEM QUANTITY --out OUT [--expect N]`
ITEM is `ITEM_*` or a number. QUANTITY 0..999 (0 removes). `--expect` is the required current quantity. HMs cap at 1; an over-full pocket is an error.

### `trainer money set INPUT VALUE --out OUT [--expect N]`
VALUE is a non-negative integer (values above the PKHeX HGSS maximum are refused with `money-range`). `--expect` is the required current money.

### `dex set INPUT SPECIES --seen BOOL --caught BOOL --out OUT`
SPECIES 1..493. `--seen` and `--caught` are both required.

### `location set INPUT MAP X Y Z --out OUT [--safe-spawns PATH]`
Sets map id, tile X/Y, and elevation Z. Only exact tuples listed in `safe-spawns.json` are accepted, because walkability is not decoded; anything else exits 8. See `docs/coverage-gaps.md`.

### `location warp INPUT MAP WARP --out OUT [--direction N] [--warp-spawns PATH]`
Teleports to warp event WARP of MAP by the game's own Continue path: sets flag `0x966` and writes the saved dynamic warp `{MAP, WARP}`. On the next Continue the game spawns the player on that warp event's tile and builds the map's actors from zone-event data. `--direction` is 0..3, default 1 (the only value observed in saves). Only (MAP, WARP) pairs listed in `hgss-us.warp-spawns.json` are accepted; others exit 8 (`warp-unverified`). The stored x/y are the warp table's tile, informational only: the game recomputes them from the warp at load. Runtime-verified on 7 of 7 sampled targets (`docs/state-log.md` F5). Limits: the player arrives on a warp tile (a door or map edge), not an arbitrary tile; the saved current position is left as it was until the game next saves.

### `party import INPUT SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]`
Imports a known-good `.pk4` (136 stored or 236 party bytes, PK4 checksum verified, species 1..493) into party SLOT 0..5. `--expect-species` refuses a file whose species differs. By default the dex is updated for the imported species; `--no-dex` skips that.

### `party clear INPUT SLOT --out OUT`
Empties party SLOT 0..5.

### `box import INPUT BOX SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]`
As `party import`, into BOX 0..17, SLOT 0..29.

### `box clear INPUT BOX SLOT --out OUT`
Empties one box slot.

### Common patch options

| Option | Meaning |
|---|---|
| `--out PATH` | Output save. Required. |
| `--force` | Overwrite an existing `--out`. |
| `--dry-run` | Plan and report only; nothing is written. Operation reports as `patch.dry-run`. |
| `--require-symbols` | Reject numeric flag/variable ids that have no matching registry symbol (otherwise they apply with a warning). |
| `--max-ranges N` | Cap on byte ranges in the audit report (default 40). |
| `--safe-spawns PATH` | Safe-spawn list for `location.set`; env `HGSS_SAFE_SPAWNS`. |
| `--warp-spawns PATH` | Warp table for `location.warp`; env `HGSS_WARP_SPAWNS`. |
| `--symbols PATH` | Symbol registry; env `HGSS_SYMBOLS`. |

## Symbol and script-index commands

### `symbols lookup NAME [--kind KIND] [--symbols PATH]`
Resolves a name or number. KIND is one of `flag` (alias `persistent_flag`), `var` (aliases `variable`, `save_work`), `map`, `item`, `species`, `move`. Without `--kind`, a name defined in more than one category is reported as ambiguous. Exit 5 if unresolved.

### `symbols search QUERY [--kind K1,K2] [--limit N] [--symbols PATH]`
Substring search over names. `--kind` takes a comma list (`--kinds` is an alias). Default limit 50.

### `symbols build-registry --headers DIR --commit SHA [--out PATH] [--registry-id ID] [--project NAME]`
Compiles `FLAG_*`, `VAR_*`, `MAP_*`, `ITEM_*`, `SPECIES_*`, `MOVE_*` from pret/pokeheartgold headers (DIR is the checkout root). Defaults: `--out hgss-us.symbols.json`, `--registry-id hgss-us`, `--project pokeheartgold`. Also writes `source-manifest.json` beside the output. Unresolvable macros are listed under `skipped`, never guessed.

### `script index --scripts DIR --commit SHA [--out PATH] [--project NAME]`
Indexes flag/var/item references in `files/fielddata/script/scr_seq/*.s`. DIR is the decomp checkout or the `scr_seq` directory itself. Defaults: `--out hgss-us.script-refs.json`, `--project pokeheartgold`.

### `script references SYMBOL [--script-refs PATH] [--limit N]`
Lists indexed reads/writes/checks of SYMBOL with file:line evidence. Default limit 100. Exit 5 if there are none.

## Fixtures

### `fixture build RECIPE.json --outdir DIR [--bases DIR] [--force] [--max-ranges N] [--safe-spawns PATH] [--symbols PATH]`
Stages a fixture from a base save named in the recipe. `--bases` is the directory holding base saves (default: current directory); base saves are never committed. Writes `<id>.sav` and `<id>.manifest.json` into `--outdir`. Verifies the recipe's `base_sha256` and game.

### `fixture verify MANIFEST.json [--outdir DIR]`
Re-hashes the output save against the manifest and re-validates it structurally. `--outdir` defaults to the manifest's directory.

### `fixture record-runtime MANIFEST.json --report REPORT.json`
Attaches a `tools/leveld_run.py` report to the manifest's `runtime_result`. Refused (exit 6) if the report's `save_sha256` does not match the manifest's output hash. Exit 6 also when the report says the run failed.

## Containers and blank saves

### `container probe FILE`
Reports `raw`, DeSmuME `.dsv`, or other, with size and sha256. Exit 3 for other.

### `container unwrap FILE --out OUT [--force] [--symbols PATH]`
Extracts the raw `0x80000` save from a `.dsv`. Never implicit; no other command opens a `.dsv`.

### `blank --game HG|SS --out OUT [--kind fresh-structure|erased-flash] [--trainer-name N] [--tid N] [--sid N] [--gender 0|1] [--language 1..8] [--force] [--symbols PATH]`
Synthesizes a structurally valid blank save (`fresh-structure`, default) or an erased-flash image with no save structure (`erased-flash`, negative tests only). `--language` defaults to 2 (English). Blank saves are not story-valid; use real played saves for fixtures.

## Recipe operations

For `patch`. Ids accept names or numbers. Full example in the README.

| `op` | Fields |
|---|---|
| `flag.set` | `id`, `value`, optional `expect` |
| `variable.set` | `id`, `value`, optional `expect` |
| `trainer.money.set` | `value`, optional `expect` |
| `inventory.quantity.set` | `item`, `quantity`, optional `expect` |
| `party.slot.import` | `slot`, `file`, optional `expect_species`, `update_dex` |
| `party.slot.clear` | `slot` |
| `box.slot.import` | `box`, `slot`, `file`, optional `expect_species`, `update_dex` |
| `box.slot.clear` | `box`, `slot` |
| `location.set` | `map`, `x`, `y`, `z` |
| `location.warp` | `map`, `warp`, optional `direction` (0..3, default 1) |
| `dex.set` | `species`, `seen`, `caught` |

Top level keys: `schema` (`hgss-save.patch/v1`), optional `game` (`HG`/`SS`), `base_sha256`, `symbol_registry`, `operations`, `assert`. The reader is strict: unknown keys, duplicate keys, wrong types, and unknown operations are rejected. Source: `src/HgssSave.Core/Patch.cs`.

### Assertions

Each `assert` entry is `{"field": ..., "equals": ...}`, evaluated against the re-parsed output save.

| `field` | `equals` |
|---|---|
| `trainer.money` | non-negative integer |
| `trainer.coins` | 0..50000 |
| `trainer.badges_johto`, `trainer.badges_kanto` | bitmask 0..255 |
| `party.count` | 0..6 |
| `party.<0-5>.species` | species id 0..493 |
| `box.<0-17>.<0-29>.species` | species id 0..493 |
| `location.map_id` | map id 0..65535 |
| `location.spawn` | object `{"map":N,"x":N,"y":N,"z":N}` (`z` defaults to 0) |
| `game` | `"HG"` or `"SS"` |
| `checksums.valid` | boolean |
| `flag.<ID>` | boolean |
| `variable.<ID>` | integer 0..65535 |
| `bag.<ITEM>` | quantity 0..999 |
| `dex.<1-493>.seen`, `dex.<1-493>.caught` | boolean |
