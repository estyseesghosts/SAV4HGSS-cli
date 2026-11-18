# How story state works in HeartGold/SoulSilver

What the game stores for story progress, how its scripts read and write it, and what that means for generating a save at an arbitrary story point. `docs/save-format.md` covers the bytes; this file covers the meaning. Finding numbers (F1-F18) refer to `docs/archive/state-log.md`; the evidence behind each is listed in `docs/findings.md`.

Evidence labels used throughout: **SOURCE** (decomp line), **SAVE_DIFF** (compared real saves), **RUNTIME** (observed in the emulator), **INTERVENTION** (edited copy against a control). The definitions are in `docs/archive/hgss_save_state_research_plan.md` §2.

## 1. Story progress is several mechanisms, not one number

A story point is the combination of:

1. **Badge bytes** (Johto and Kanto masks).
2. **Persistent flags** (2,912 bits): completion (`FLAG_GOT_TM51_FROM_FALKNER`), actor visibility (`FLAG_HIDE_*`), trainer defeats, item pickups, legendary catches, unlocks.
3. **Save variables** (320 writable): scene stages (`VAR_SCENE_*`), counters, and the many `VAR_UNK_*` that scripts use as small state machines.
4. **Items** in the bag, which gate some scripts and are granted by others.
5. **Position state**: the current location, the dynamic warp, spawn points and the saved map objects.
6. **Per-map init scripts**, which run on every entry and can rewrite flags, move the player and decide which actors exist (section 6).

Setting one of these without the others produces states the game never creates. The project's working rule is that a generated save must be reachable by play, and the tool refuses writes that fabricate unreachable states (`docs/archive/story-state-field-knowledge.md` §6).

## 2. Flags and variables turn off as well as on

SAVE_DIFF on consecutive HG saves from one playthrough (`research/lineage-HG-Master.md`) shows 2 to 9 flags clearing at every step, alongside 7 to 65 setting (F1). Almost all clearing flags are `FLAG_HIDE_*`, where *set means hidden*, so clearing makes an NPC or object appear (`FLAG_HIDE_JASMINE_IN_GYM` clears when Jasmine becomes available). Other flags that clear: `FLAG_ROCKET_TAKEOVER_ACTIVE`, `FLAG_AZALEA_ROCKET_HARASSING_CIVILIAN`, `FLAG_DAILY_*`, and several `FLAG_UNK_*`. After the Hall of Fame the league resets: `FLAG_DEFEATED_WILL/KOGA/BRUNO/KAREN` clear (25 flags clear on the SS Elite Four step).

A "set progress to X" feature that only adds flags would leave NPCs hidden that the game expects visible. Presets therefore set and clear every persistent flag. The corpus cannot show which clears are story and which are player choice (section 9).

Flag families across the corpus (F2): `FLAG_HIDE_*` 5,270 set instances, trainer-defeated 2,154, `FLAG_UNK_*` 1,430 (184 distinct ids), `FLAG_GOT_*` 643, `FLAG_CAUGHT_*` 47, hidden items 46, `FLAG_DEFEATED_*` 12, daily 7.

## 3. Badges are the hub

Writing only the badge byte leaves the save incoherent. Three layers hang off it (F8, checked against the 29 saves):

**Gym-script consequences.** After `GiveBadge`, each gym script sets flags and variables. `research/badge-effects.json` lists them with file and line; `tools/make_badge_rules.py` classifies each against the corpus as `confirmed` (holds in every save owning the badge, absent in some without), `source-only` (no corpus save owns the badge) or `skipped` (later story overwrote it, so the value at the badge is not the final value). Examples confirmed in all saves with the badge: Zephyr sets `FLAG_GOT_TM51_FROM_FALKNER`, `FLAG_UNK_192`, `FLAG_UNK_0F4`, `FLAG_HIDE_WIFI_CLUB_CLOSED_LADY` and two Bird Keeper trainer flags (27/27); Fog sets `FLAG_GOT_TM30_FROM_MORTY` (20/20); Hive sets four trainer flags (24/24). The result is `registry/hgss-us.badge-rules.json` (16 badge entries, 1 counter, 4 grants, 8 field gates).

