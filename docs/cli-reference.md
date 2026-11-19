# hgss-save command reference

Every command, argument, option, environment variable, and exit code. The same content is printed by `hgss-save --help`. Source of truth for parsing: `src/HgssSave.Cli/Program.cs` (`Args`) and `src/HgssSave.Cli/Commands.cs`.

## Conventions

- Stdout is one JSON document, schema `hgss-save.report/v1`, except `script index` and `symbols build-registry`, which print a one-line summary. Diagnostics go to stderr. Errors also emit a JSON error document on stdout.
- Options take `--name value` or `--name=value`. Options may appear anywhere after the command. `--help` / `-h` anywhere prints the reference and exits 0.
- Options that take a value (parsing treats the next token as that value): `--recipe --out --outdir --bases --report --symbols --safe-spawns --seen --caught --script-refs --kind --kinds --limit --level --game --trainer-name --tid --sid --gender --language --headers --scripts --commit --registry-id --project --max-list --flags-limit --expect --expect-species --max-ranges --presets --badge-rules --var --map --scene-triggers --warp-spawns --direction --johto --kanto`. Everything else starting with `--` is a boolean switch: `--force --dry-run --require-symbols --no-dex --raw --strict`. Note: `--x` and `--y` (used only by the `location warp` free spawn) are read as value options by the command but are not in the parser's value-option set, so pass them with `=` (`--x=16 --y=38`); plain `--x 16 --y 38` leaves the bare numbers in the positional stream (harmless for this command, but do not rely on it elsewhere).
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

### `party create --species N --level N --out FILE.pk4 [--moves a,b,c,d] [--ot NAME] [--tid N] [--sid N] [--language N] [--nickname NAME] [--force] [--symbols PATH]`
Builds the 236-byte party-format file an emulator run needs, without an input save. SPECIES is 1..493, LEVEL 1..100.

Without `--moves`, the moveset is the species' own level-up moves at that level (the last four, from the HGSS learn table), so move slot 1 — the one a bare A press selects — is legal for that species. `--moves` writes the four ids in order; `0` is an empty slot. Defaults: OT `STAGE`, TID 4242, SID 1717, language 2, no nickname (the nickname field still holds the species name, because a zeroed one renders as garbage in the battle box). `--nickname` sets one and marks the entity nicknamed.

This is staging tooling, not a legality claim: trainer ids, IVs (31), EVs (6/252/0/0/0/252), ball and met location are fixed, and the report says so (`harness_entity: true`, `legality: "not-evaluated"`). The generated file passes the same size/parse/species/checksum validation `party import` runs. Recipes still take exported templates; nothing in the patch path synthesizes a Pokémon from a species id.

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

### `trainer badges set INPUT [--johto MASK] [--kanto MASK] [--raw] [--badge-rules PATH] --out OUT`
MASK is 0..255; bit n is badge n in game order (Johto: Zephyr, Hive, Plain, Fog, Storm, Mineral, Glacier, Rising; Kanto: Boulder, Cascade, Thunder, Rainbow, Soul, Marsh, Volcano, Earth). At least one of the two is required; an omitted mask is left unchanged. Badges are bytes in the trainer block (`PlayerProfile.johtoBadges/kantoBadges`, player_data.c), with no flag mirror. Values outside 0..255 exit 2 (`badges-range`).

By default the command also writes what the game itself does when each newly gained badge is won, from `hgss-us.badge-rules.json` (built by `tools/make_badge_rules.py` from the gym scripts, checked against the played saves; `docs/archive/state-log.md` F8): the `SetFlag`/`ClearFlag`/`SetVar`/`SetTrainerFlag` statements after `GiveBadge`, the TM item if its `FLAG_GOT_TM*` flag is set and the bag has none, and the `VAR_MIDGAME_BADGES` counter (number of Fog, Storm, Mineral and Glacier badges; at 4 or more it keeps the larger of the count and the current value, since the Rocket takeover script raises it to 5). Only badges that were not already owned are expanded; removing a badge changes the byte and the counter only. Effects that later story overwrote in the played saves are not copied (the rules file lists them under `skipped` with the reason). `--raw` writes only the badge bytes. `--badge-rules` or `$HGSS_BADGE_RULES` overrides the file (searched beside the exe, `./`, `./registry`); a missing file exits 2 (`badge-rules-missing`).

### `story check INPUT [--badge-rules PATH] [--strict]`

