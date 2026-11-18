# Documentation index

Project: `hgss-save`, a CLI for reading and generating US Pokémon HeartGold/SoulSilver saves for regression testing the Kohjoh port. The research behind it answers one question: what does a save have to contain for the game to behave as if the player reached a given story point by play.

## Start here

| If you want to | Read |
|---|---|
| Understand the project in five minutes | this page, then `../README.md` |
| Know how the save file is laid out | `save-format.md` |
| Know what the flags, variables and badges mean and why a single edit is not enough | `story-state-model.md` |
| Find a specific result (F5, F14, ...) | `findings.md` |
| See which of the original research questions are answered | `research-questions.md` |
| Know what is in the corpus, the presets and the 80 scenes | `corpus.md` |
| Know what each generated file and script is | `artifacts-and-tools.md` |
| Know how a save is proven to work, and the pitfalls | `verification.md` |
| See what is unfinished or was once claimed wrongly | `open-questions.md` |
| Run a command | `cli-reference.md` (same text as `hgss-save --help`) |

## Reading order for a newcomer

1. `story-state-model.md` sections 1-4 for the shape of the problem.
2. `save-format.md` sections 1-6 for where the data sits.
3. `corpus.md` for what the conclusions are based on.
4. `findings.md`, then the entries in `archive/state-log.md` it points to.
5. `verification.md` before touching the runtime tools.

## All documents

### Synthesized reference (read these)

| File | Content |
|---|---|
| `save-format.md` | Byte-level format: partitions, footers, checksums, block selection, general block map, flags and variables encoding, location structures, bag, storage, containers |
| `story-state-model.md` | How progress is stored and consumed: flags that clear, badge dependency layers, triggers, actor visibility, unlock chains, what the corpus can and cannot separate, the walkability problem |
| `findings.md` | Index of every numbered finding with result, evidence kind and status |
| `research-questions.md` | The plan's 98 question IDs with the answer or "not investigated" |
| `corpus.md` | The 29 saves, 18 presets, 80 scenes, 30 fixtures, and coverage gaps |
| `artifacts-and-tools.md` | Every file in `registry/`, `research/`, `fixtures/` and every script in `tools/` |
| `verification.md` | Four verification levels, the headless harness, hard-won lessons |
| `open-questions.md` | Open work, deliberate refusals, corrections log |

### Live files outside the synthesized set

| File | Content |
|---|---|
| `cli-reference.md` | Every command, option, exit code, recipe operation. Same text as `hgss-save --help` |
| `scene-boot-matrix.md` | Generated per-scene boot results. Regenerate with `tools/scene_boot_matrix.py`; do not edit by hand |
| `../README.md` | Build, commands, safety model, symbol registry, patch recipes |
| `../AGENTS.md` | Writing and commit rules for work in this repository |

### Archive (`archive/`)

Original working documents, kept unchanged apart from path updates. They hold the full accounts that the synthesized docs summarize. The registry, tool docstrings and source comments cite them by path and finding number (`docs/archive/state-log.md F14`), so do not renumber.

| File | Content |
|---|---|
| `archive/state-log.md` | Findings F1-F23 in order, each with its evidence |
| `archive/research-findings.md` | Answers to the plan: Lake of Rage gate, interior structs, Surf synth, Olivine, Mt. Silver chain |
| `archive/story-state-field-knowledge.md` | Live RAM struct, containers, issue log, deliberate refusals, Lake of Rage trek and play-to-fill notes (sections 7-9 are a navigation log) |
| `archive/runtime-results.md` | Boot matrix, Continue sequences, legendary and Red encounter proofs |
| `archive/session-log.md` | Dated session records: decisions, mistakes, recoveries |
| `archive/coverage-matrix.md`, `archive/coverage-gaps.md` | Axes the regression suite needs and what exists; `coverage-gaps.md` opens with a correction |
| `archive/runtime-validation.md` | Early manual melonDS checklist, partly superseded by `verification.md` |
| `archive/SAV4HGSS.md` | Original format walkthrough, superseded by `save-format.md` |
| `archive/hgss_save_state_research_plan.md` | The research plan: evidence hierarchy, the 98 questions, experiment templates, definition of done |
| `archive/hgss_save_cli_engineering_design.md` | CLI design: scope, dependencies, patch format, safety levels, risks |

Raw agent transcripts `chatlog00.*` and `chatlog01.md` sit at the repository root, git-ignored, for provenance only.

### Where new material goes

New findings: add a row to `findings.md` with the full account under a new heading in that file (the archive is closed). New runtime proofs: `verification.md`. New open items and corrections: `open-questions.md`. Question status changes: `research-questions.md`.

## Vocabulary

| Term | Meaning |
|---|---|
| Partition | One of the two `0x40000` halves of the raw save |
| General block | The `0xF628`-byte block with trainer, party, bag, variables, flags and position |
| Flag, variable | A bit or a u16 in the save that scripts read and write. `FLAG_*` and `VAR_*` names come from the decomp |
| `FLAG_HIDE_*` | Actor visibility: set means the object is hidden |
| `FLAG_UNK_*`, `VAR_UNK_*` | Symbols the decomp has not named. They are used, not unused |
| Scene variable | A `VAR_SCENE_*` or `VAR_UNK_*` that stages a cutscene sequence |
| Location | Five ints: map, warp, x, y, direction. The save holds five; the game holds a live one in RAM |
| Dynamic warp, flag `0x966` | Continue spawns the player on a warp tile when this flag is set |
| Census | `research/corpus-census.json`, the decoded corpus |
| Preset | A whole played milestone applied absolutely over a save |
| Scene | A named story point: preset, badges with consequences, scene writes, warp |
| Fixture | A curated recipe from a played base save, with a manifest and runtime record |
| Level A-D | Structural, invariant, story and runtime verification |
| `.mcpr` | A melonDS savestate. Not a `.sav` |
| F1-F23 | Findings in `archive/state-log.md` |
| Decomp | `pret/pokeheartgold`, pinned at `4e95633` |

## Conventions

- Every claim cites a source file and line, a corpus measurement, or a runtime observation. If a statement has none, treat it as unverified.
- Evidence kinds: SOURCE, SAVE_DIFF, INTERVENTION, RUNTIME, NEGATIVE.
- Original saves and ROMs are never modified or committed.
- When a doc is wrong, the correction goes in the document that stated it and in `open-questions.md`.
