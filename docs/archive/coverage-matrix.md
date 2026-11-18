# Coverage matrix: what regression needs, what exists, how to fill the rest

Goal: synthetic saves at any story point, across the axes the games
actually vary on: **game** (HG/SS), **hero** (Ethan/Lyra), **starter**
(Totodile/Cyndaquil/Chikorita), **16 gyms pre/post**, **E4/Blue/Red
pre/post**, **every legendary pre/post**, plus event movies.

## Axis 1 — game and hero (covered, record don't repeat)

| Base family | Game | Hero | Gender | Lineage position |
| --- | --- | --- | --- | --- |
| M@ster SS chain (1,2,7,8,16gyns, pose4, before-wing) | SS | Ethan-type | 0 | full Johto + postgame |
| Master HG chain (1-8gyn, E4, posE4, Celebi) | HG | Ethan-type | 0 | full Johto + postgame |
| Lyra event saves (Celebi, Arceus, Pichu, Enigma, Rotom) | HG | Lyra-type | 1 | event pre-states |
| NotKris Cherrygrove + Ash Cherrygrove | SS / HG | Lyra / Ethan | 1 / 0 | 0-badge starts |
| Desmond Dragons Den | HG | Ethan-type | 0 | postgame shrine |

Both games and both hero genders are staged. New fixtures reuse
these heroes; no new hero playthrough is needed unless a script reads
hero gender at the staged beat (none found so far).

## Axis 2 — starter (Cyndaquil + Totodile covered, Chikorita open)

`VAR_PLAYER_STARTER` reads live: 155 Cyndaquil on every M@ster/Master/
Ash/Desmond base, 158 Totodile on Lyra/NotKris. **No base chose 152
Chikorita.** Starter choice affects the Elm-lab scripts, the rival's
team (advantage matchup), and early-game party content — it cannot be
edited in (rival team isn't derivable from the flag). Fill method:
**play** — scripted new-game intro on one ROM (title → Oak → default
name → lab → take Chikorita → in-game save at Cherrygrove), all
headless, steered by screenshots. Queued, not yet run.

## Axis 3 — 16 gyms pre/post (Johto done, Kanto done)

All 8 Johto gyms have per-gym fixtures on both lineages (Falkner,
Bugsy, Whitney, Morty, Chuck, Jasmine, Pryce via TM ladders; Clair via
Blackthorn/Clair verify). All 7 Kanto gyms are asserted on the 16gyns
base (kanto-gyms-verify). No play needed.

## Axis 4 — E4 / Blue / Red (E4 both sides done, Blue + Red open)

- E4: pre-challenge (HG E4 base, flags clear) + fresh Champion (SS
  pose4, flags set). Done.
- Red: beaten on 16gyns (`FLAG_UNK_25F` set by
  `scr_seq_0107_D41R0108.s:26`). Pre-Red needs a base from before
  that fight — none on disk. Fill method: **play** — continue the
  Kanto-states line toward Mt Silver in-emulator, or check whether HG
  Celebi postgame base (E4 beaten, Red unknown) is pre-Red: read
  `FLAG_UNK_25F` on it. If clear, stage pre-Red there with zero play.
- Blue (Viridian gym, counts as Kanto gym 8): covered by TM92 assert
  in kanto-gyms-verify (post). Pre-Blue needs a Kanto-mid base; none
  exists. Fill method: **play** forward from the gatehouse base is
  long; cheaper is a League-run from a nearer future base. Open.

## Axis 5 — legendaries pre/post

| Legendary | Pre-state fixture | Post-state evidence |
| --- | --- | --- |
| Raikou / Entei | Burned Tower pre (dogs present, roamers 0) | 4gyn base (hides set) |
| Suicune | legendary-verify (caught flag asserted true postgame) | same |
| Lugia | lugia-pre-event-stage | SS 8gyn base (caught) |
| Ho-Oh | ho-oh-rebattle-stage | 16gyns base (caught) |
| Birds | legendary-verify (all caught) | same |
| Mewtwo | — | 16gyns (caught, hide set). Pre-state needs Cerulean Cave approach; open |
| Kyogre (SS) | kyogre-embedded-prestage | — (open: capture-movie path) |
| Groudon (HG) | — | — (needs HG postgame base with orb; open) |
| Celebi | celebi-event-prestage | HG Celebi base untriggered (VAR 0) — same gate, skipped as duplicate |
| Arceus / Pichu / Rotom / Enigma | pre-states staged | outcome movies open (require playing the event through) |

## Fill methods, cheapest first

1. **Pin** (no play, no synth): assert observed values on a base
   already at the point. Used for all gym ladders and most hides.
2. **Grant** (edit, no play): stage a key item whose only script
   touchpoint is the gate's `HasItem` (Silver Wing, Blue Orb,
   Enigma Stone is bag-native). Forbidden when a give-flag or
   counter would disagree — check `script references` first.
3. **Play** (emulator, no synth): boot a base, drive inputs with
   position feedback from the RAM Location reader, trigger the
   event or reach the area, then **in-game save** and extract the
   staged `.sav`. Produces real post-event states (defeats,
   captures, pickups) the patcher may never write. This is the only
   honest path to Lake pre-Gyarados (forward from a Pryce-era base)
   and the Chikorita intro. Olivine pre-Jasmine is done by this same
   method (flew HG 5gyn Cianwood across to Olivine, staged as
   `hg-olivine-pre-jasmine`; the walk-east idea in the earlier draft
   failed on cliff tiles, fly was the honest forward path). Lake
   pre-Gyarados play is partially driven from the HG 6gyn/Surf synth
   base to Route 43 `(524,149)` (map 45, mid-Spencer), but no in-game
   save/fixture is staged yet (`leveld-out/surf-fast*`, `surf-battle1`).
   In-game saves are extracted with `leveld_run.py`
   `--extract-save` and verified with `inspect` + `diff` against
   the pre-play save so the only deltas are played ones.
4. **Synth** (edit what play can't reach): reserved for states no
   legitimate play sequence from any base can produce; none
   approved yet.

## Deliberately skipped (checked, not oversights)

HG 7gyn (= SS 7gyn point), HG posE4 (= SS pose4 point), HG Celebi
postgame (same untriggered gate as Lyra Celebi), HG 8gyn (= SS 8gyn
point). Duplicates add maintenance, not coverage.