Read-only audit of the badge cascade (`docs/archive/state-log.md` F8). For every owned badge, each flag/variable effect in `hgss-us.badge-rules.json` must hold, and each counter rule (`VAR_MIDGAME_BADGES`) must agree with the badge byte. Prints a `hgss-save.report/v1` report (`operation: story.check`) with `violation_count` and one entry per violation (`rule`, `symbol`, `id`, `expected`, `actual`, `because`). Exit 0 whether or not there are violations; with `--strict` violations exit 6. TM items are not audited because Gen 4 TMs are consumed on use (10 of the 29 census saves own a badge without its TM). On the census, all 29 played saves have 0 violations; a save built with `trainer badges set --raw` does not. It also reports `locked_field_moves` (moves whose badge is missing, `src/field_move.c`) and flags an HM grant flag whose item is not in the bag, or the reverse (`hm-grant`, `docs/archive/state-log.md` F12).

### `story build INPUT --johto MASK [--kanto MASK] --out OUT [--presets DIR] [--badge-rules PATH] [--warp-spawns PATH] [--safe-spawns PATH] [--force] [--strict]`
Builds a save for a requested badge combination (masks 0..255, default 0; `badges-range` exits 2). Of the presets for the input's game whose badges are a subset of the request, the one with the most badges is applied in full (a real played state, `docs/archive/state-log.md` F9), then `trainer badges set` with consequences adds the remaining badges (F8), then the result is audited as in `story check`. With no usable preset the input is the base. The report names `base_preset`, `violations`, `locked_field_moves` and a note that scene variables, NPC flags and HM grants for badges beyond the base are not modelled. `--strict` exits 6 on any violation. Output goes to `--out` (`--force` overwrites).

### `story scene list [--kind gym|e4|legendary|event] [--scenes PATH]`
Prints the scene registry (`registry/hgss-us.scenes.json`, `hgss-save.scenes/v1`, built by `tools/make_scenes.py`): one entry per story point with its id, title, kind, game, base preset, badge masks, map and warp index. The table is searched as `--scenes`, `$HGSS_SCENES`, beside the exe, `./`, `./registry`.

### `story scene apply ID INPUT --out OUT [--scenes PATH] [--presets DIR] [--badge-rules PATH] [--warp-spawns PATH] [--safe-spawns PATH] [--force] [--strict]`
Composes one scene onto any save of the scene's game, in this order: the scene's base preset (a whole played milestone, `docs/archive/state-log.md` F9), the scene's badge set with consequences (F8/F13), the scene's own flag/variable/item operations, then its `location.warp` (F5). The result is written to OUT and audited: badge bytes, the saved dynamic warp (map, index, tile), every `flags_on`/`flags_off` expectation, every `vars` value, every `items` quantity, and `story check` against the badge rules. The report (`operation: story.scene.apply`) carries a `checks` array and `passed`; exit 6 if any check fails, and `sources`/`note` explain each scene. 80 scenes ship: 32 gym before/after pairs (16 gyms), 7 Elite Four/champion, 13 legendary/Red, 28 events. Gym scenes clear the leader's own `FLAG_HIDE_*` for the "before" case, which is what the gym's init script does, then warp to the gym's entrance tile. The event scenes include the six rival sites, the Radio Tower and Rocket HQ executives, the five Kimono Girl battles and Elder Li; each carries the variable or hide flag its own script tests (F16). Two scenes are SoulSilver (`legendary-groudon`, `legendary-latias`): the room scripts hide those actors in HeartGold (`docs/archive/state-log.md` F17), so they need an SS input save. Three scenes record `expect.spawn_after` because their map moves the player on load: `e4-lance` (`scr_seq_0824_T10R0601.s:31-33`), `event-rival-mount-moon` (`scr_seq_0007_D02R0101.s:16`) and `event-kimono-battles` (`scr_seq_0928_T27R0501.s:721,911-917`). The save keeps the entry warp tile; the boot check asserts the post-load tile on the primary Location copy.

### `story triggers [--var NAME] [--map NAME|ID] [--scene-triggers PATH]`
Prints the cutscene and scene-machine triggers extracted from the decomp (`tools/scene_triggers.py`; 315 entries over 169 variables in `hgss-us.scene-triggers.json`). Each entry fires when `var == value`: `kind: coord` entries run when the player steps on the tiles `area` (x, z, w, h, y) of the map (zone-event `coords`), `kind: frame` entries run when the map's header on-frame table is evaluated (`InitScriptGoToIfEqual`, `at` gives file:line). `script` and `file` name the script; `writes` lists every `SetVar`/`AddVar`/`SubVar`/`SetFlag`/`ClearFlag`/`SetTrainerFlag`/`GiveBadge` reachable from it with `at` (file:line), which shows the variable's next values. `--var` and `--map` (symbol or numeric id) filter; with neither, all entries print. The table path is searched as `--scene-triggers`, `$HGSS_SCENE_TRIGGERS`, beside the exe, `./`, `./registry`. Errors: `triggers-missing`, `triggers-parse`, `triggers-schema` (exit 2).

