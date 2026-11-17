namespace HgssSave.Cli;

/// <summary>
/// Full command reference printed by <c>hgss-save --help</c>. Kept in step with docs/cli-reference.md;
/// every option here corresponds to an <c>Args.Option</c>/<c>Args.Flag</c> read in Commands.cs.
/// </summary>
internal static class HelpText
{
    internal const string Full = """
hgss-save: HeartGold/SoulSilver raw-save inspector and transactional patcher (PKHeX.Core engine).

USAGE
  hgss-save COMMAND [ARGS] [OPTIONS]
  hgss-save --help | -h | help       print this reference (any command followed by --help does too)

Stdout is one JSON document (schema hgss-save.report/v1); diagnostics go to stderr.
Exit codes: 0 ok, 2 bad arguments, 3 unsupported save, 4 structural failure, 5 symbol failure,
            6 precondition failure, 7 output conflict / I/O error, 8 unsafe operation, 9 internal error.
Writing commands never edit in place: --out must be a new path (or pass --force to overwrite it),
and a patch that cannot fully apply exits non-zero without writing anything.
Flag/var/item IDs accept a symbol name, decimal, or 0x-hex. All other numbers are decimal. Booleans are true|false|1|0.

READ-ONLY COMMANDS
  version [--symbols PATH]
  inspect INPUT [--symbols PATH] [--flags-limit N]       identity, trainer, party, boxes, bag, set flags (default 200)
  validate INPUT [--level structural] [--symbols PATH]   checksums and block integrity only
  diff BEFORE AFTER [--max-list N] [--max-ranges N] [--symbols PATH]
                                                         semantic and byte-range diff (defaults 200 and 40)
  flag get INPUT ID [--symbols PATH]                     ID: FLAG_* name or number
  var get INPUT ID [--symbols PATH]                      ID: VAR_* name or number
  flags list INPUT [--limit N] [--symbols PATH]          all set persistent flags (default 5000)
  vars list INPUT [--limit N] [--symbols PATH]           all non-zero save variables (default 500)
  party export INPUT SLOT --out FILE.pk4 [--force]       SLOT 0..5
  box export INPUT BOX SLOT --out FILE.pk4 [--force]     BOX 0..17, SLOT 0..29

WRITING COMMANDS (all require --out OUT.sav; all accept the common patch options below)
  patch INPUT --recipe PATCH.json --out OUT.sav          apply a hgss-save.patch/v1 recipe
  flag set INPUT ID true|false --out OUT [--expect true|false]
  var set INPUT ID VALUE --out OUT [--expect N]          VALUE 0..65535
  bag set INPUT ITEM QUANTITY --out OUT [--expect N]     ITEM: ITEM_* name or number; QUANTITY 0..999
  trainer money set INPUT VALUE --out OUT [--expect N]
  dex set INPUT SPECIES --seen BOOL --caught BOOL --out OUT   SPECIES 1..493
  location set INPUT MAP X Y Z --out OUT [--safe-spawns PATH] refuses tiles not in safe-spawns.json
  location warp INPUT MAP WARP --out OUT [--direction 0..3] [--warp-spawns PATH]
                                                       teleport to warp event WARP of MAP (sets flag 0x966 and the
                                                       saved dynamic warp; Continue then spawns on that warp tile)
  party import INPUT SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]
  party clear INPUT SLOT --out OUT
  box import INPUT BOX SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]
  box clear INPUT BOX SLOT --out OUT

  Common patch options:
    --out PATH            output save (always required; with --dry-run nothing is written)
    --force               overwrite an existing --out
    --dry-run             plan and report only; write nothing
    --require-symbols     reject numeric ids that have no matching registry symbol
    --max-ranges N        cap on byte ranges listed in the audit report (default 40)
    --safe-spawns PATH    safe-spawn list for location.set (or env HGSS_SAFE_SPAWNS)
    --warp-spawns PATH    warp table for location.warp (or env HGSS_WARP_SPAWNS)
    --symbols PATH        symbol registry (or env HGSS_SYMBOLS)
    --expect VALUE        refuse unless the field currently holds VALUE (flag/var/bag/money)

SYMBOL AND SCRIPT-INDEX COMMANDS
  symbols lookup NAME [--kind flag|var|map|item|species|move] [--symbols PATH]
  symbols search QUERY [--kind K1,K2] [--limit N] [--symbols PATH]       default limit 50
  symbols build-registry --headers DIR --commit SHA [--out PATH] [--registry-id ID] [--project NAME]
                                                         defaults: hgss-us.symbols.json, hgss-us, pokeheartgold
  script index --scripts DIR --commit SHA [--out PATH] [--project NAME]
                                                         DIR: decomp checkout or its scr_seq directory
  script references SYMBOL [--script-refs PATH] [--limit N]              default limit 100; env HGSS_SCRIPT_REFS

FIXTURES
  fixture build RECIPE.json --outdir DIR [--bases DIR] [--force] [--max-ranges N] [--safe-spawns PATH] [--symbols PATH]
  fixture verify MANIFEST.json [--outdir DIR]
  fixture record-runtime MANIFEST.json --report REPORT.json              report from tools/leveld_run.py

CONTAINERS AND BLANK SAVES
  container probe FILE                                   classify raw / DeSmuME .dsv / other
  container unwrap FILE --out OUT [--force] [--symbols PATH]
  blank --game HG|SS --out OUT [--kind fresh-structure|erased-flash] [--trainer-name N] [--tid N] [--sid N]
        [--gender 0|1] [--language 1..8, default 2] [--force] [--symbols PATH]

SYMBOL REGISTRY LOOKUP ORDER
  --symbols PATH, $HGSS_SYMBOLS, hgss-us.symbols.json beside the executable, ./hgss-us.symbols.json,
  ./registry/hgss-us.symbols.json. Without a registry numeric ids still work; names are unavailable.

See docs/cli-reference.md for details and examples.
""";
}