**A derived counter.** `VAR_MIDGAME_BADGES` (`0x4134`) equals the number of Fog, Storm, Mineral and Glacier badges while that is below 4 (29/29 saves). When it reaches 4, map header scripts (`scr_seq_0684_T26_hdr.s:13`, `scr_seq_0703_T28_hdr.s:12`, `scr_seq_0875_T24.s:46`) start the Rocket takeover and set the counter to 5 (`scr_seq_0875_T24.s:86`). Every save with all four badges holds 5. RUNTIME (F10): with the counter at 4 and the player warped into Mahogany, Elm's phone call about the radio broadcasts plays; with the counter left at 3, nothing happens. The counter is load-bearing: a raw badge write (`--raw`) can produce a save in which the takeover never starts.

**Scene machines.** `VAR_SCENE_ROCKET_TAKEOVER` is 0, 1 (three midgame badges), 2 or 4 (takeover in progress) and 5 (done) in the corpus. Value 3 is written by a Goldenrod Tunnel script (F11) and does not appear in a corpus save. Writers: Cianwood and its gym, Olivine and its gym, Mahogany and its gym, Radio Tower 1F, the Observation Deck, Goldenrod Tunnel B1F. Readers: Goldenrod (city, Pokémon Center, department store, Radio Tower, tunnel, warehouse), Mahogany and its shop, Blackthorn, Kurt's house. Other cross-map gates found the same way: `FLAG_GOT_SECRETPOTION` (written in Cianwood Pharmacy, read in the Olivine Lighthouse), `VAR_SCENE_LIGHTHOUSE_JASMINE` (written in Olivine, read in Cianwood and Olivine), `FLAG_BEAT_AZALEA_ROCKETS` (written in Slowpoke Well B1F, read by Azalea, the Charcoal Kiln, Kurt's house, Ilex Forest), `FLAG_GAME_CLEAR` (read by five gym scripts for postgame behaviour).

**HM grants and field gates (F12).** Four scripts set an HM flag and give the item together (`FLAG_GOT_HM01` Ilex Forest, `HM02` Cianwood, `HM03` Ecruteak Dance Theater, `HM08` Oak). Flag and item agree in 29 of 29 saves, so an HM flag without its item is an inconsistency, unlike TMs, which are consumed. Field moves require a badge in `src/field_move.c`: Cut/Hive, Fly/Storm, Surf/Fog, Strength/Plain, Rock Smash/Zephyr, Waterfall/Rising, Rock Climb/Earth, Whirlpool/Glacier. A missing badge is not an inconsistency, so `story check` lists these as informational `locked_field_moves`.

## 4. The scripts that award progress do not check story order (F14)

`tools/script_guards.py` walked every script entry through GoTo/GoToIf/CallIf edges and recorded the flag and variable conditions on the path to every state write (`research/script-guards.json`: 497 scripts, 1,869 distinct writes, 6,650 guarded paths).

All 16 `GiveBadge` paths have no story guard once battle results and temp vars are dropped. `FLAG_GOT_HM02` and `FLAG_GOT_HM03` are unguarded. HM08 tests `VAR_UNK_4096 == 2`, but the one save that owns HM08 has that variable at 0 because a later script resets it, so a scene variable cannot be audited from end state. TM givers test only `VAR_MIDGAME_BADGES` and `VAR_SCENE_ROCKET_TAKEOVER != 4` (TM11).

So order is enforced by the world, not the awarding scripts: which map is reachable, which NPC blocks a path, which frame or coordinate trigger fires. Prerequisites for an arbitrary badge set cannot be read from gym scripts. The earlier `needs` edges in `research/story-atoms-hg.json` are file-level reads, not guards, and should not be used as prerequisites.

## 5. Triggers: how scenes start

Two kinds, both "when VAR == value" (F11, `tools/scene_triggers.py`, `registry/hgss-us.scene-triggers.json`):

- **Coordinate triggers**: `zone_event` `coords` entries run a script when the player steps on a tile rectangle while a variable has a value. 187 of them.
- **Frame triggers**: map header on-frame tables (`InitScriptGoToIfEqual`) run when the map loads or a frame passes. 128 of them.

315 triggers over 169 variables (27 are script temp vars). Each lists the script and everything it can write. `hgss-save story triggers --var V --map M` queries them.

To stage a cutscene: set its variable, put the player on its map. Frame triggers fire on load. Coordinate triggers need the player to step on the area, and the tile coordinates are tile data, so reaching one from a warp tile needs walking. That is not automated.

