# Runtime validation (Level D) — melonDS DS mode

> **Partly superseded.** The statement below that there is no headless test mode is out of date: `tools/leveld_run.py` boots saves headless. See [verification.md](../verification.md) for the current process and [runtime-results.md](runtime-results.md) for results. The manual save-and-diff steps and failure triage here still apply.

Structural validity is not story validity. A fixture passes Level D only after
it boots in melonDS, performs its event, and saves back to a parseable file.
`hgss-save validate` reports `"semantic": "not-evaluated"` for exactly this reason.

## melonDS 1.1 setup (Windows, DS mode)

1. Use the GUI build at `3DS/melonDS-1.1-windows-x86_64/melonDS.exe`. CLI flags
   only select ROM/boot behavior (`--boot auto`); there is no headless test mode,
   so Level D stays a manual checklist per fixture (design §9).
2. Place the real SoulSilver ROM where melonDS can read it. Keep ROMs out of this repo.
3. Stage the fixture save next to the ROM with the matching basename:
   `soulsilver.nds` + `soulsilver.sav`. melonDS loads `<romname>.sav` from the ROM
   directory by default. Back up any existing `.sav` first.
4. Boot with firmware on auto. If the title screen halts, confirm DS mode and a
   valid firmware/BIOS path before blaming the save.

## Per-fixture run

1. Build: `hgss-save fixture build fixtures/recipes/<id>.json --bases <your-checkpoint-dir> --outdir <out>`.
2. Verify: `hgss-save fixture verify <out>/<id>.manifest.json --outdir <out>`.
3. Copy `<out>/<id>.sav` over the staged `<romname>.sav`.
4. Boot, run the recipe's `runtime.checkpoints` in order:
   - `ss-violet-safe-money`: Violet spawn visible, menu opens, in-game save works.
   - `ss-azalea-rockets-verify`: Slowpoke Well resolved, no repeated Rocket trigger.
   - `ss-lugia-pre-event-stage`: travel to Whirl Islands in-game, confirm Lugia
     actor present and Silver Wing in bag, do not save inside the event script.
5. Save in-game, copy the resulting `.sav` back, and re-run:
   `hgss-save inspect <resaved>.sav` and `hgss-save diff <fixture>.sav <resaved>.sav`.
   Expected: only playtime/RTC-adjacent bytes plus event effects from the manifest.
6. Record the outcome in the manifest's `runtime_result` (pass/fail + melonDS
   version + notes). Do not mark `semantic: pass` on CRC alone.

## Failure triage

- Black screen on boot: wrong basename, truncated `.sav`, or DSi-mode boot.
  Re-verify size `0x80000` and `validate` first.
- Event does not trigger: base too far from the event, missing Kimono Girls or
  bell/wing branch, or teleport used. Return to the verified safe spawn and walk
  the intended path; see `fixtures/safe-spawns.json`.
- Re-save drops party/box edits: import used a corrupt template or dex sync was
  disabled. Re-import from an exported real-slot template and keep `update_dex`
  enabled unless the recipe says otherwise.