### `preset list [--presets DIR]` and `preset apply NAME INPUT --out OUT [--presets DIR]`
A preset is a recipe (`registry/presets/NAME.json`, bundled beside the exe in `presets/`) generated by `tools/make_presets.py` from a real played milestone save. Applying one overwrites the whole story domain of INPUT with that save's state: every persistent flag (true or false) and every writable variable, plus badge bytes, the KeyItems and Machines (TM/HM) pockets, and a `location.warp` to a Pokemon Center door (or the city's first warp) so Continue spawns in a sensible place. It does not depend on INPUT's current progress. Left as INPUT has them: `FLAG_DAILY_*`, `FLAG_MAPTEMP_*`, flag `0x966`, `VAR_PLAYER_STARTER`, `VAR_MAGIKARP_SIZE_RECORD`, `VAR_LOTO_NUMBER_*`, `VAR_FRIENDSHIP_STEPS`, script temp vars, and everything outside event state (party, boxes, other bag pockets, dex, identity, money). The preset's `game` must match INPUT. The directory is searched as `--presets`, `$HGSS_PRESETS`, beside the exe, `./presets`, `./registry/presets`. `preset list` prints `index.json` (name, game, badges, source map, spawn). All common patch options apply (`--dry-run`, `--report`, `--force`, ...). Errors: `presets-missing`, `preset-name`, `preset-unknown` (exit 2).

### `dex set INPUT SPECIES --seen BOOL --caught BOOL --out OUT`
SPECIES 1..493. `--seen` and `--caught` are both required.

### `location set INPUT MAP X Y Z --out OUT [--safe-spawns PATH]`
Sets map id, tile X/Y, and elevation Z. Only exact tuples listed in `safe-spawns.json` are accepted, because walkability is not decoded; anything else exits 8. See `docs/archive/coverage-gaps.md`.

### `location warp INPUT MAP WARP --out OUT [--direction N] [--warp-spawns PATH]`
Teleports to warp event WARP of MAP by the game's own Continue path: sets flag `0x966` and writes the saved dynamic warp `{MAP, WARP}`. On the next Continue the game spawns the player on that warp event's tile and builds the map's actors from zone-event data. `--direction` is 0..3, default 1 (the only value observed in saves). Only (MAP, WARP) pairs listed in `hgss-us.warp-spawns.json` are accepted; others exit 8 (`warp-unverified`). The stored x/y are the warp table's tile, informational only: the game recomputes them from the warp at load. Runtime-verified on 7 of 7 sampled targets (`docs/archive/state-log.md` F5), and again on the Latios and Latias scenes.

### `location warp INPUT MAP -1 --x=X --y=Y --out OUT [--direction N]`
Free spawn on an arbitrary tile: writes the dynamic warp with `warpId = -1`, which makes the game keep the stored coordinates instead of replacing them from a warp event (`src/field_warp_tasks.c:206-209`; the game uses the same path for the Union Room exit at `:380-382`). MAP must resolve to a map symbol, otherwise the command exits 8. The tile is not checked for walkability and the map's own init script may still move the player (`:205`), so a free spawn is a claim to verify in-game, not a guarantee. Pass `--x`/`--y` with `=`. Recipes use the same op with `{"op":"location.warp","map":M,"warp":-1,"x":X,"y":Y}`. Runtime-verified by the free-spawn legendary scene runs (Suicune, Articuno, Zapdos, Moltres, Mewtwo, Latios, Latias; `docs/archive/runtime-results.md`, F19). Ho-Oh and Lugia were reached from their scene warps and a Surf-capable party, not from a free spawn.

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
| `trainer.badges.set` | `johto` and/or `kanto`, each 0..255; optional `consequences` (bool, default false in recipes: badge bytes only) |
| `inventory.quantity.set` | `item`, `quantity`, optional `expect` |
| `party.slot.import` | `slot`, `file`, optional `expect_species`, `update_dex` |
| `party.slot.clear` | `slot` |
| `box.slot.import` | `box`, `slot`, `file`, optional `expect_species`, `update_dex` |
| `box.slot.clear` | `box`, `slot` |
| `location.set` | `map`, `x`, `y`, `z` |
| `location.warp` | `map`, `warp`, optional `direction` (0..3, default 1), optional `x`/`y` (only when `warp` is `-1`) |
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