Worked example, the takeover chain: Mahogany's header sets `VAR_SCENE_ROCKET_TAKEOVER` to 2 when the counter is 4; stepping onto Goldenrod Tunnel B1F with the variable at 2 runs a script that sets 3 (`scr_seq_0094_D37R0102.s:695`); the Radio Tower 1F trigger at 3 sets 4 or 2 (`scr_seq_0029_D23R0101.s:457,463`).

## 6. Actor visibility is decided by the map, not by the save alone (F17)

Clearing a `FLAG_HIDE_*` is necessary and not sufficient. A map's init table (`scr_seq_####_<TAG>_hdr.s`: `InitScriptEntry_OnLoad`, `OnTransition`, `OnFrameTable`) runs on entry and may set the flag again, gate the actor on a variable, or move the player. `tools/scene_actor_check.py` walks those scripts and records every write to an actor's hide flag with its path conditions (`research/scene-actor-gates.json`).

Findings:

- Mewtwo, Articuno, Moltres, Zapdos, Lugia, Ho-Oh and Suicune are hidden only while `FLAG_ENGAGING_STATIC_POKEMON` is set. Scenes clear that flag and the hide flag.
- The game itself clears most of the Kanto legendary hide flags in the post-League script (`scr_seq_0825_T10R0701.s:118-150`). Zapdos needs `BADGE_EARTH` and Suicune needs `VAR_SCENE_ROUTE_25 == 3`. This is why Kanto legendary scenes build on the 16-badge state.
- Version locks: Kyogre is HG-only, Groudon SS-only, Latios in HG and Latias in SS (`include/config.h:9-10`, `scr_seq_0134_D52R0102.s:20-26`, `scr_seq_0133_D52R0101.s:20-26`, `scr_seq_0750_T03.s:238-245`). Rayquaza is shown while `VAR_SCENE_EMBEDDED_TOWER < 7`. The orb must be in the bag.
- The Latis actor shows only for `VAR_UNK_4083` 2..4; base presets have 0, so scenes set 3.
- 22 of 80 scenes have a map-init script that moves the player. Three fire: Lance's room (the player walks from (6,28) to (6,22)), Mt. Moon (4,4 to 5,4) and the Kimono Girls' theater (7,17 to 7,7). The save keeps the entry tile, which is what the warp needs; the scene records the post-load tile as `expect.spawn_after`.
- Silph Co.'s Rotom room cannot be produced by flags: its script sets all appliance hide flags on every transition and clears only forms the player owns (`scr_seq_0838_T11R0702.s:17-34`). Rotom needs party data, so there is no Rotom scene.
- Known limitation: the checker over-reports one write. It does not model `CheckBattleWon`, so Mt. Moon's post-battle `SetFlag FLAG_UNK_25C` is attributed to map init.

## 7. Position and Continue

See `docs/save-format.md` §6 for the structures. The behaviours that matter:

- `location set` writes only `currentPosition`, and only to tiles in `fixtures/safe-spawns.json` (20 tiles seen in real saves). Walkability per tile is not decoded (section 10).
- `location warp MAP WARP` sets flag `0x966` and the saved dynamic warp. On Continue the game resolves the warp to its event tile and builds actors from zone-event data (F5). RUNTIME: a hand-edited warp with deliberately wrong x/y landed on the table's tile; 6 of 6 random (map, warp) targets landed exactly; 80 scene spawns use it. The destination is a warp event tile (a door, stairs or map exit), never an arbitrary tile. The `registry/hgss-us.warp-spawns.json` table has 1,317 such tiles over 540 maps.
- In saves, flag `0x966` is set once (HG Master right after the Hall of Fame), with the New Bark player room as dynamic warp. The final SS save carries the same dynamic warp with the flag clear, as expected after one Continue.
- In the live RAM struct the warp field depends on the map: outdoor Ecruteak shows `0xFFFFFFFF`, while Route 42, Mt. Mortar and interiors show warp ids such as 0 (`docs/archive/research-findings.md`, `docs/archive/session-log.md`).

## 8. Multi-step unlock chains: Mt. Silver and Red (F18, STORY-08)

Unlocking Red is a five-write chain from Blue's gym, not one flag (SOURCE, all in `pokeheartgold@4e95633`):

1. Beating Blue sets `VAR_UNK_4096 = 1` (`scr_seq_0743_T02GYM0101.s:65`).
2. Viridian's header runs `scr_seq_T02_011`: it sets `FLAG_UNLOCKED_WEST_KANTO`, `VAR_UNK_4121 = 1`, calls Oak and sets `VAR_UNK_4096 = 2` (`scr_seq_0741_T02.s:42-51`).
3. Talking to Oak at 2 gives `ITEM_HM08`, sets `VAR_UNK_4110 = 2`, `FLAG_UNLOCKED_MT_SILVER`, `FLAG_GOT_HM08` (`scr_seq_0740_T01R0301.s:95-96,198-208`).
4. Route 22's two guards: the west guard's coordinate script runs while `VAR_UNK_4121 == 0`, the north one while `VAR_UNK_4110 == 1` (`zone_event/270_R22R0101.json`).
5. Red is placed by the summit's zone event with hide flag `FLAG_UNK_25F`; his script has no condition (`scr_seq_0107_D41R0108.s:13-19`).

The corpus cannot show this chain (Kanto badge counts are 0 and 255 only; the 16-badge SS save confirms the end state). The intermediate values come from source. An earlier version of the Red scenes wrote only `FLAG_UNLOCKED_MT_SILVER`, a state the game never produces; both scenes now write the whole chain.

## 9. What the corpus can and cannot separate

HG and SS share a story core. At 4 badges, HG has 268 flags set and SS 284, with 252 in common (F3). The differences are optional state: item balls, a few trainers, side events (`FLAG_GOT_OLD_ROD`, `FLAG_GOT_APRICORN_BOX`), version-exclusive story (`FLAG_CAUGHT_LUGIA` in SS, Ho-Oh in HG), and one real actor difference (`FLAG_HIDE_VIOLET_GYM_GYM_GUY_BEFORE_SPROUT` versus `_AFTER_SPROUT`). At 1, 2, 4 and 8 Johto badges the HG and SS flag sets share 171/192/252/363 flags.

The HG corpus is one playthrough. It cannot separate story-required clears from choices that differed between playthroughs. That is why presets are absolute, and why no set-only preset is shipped. Closing it needs a second HG playthrough dumped at the same milestones (`docs/open-questions.md`).

State replay works. `tools/state_delta.py` turns two corpus saves into a recipe of flag sets and clears plus variable writes. Replaying all 10 consecutive HG steps (1 badge through post-Elite-Four) left 0 differing flags; the remaining differences are temp var `0x4000`, `VAR_LOTO_NUMBER_LO` and `VAR_FRIENDSHIP_STEPS` (F7). A replay is only as good as its milestones: replaying 1-badge flags up to 8 badges while keeping the 1-badge byte boots but holds the 8-badge story with one badge (F9), which is the disagreement section 3 describes.

Unnamed flags that change between consecutive HG milestones (`tools/unk_flags.py`), for the steps 1→2 badges through post-Elite-Four→Kanto: 10, 9, 15, 6, 6, 13, 13, 7, 2 and 30. A cluster at `0x96x-0x99x` and `0xAA5` moves with warps and play time rather than story; `FLAG_UNK_966` is the Continue-at-dynamic-warp flag. The rest (for example `FLAG_UNK_0B7`, `_0C5`, `_103`, `_1BF`-`_1C5`, `_1E5`-`_1FE`, `_25C`-`_27F`) are real story writes without names.

## 10. Walkability and reachability (F15, unresolved)

The aim was to derive which field-move gates each map needs, since gym scripts do not encode order (section 4). A pipeline exists: `tools/nds_files.py` reads the ROM filesystem and NARCs, `tools/map_data.py` loads the overworld map matrix and the land-data tile attributes (u16 at member offset `0x14`, bit 15 = blocked, low byte = behaviour), `tools/map_reach.py` floods the 47x17-block overworld (503,808 tiles).

What checked out: row-major tile order, behaviour ids (`WATER_SEA` = `0x15`), and bit 15 as "blocked" (of 26 distinct played positions, 16 are on overworld tiles with bit 15 clear, 7 are interiors, 3 are entrance tiles).

What fails: with doors, ledges and water all free, the tiles split into 521 components of at most 8,290 tiles. Cherrygrove and New Bark form one component, separate from Violet, Azalea, Goldenrod, Ecruteak and Cianwood. The Route 30 corridor dead-ends against a block of `0x8006` tiles that the zone events do not mention. The join between map blocks is not in the tile attributes as read. Candidates: script-driven matrix edits, the object/model section of the land-data member, or a loader detail not in `src/map_matrix.c`. Until the 26 played positions are reachable, no per-gym gate set is shipped, and `location set` stays allowlisted.
