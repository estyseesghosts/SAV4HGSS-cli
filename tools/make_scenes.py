"""Generate the scene registry (hgss-save.scenes/v1).

A scene is everything needed to put a save at one point of the story:

  base preset   a whole played milestone (registry/presets, tools/make_presets.py)
  badges        the badge set for that point; consequences come from
                registry/hgss-us.badge-rules.json (docs/state-log.md F8/F13)
  ops           explicit flag/variable/item writes for the scene itself
  warp          a real warp event (WarpRegistry, docs/state-log.md F5)

Machine data is not hand-typed where the decomp already has it:

  gym scenes       badge rule -> gym script -> zone_event file -> leader object
                   (sprite, tile, hide flag, script) -> map id and warp tile
  actor hide flags zone_event objects[].eventFlag; set = hidden
                   (scr_seq_0859_T22GYM0101.s:55 is the game's own ClearFlag)
  map ids/warps    registry/hgss-us.warp-spawns.json
  flag/item ids    registry/hgss-us.symbols.json

The registry is a plan, not a claim: `story scene apply` audits the result and
the runtime check (tools/scene_boot_check.py) boots every scene in melonDS and
asserts the live Location.

usage: make_scenes.py --out registry/hgss-us.scenes.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
DECOMP = ROOT.parent / "pokeheartgold"
ZONE = DECOMP / "files" / "fielddata" / "eventdata" / "zone_event"

GAME = "HG"

# Gym leaders in badge order (BADGE_ZEPHYR..BADGE_EARTH, registry/hgss-us.badge-rules.json).
# The map is the room that holds the leader object. The badge script in the rules file is the
# script that gives the badge, which for Clair is the Dragons Den test, not the gym arena, so the
# arena is named here and the leader object is still resolved from the zone-event JSON.
LEADERS = ["Falkner", "Bugsy", "Whitney", "Morty", "Chuck", "Jasmine", "Pryce", "Clair",
           "Brock", "Misty", "Lt. Surge", "Erika", "Janine", "Sabrina", "Blaine", "Blue"]
GYM_MAPS = ["MAP_VIOLET_GYM", "MAP_AZALEA_GYM", "MAP_GOLDENROD_GYM", "MAP_ECRUTEAK_GYM",
            "MAP_CIANWOOD_GYM", "MAP_OLIVINE_GYM", "MAP_MAHOGANY_GYM_LEADER_ROOM",
            "MAP_BLACKTHORN_GYM", "MAP_PEWTER_GYM", "MAP_CERULEAN_GYM", "MAP_VERMILION_GYM",
            "MAP_CELADON_GYM", "MAP_FUCHSIA_GYM", "MAP_SAFFRON_GYM",
            "MAP_SEAFOAM_ISLANDS_CINNABAR_GYM", "MAP_VIRIDIAN_GYM"]

# Static legendary battles: (id, title, game, map symbol, sprite prefix, hide flag,
# base preset, Kanto badge mask, extra ops, sources).
#
# game and Kanto mask are data, not taste:
#   * include/config.h:9-10   VERSION_HEARTGOLD 7, VERSION_SOULSILVER 8
#   * scr_seq_0133_D52R0101.s:20-26  Groudon's room hides him when version == 7 (HG)
#   * scr_seq_0134_D52R0102.s:20-26  Kyogre's room hides him when version != 7 (SS)
#   * scr_seq_0750_T03.s:238-245      Pewter shows Latios on version 7, Latias otherwise
#   * scr_seq_0825_T10R0701.s:142-145 the post-League script clears Zapdos only with BADGE_EARTH,
#                                     so the 16-badge state is where the game produces that actor
# The Kanto mask 255 composes the remaining badges with consequences (F8/F13); the base is the
# real 8-Johto + 4-Kanto save (hg-kanto-4) or the real 16-badge SS save (ss-kanto-8).
LEGENDARIES = [
    ("lugia", "Lugia, Whirl Islands B3F", "HG", "MAP_WHIRL_ISLANDS_B3F_LUGIA_CAVE",
     "SPRITE_LUG_OBJ01", "FLAG_HIDE_WHIRL_ISLAND_LUGIA", "hg-08-rising", 0,
     [("flag", "FLAG_CAUGHT_LUGIA", False)],
     ["files/fielddata/script/scr_seq/scr_seq_0104_D40R0107.s:112 (capture sets FLAG_CAUGHT_LUGIA)",
      "scr_seq_0104_D40R0107.s:38 (GoToIfSet FLAG_HIDE_WHIRL_ISLAND_LUGIA = actor hidden)",
      "fixtures/event-prereqs.json lugia-whirl-islands"]),
    ("ho-oh", "Ho-Oh, Bell Tower roof", "HG", "MAP_BELL_TOWER_ROOF",
     "SPRITE_HOU_OBJ01", "FLAG_HIDE_BELL_TOWER_HO_OH", "hg-08-rising", 0,
     [("flag", "FLAG_CAUGHT_HO_OH", False)],
     ["zone_event 311_D17R0110.json objects[5] (SPRITE_HOU_OBJ01 at 15,16)",
      "scr_seq_0021_D17R0110.s:21 (OnLoad hides only while FLAG_ENGAGING_STATIC_POKEMON)",
      "fixtures/event-prereqs.json ho-oh-bell-tower"]),
    ("articuno", "Articuno, Seafoam Islands B4F", "HG", "MAP_SEAFOAM_ISLANDS_B4F",
     "SPRITE_FOLLOWER_MON_STATIC_ARTICUNO", "FLAG_HIDE_SEAFOAM_ISLAND_ARTICUNO",
     "hg-kanto-4", 255, [("flag", "FLAG_CAUGHT_ARTICUNO", False)],
     ["zone_event 411_D11R0105.json (static Articuno object)",
      "scr_seq_0014_D11R0105.s:12 (OnLoad hides only while FLAG_ENGAGING_STATIC_POKEMON)",
      "scr_seq_0825_T10R0701.s:138 (the post-League script clears this flag)",
      "fixtures/event-prereqs.json kanto-arc"]),
    ("zapdos", "Zapdos, Route 10", "HG", "MAP_ROUTE_10",
     "SPRITE_FOLLOWER_MON_STATIC_ZAPDOS", "FLAG_HIDE_ROUTE_10_ZAPDOS",
     "hg-kanto-4", 255, [("flag", "FLAG_CAUGHT_ZAPDOS", False),
                         ("flag", "FLAG_RESTORED_POWER", True)],
     ["zone_event 015_R10.json (static Zapdos object)",
      "scr_seq_0825_T10R0701.s:142-145 (the post-League script clears this flag only with BADGE_EARTH)",
      "fixtures/event-prereqs.json kanto-arc"]),
    ("moltres", "Moltres, Mt. Silver cave", "HG", "MAP_MOUNT_SILVER_CAVE_MOLTRES_CHAMBER",
     "SPRITE_FOLLOWER_MON_STATIC_MOLTRES", "FLAG_HIDE_MT_SILVER_CAVE_MOLTRES",
     "hg-kanto-4", 255, [("flag", "FLAG_CAUGHT_MOLTRES", False)],
     ["zone_event 416_D41R0105.json (static Moltres object)",
      "scr_seq_0106_D41R0105.s:12 (OnLoad hides only while FLAG_ENGAGING_STATIC_POKEMON)",
      "fixtures/event-prereqs.json kanto-arc"]),
    ("mewtwo", "Mewtwo, Cerulean Cave B1F", "HG", "MAP_CERULEAN_CAVE_B1F",
     "SPRITE_FOLLOWER_MON_STATIC_MEWTWO", "FLAG_HIDE_CERULEAN_CAVE_MEWTWO",
     "hg-kanto-4", 255, [("flag", "FLAG_CAUGHT_MEWTWO", False)],
     ["zone_event 406_D03R0103.json (static Mewtwo object at 56,24)",
      "scr_seq_0011_D03R0103.s:12 (OnLoad hides only while FLAG_ENGAGING_STATIC_POKEMON)",
      "scr_seq_0825_T10R0701.s:134 (the post-League script clears this flag)",
      "fixtures/event-prereqs.json kanto-arc (FLAG_CAUGHT_MEWTWO at 16 badges)"]),
    ("suicune", "Suicune, Route 25", "HG", "MAP_ROUTE_25",
     "SPRITE_FOLLOWER_MON_STATIC_SUICUNE", "FLAG_HIDE_ROUTE_25_SUICUNE",
     "hg-kanto-4", 255, [("flag", "FLAG_CAUGHT_SUICUNE", False)],
     ["zone_event 026_R25.json (static Suicune object at 1421,37; Eusine scene scr_seq_R25_005/006)",
      "scr_seq_0216_R25.s:88-92 (OnLoad hides only while FLAG_ENGAGING_STATIC_POKEMON)",
      "scr_seq_0825_T10R0701.s:161-164 (cleared when VAR_SCENE_ROUTE_25 == 3)",
      "fixtures/event-prereqs.json kanto-arc (FLAG_CAUGHT_SUICUNE at 16 badges)"]),
    ("kyogre", "Kyogre, Embedded Tower", "HG", "MAP_EMBEDDED_TOWER_KYOGRE_ROOM",
     "SPRITE_FOLLOWER_MON_STATIC_KYOGRE", "FLAG_HIDE_EMBEDDED_TOWER_KYOGRE",
     "hg-kanto-4", 255,
     [("flag", "FLAG_CAUGHT_KYOGRE", False), ("item", "ITEM_BLUE_ORB", 1),
      ("var", "VAR_SCENE_EMBEDDED_TOWER", 0)],
     ["zone_event 477_D52R0102.json (static Kyogre object at 16,11)",
      "scr_seq_0134_D52R0102.s:20-26 (version 7/HG runs the orb check; SS hides the actor)",
      "scr_seq_0134_D52R0102.s:35-38 (HasItem ITEM_BLUE_ORB gates ClearFlag)",
      "fixtures/event-prereqs.json kyogre-embedded-tower"]),
    ("groudon", "Groudon, Embedded Tower", "SS", "MAP_EMBEDDED_TOWER_GROUDON_ROOM",
     "SPRITE_FOLLOWER_MON_STATIC_GROUDON", "FLAG_HIDE_EMBEDDED_TOWER_GROUDON",
     "ss-kanto-8", 255,
     [("flag", "FLAG_CAUGHT_GROUDON", False), ("item", "ITEM_RED_ORB", 1),
      ("var", "VAR_SCENE_EMBEDDED_TOWER", 0)],
     ["zone_event 476_D52R0101.json (static Groudon object at 16,11)",
      "scr_seq_0133_D52R0101.s:20-26 (version 7/HG sets the hide flag; SS runs the orb check)",
      "scr_seq_0133_D52R0101.s:35-38 (HasItem ITEM_RED_ORB gates ClearFlag)",
      "fixtures/event-prereqs.json kyogre-embedded-tower"]),
    ("rayquaza", "Rayquaza, Embedded Tower", "HG", "MAP_EMBEDDED_TOWER_RAYQUAZA_ROOM",
     "SPRITE_FOLLOWER_MON_STATIC_RAYQUAZA", "FLAG_HIDE_EMBEDDED_TOWER_RAYQUAZA",
     "hg-kanto-4", 255,
     [("flag", "FLAG_CAUGHT_RAYQUAZA", False), ("item", "ITEM_JADE_ORB", 1),
      ("var", "VAR_SCENE_EMBEDDED_TOWER", 0)],
     ["zone_event 478_D52R0103.json (static Rayquaza object at 16,11)",
      "scr_seq_0135_D52R0103.s:16-25 (OnTransition clears the hide flag while VAR_SCENE_EMBEDDED_TOWER < 7)",
      "fixtures/event-prereqs.json kyogre-embedded-tower"]),
    ("latios", "Latios, Pewter City (Enigma Stone)", "HG", "MAP_PEWTER",
     "SPRITE_FOLLOWER_MON_STATIC_LATIOS", "FLAG_HIDE_PEWTER_CITY_LATIOS",
     "hg-kanto-4", 60, [("item", "ITEM_ENIGMA_STONE", 1), ("var", "VAR_UNK_4083", 3)],
     ["zone_event 048_T03.json (static Latios object at 1039,83)",
      "scr_seq_0750_T03.s:238-245 (version 7/HG clears the Latios hide flag)",
      "scr_seq_0750_T03.s:356-379 (the battle species is chosen by version)",
      "scr_seq_0750_T03.s:226-232 (the Pewter transition only shows the actor for VAR_UNK_4083 2..4)",
      "fixtures/event-prereqs.json enigma-stone-pewter"]),
    ("latias", "Latias, Pewter City (Enigma Stone)", "SS", "MAP_PEWTER",
     "SPRITE_FOLLOWER_MON_STATIC_LATIAS", "FLAG_HIDE_PEWTER_CITY_LATIAS",
     "ss-kanto-8", 255, [("item", "ITEM_ENIGMA_STONE", 1), ("var", "VAR_UNK_4083", 3)],
     ["zone_event 048_T03.json (static Latias object at 1039,83)",
      "scr_seq_0750_T03.s:238-245 (version 8/SS clears the Latias hide flag)",
      "scr_seq_0750_T03.s:356-379 (the battle species is chosen by version)",
      "scr_seq_0750_T03.s:226-232 (the Pewter transition only shows the actor for VAR_UNK_4083 2..4)",
      "fixtures/event-prereqs.json enigma-stone-pewter"]),
]
# Not shipped as a scene: the Silph Co. Rotom room
# (scr_seq_0838_T11R0702.s:17-34) sets all five appliance hide flags on every transition and
# then clears only the forms the player owns (GetOwnedRotomForms). The actor cannot be produced
# by flags alone, and Rotom is not a legendary encounter.

# The Mt. Silver / Red unlock is a chain of writes, not one flag. Each entry below is a write the
# game itself performs on the way to it:
#   scr_seq_0743_T02GYM0101.s:65   beating Blue sets VAR_UNK_4096 = 1
#   scr_seq_0514_T02_hdr.s:13      Viridian's header runs scr_seq_T02_011 while that var is 1
#   scr_seq_0741_T02.s:42-44       that script sets FLAG_UNLOCKED_WEST_KANTO and VAR_UNK_4121 = 1
#   scr_seq_0741_T02.s:48-51       then sets VAR_UNK_4096 = 2 after Oak's phone call
#   scr_seq_0740_T01R0301.s:95-96  Oak's lab branch requires VAR_UNK_4096 == 2
#   scr_seq_0740_T01R0301.s:198-208 gives ITEM_HM08, sets VAR_UNK_4110 = 2,
#                                  FLAG_UNLOCKED_MT_SILVER and FLAG_GOT_HM08
# The Route 22 state read by the guards matches those pieces:
#   scr_seq_0213_R22R0101.s:19-23  the policemen step aside only for the two flags
#   zone_event 270_R22R0101.json   coord scripts run while VAR_UNK_4121 == 0 (west path, 14,8)
#                                  and while VAR_UNK_4110 == 1 (north gate, 8,8)
MT_SILVER_UNLOCK_EXTRAS = [
    ("flag", "FLAG_UNLOCKED_MT_SILVER", True),
    ("flag", "FLAG_UNLOCKED_WEST_KANTO", True),
    ("flag", "FLAG_GOT_HM08", True),
    ("var", "VAR_UNK_4096", 2),
    ("var", "VAR_UNK_4110", 2),
    ("var", "VAR_UNK_4121", 1),
    ("item", "ITEM_HM08", 1),
]
MT_SILVER_UNLOCK_SOURCES = [
    "scr_seq_0743_T02GYM0101.s:65 (Blue's defeat sets VAR_UNK_4096 = 1)",
    "scr_seq_0514_T02_hdr.s:13 (Viridian header runs scr_seq_T02_011 while VAR_UNK_4096 == 1)",
    "scr_seq_0741_T02.s:42-44 (FLAG_UNLOCKED_WEST_KANTO, VAR_UNK_4121 = 1)",
    "scr_seq_0741_T02.s:48-51 (Oak's call, VAR_UNK_4096 = 2)",
    "scr_seq_0740_T01R0301.s:95-96,198-208 (Oak gives HM08; VAR_UNK_4110 = 2, FLAG_UNLOCKED_MT_SILVER, FLAG_GOT_HM08)",
    "scr_seq_0213_R22R0101.s:19-23,388 (the Route 22 policemen read both unlock flags)",
    "zone_event 270_R22R0101.json coords (VAR_UNK_4121 == 0 west path, VAR_UNK_4110 == 1 north gate)",
]

# Milestone scenes whose value is the state and the spawn, not a forced cutscene: base preset + warp.
# extra = list of ("flag", NAME, value) / ("var", NAME, value) / ("item", NAME, qty).
MILESTONES = [
    ("event-dragons-den-shrine", "Dragons Den shrine (Dratini gift)", "hg-08-rising",
     "MAP_DRAGONS_DEN_SHRINE", [], ["zone_event 259_D44R0103.json (shrine objects)",
                                    "census: Pre-Dratini.sav stands at this shrine at 8 badges"]),
    ("event-dragons-den-test", "Dragons Den (Clair's test)", "hg-08-rising",
     "MAP_DRAGONS_DEN", [], ["zone_event 239_D44R0102.json"]),
    ("event-hall-of-fame", "Hall of Fame (champion)", "hg-champion",
     "MAP_POKEMON_LEAGUE_HALL_OF_FAME", [], ["census: Pokemon_HeartGoldposE4 save is on map 306"]),
    ("event-burned-tower", "Burned Tower B1F (the beasts flee)", "hg-03-plain",
     "MAP_BURNED_TOWER_B1F", [], ["fixtures/event-prereqs.json burned-tower-dogs-flee"]),
    ("event-sprout-tower", "Sprout Tower 3F (Elder)", "hg-00-cherrygrove",
     "MAP_SPROUT_TOWER_3F", [], ["zone_event 151_D15R0103.json"]),
    ("event-slowpoke-well", "Slowpoke Well B2F", "hg-01-zephyr",
     "MAP_SLOWPOKE_WELL_B2F", [], ["zone_event 174_D26R0103.json"]),
    ("event-victory-road-rival", "Indigo Plateau entrance (rival battle)", "hg-e4-gate",
     "MAP_POKEMON_LEAGUE_ENTRANCE", [], ["zone_event 271_T10R0101.json object SPRITE_GSRIVEL at 10,24"]),
    ("event-starter", "New Bark Town, Elm's lab (starter)", "hg-00-cherrygrove",
     "MAP_NEW_BARK_ELMS_LAB_1F", [], ["zone_event for MAP_NEW_BARK_ELMS_LAB_1F"]),
    ("event-mr-pokemon", "Route 30, Mr. Pokemon's house", "hg-00-cherrygrove",
     "MAP_ROUTE_30_MR_POKEMON_HOUSE", [], ["zone_event 052_T30R0201.json"]),
    ("event-cherrygrove-rival", "Cherrygrove City (first rival battle)", "hg-00-cherrygrove",
     "MAP_CHERRYGROVE", [], ["census: the 0-badge donor save stands here"]),
    ("event-ilex-forest", "Ilex Forest (Farfetch'd and Cut)", "hg-02-hive",
     "MAP_ILEX_FOREST", [], ["zone_event 114_D36R0101.json"]),
    ("event-sudowoodo", "Route 36 (Sudowoodo)", "hg-03-plain",
     "MAP_ROUTE_36", [], ["zone_event 100_R36.json"]),
    ("event-kimono-dance", "Ecruteak Dance Theater (Kimono Girls)", "hg-08-rising",
     "MAP_ECRUTEAK_DANCE_THEATER", [], ["zone_event for MAP_ECRUTEAK_DANCE_THEATER"]),
    ("event-rocket-hq", "Team Rocket HQ B1F (Mahogany)", "hg-06-mineral",
     "MAP_TEAM_ROCKET_HEADQUARTERS_B1F", [], ["zone_event 148_D35R0101.json"]),
    ("event-lake-of-rage", "Lake of Rage (red Gyarados)", "hg-06-mineral",
     "MAP_LAKE_OF_RAGE", [("flag", "FLAG_HIDE_LAKE_OF_RAGE_RED_GYARADOS", False),
                          ("flag", "FLAG_CAUGHT_RED_GYARADOS", False)],
     ["zone_event 085_T29.json object SPRITE_RGYARADOSU at 525,71",
      "scr_seq_0938_T29.s:135 (the scene's own hide-flag write)"]),
    ("event-radio-tower", "Goldenrod Radio Tower 1F (takeover)", "hg-06-mineral",
     "MAP_GOLDENROD_RADIO_TOWER_1F", [], ["docs/state-log.md F10/F11 (header script starts the takeover on load)"]),
    ("event-ice-path", "Ice Path 1F", "hg-07-glacier",
     "MAP_ICE_PATH_1F", [], ["zone_event 117_D39R0101.json"]),
    ("event-olivine-lighthouse", "Olivine Lighthouse light room", "hg-05-storm",
     "MAP_OLIVINE_LIGHTHOUSE_LIGHT_ROOM", [], ["docs/state-log.md F8 (VAR_SCENE_LIGHTHOUSE_JASMINE gates this room)"]),
    ("event-mt-silver", "Route 28 (the road to Mt. Silver)", "hg-kanto-4",
     "MAP_ROUTE_28", MT_SILVER_UNLOCK_EXTRAS,
     ["fixtures/event-prereqs.json kanto-arc (FLAG_UNLOCKED_MT_SILVER at 16 badges)"] + MT_SILVER_UNLOCK_SOURCES),
]


def load(path: Path):
    return json.load(open(path, encoding="utf-8"))


class Registry:
    def __init__(self) -> None:
        self.symbols = load(ROOT / "registry" / "hgss-us.symbols.json")["symbols"]
        self.warps = load(ROOT / "registry" / "hgss-us.warp-spawns.json")["maps"]
        self.badges = load(ROOT / "registry" / "hgss-us.badge-rules.json")["badges"]
        self.presets = load(ROOT / "registry" / "presets" / "index.json")["presets"]
        self.flag_id = {s["name"]: s["id"] for s in self.symbols if s["kind"] == "persistent_flag"}
        self.var_id = {s["name"]: s["id"] for s in self.symbols if s["kind"] == "save_work"}
        self.item_id = {s["name"]: s["id"] for s in self.symbols if s["kind"] == "item"}
        self.by_symbol = {m["map_symbol"]: m for m in self.warps}
        self.by_zone = {m["zone_event_file"]: m for m in self.warps}

    def fid(self, name: str) -> int:
        if name not in self.flag_id:
            raise SystemExit(f"unknown persistent flag {name}")
        return self.flag_id[name]

    def iid(self, name: str) -> int:
        if name not in self.item_id:
            raise SystemExit(f"unknown item {name}")
        return self.item_id[name]

    def vid(self, name: str) -> int:
        if name not in self.var_id:
            raise SystemExit(f"unknown save variable {name}")
        return self.var_id[name]

    def preset_for(self, johto: int, kanto: int) -> str | None:
        for p in self.presets:
            if p["game"] != GAME:
                continue
            if p["badges_johto"] == johto and p["badges_kanto"] == kanto:
                return p["name"]
        return None


def zone_objects(zone_file: str):
    return json.load(open(ZONE / zone_file, encoding="utf-8")).get("objects", [])


def warp_tile(map_entry) -> tuple[int, int, int]:
    arrivals = map_entry.get("warp_arrivals") or []
    if not arrivals:
        raise SystemExit(f"{map_entry['map_symbol']} has no warp arrivals")
    a = arrivals[0]
    return a["warp_index"], a["x"], a["y"]


def make_gym_scenes(reg: Registry) -> list[dict]:
    scenes = []
    for badge in reg.badges:
        n = badge["id"] + 1
        gym_sym = GYM_MAPS[badge["id"]]
        m = reg.by_symbol.get(gym_sym)
        if m is None:
            raise SystemExit(f"badge {badge['name']}: no map {gym_sym}")
        zone_stem = Path(m["zone_event_file"]).stem.split("_", 1)[-1]  # T22GYM0101
        objs = zone_objects(m["zone_event_file"])
        leaders = [o for o in objs if o.get("spriteId", "").startswith("SPRITE_GSLEADER")
                   and o.get("eventFlag") not in (None, "FLAG_NOTHING")]
        if len(leaders) > 1:
            # Viridian has two objects on the same hide flag; the leader's script names its room.
            pref = [o for o in leaders if zone_stem in (o.get("scriptId") or "")]
            leaders = pref or leaders
        if not leaders:
            raise SystemExit(f"badge {badge['name']}: no leader object in {m['zone_event_file']}")
        leader = leaders[0]
        warp, wx, wy = warp_tile(m)
        hide = leader.get("eventFlag")
        sources = [
            f"registry/hgss-us.badge-rules.json badge {badge['id']} {badge['name']} ({badge['script']})",
            f"{m['zone_event_file']} objects[] {leader['spriteId']} at ({leader['x']},{leader['z']}) flag {hide} script {leader.get('scriptId')}",
            f"registry/hgss-us.warp-spawns.json map {m['map_id']} {m['map_symbol']} warp {warp} tile ({wx},{wy})",
            "docs/state-log.md F14 (gym scripts carry no story guard)",
        ]
        if badge["id"] < 8:
            johto_after = (1 << (badge["id"] + 1)) - 1
            johto_before = (1 << badge["id"]) - 1
            kanto_after = kanto_before = 0
            after_preset = reg.preset_for(johto_after, 0)
            before_preset = reg.preset_for(johto_before, 0)
        else:
            kanto_after = (1 << (badge["id"] - 8 + 1)) - 1
            kanto_before = (1 << (badge["id"] - 8)) - 1
            johto_after = johto_before = 255
            # Kanto has no per-badge played saves beyond 4 (census); the badge consequences are
            # composed instead, which F13 records as the supported path.
            after_preset = before_preset = "hg-champion"
        for phase in ("before", "after"):
            mask_j = johto_after if phase == "after" else johto_before
            mask_k = kanto_after if phase == "after" else kanto_before
            base = after_preset if phase == "after" else before_preset
            ops = []
            if phase == "before":
                ops.append({"op": "flag.set", "id": reg.fid(hide), "value": False})
            ops.append({"op": "location.warp", "map": m["map_id"], "warp": warp})
            scenes.append({
                "id": f"gym-{n:02d}-{badge['name'].lower()}-{phase}",
                "title": f"{m['map_symbol']} ({LEADERS[badge['id']]}), {phase} the badge",
                "kind": "gym",
                "game": GAME,
                "base_preset": base,
                "badges": {"johto": mask_j, "kanto": mask_k},
                "ops": ops,
                "expect": {
                    "map": m["map_id"], "warp": warp, "x": wx, "y": wy,
                    "badges_johto": mask_j, "badges_kanto": mask_k,
                    "flags_on": [], "flags_off": [hide] if phase == "before" else [],
                },
                "actor": {"sprite": leader["spriteId"], "x": leader["x"], "z": leader["z"],
                          "script": leader.get("scriptId"), "hide_flag": hide},
                "sources": sources,
                "note": (f"Clears {hide} to make the leader present, which is what the gym's own "
                         f"init script does, then warps onto warp {warp} of the same map.")
                if phase == "before" else
                        "Base preset is the played milestone after this badge; the warp puts the player back in the gym.",
            })
    return scenes


def make_legendary_scenes(reg: Registry) -> list[dict]:
    scenes = []
    for (sid, title, game, map_sym, sprite, hide, base, kanto, extra, sources) in LEGENDARIES:
        m = reg.by_symbol.get(map_sym)
        if m is None:
            raise SystemExit(f"legendary {sid}: no map {map_sym}")
        preset = next((p for p in reg.presets if p["name"] == base), None)
        if preset is None:
            raise SystemExit(f"legendary {sid}: no preset {base}")
        if preset["game"] != game:
            raise SystemExit(f"legendary {sid}: preset {base} is {preset['game']}, scene is {game}")
        objs = [o for o in zone_objects(m["zone_event_file"])
                if o.get("spriteId", "").startswith(sprite)]
        if not objs:
            raise SystemExit(f"legendary {sid}: no {sprite} object in {m['zone_event_file']}")
        actor = objs[0]
        warp, wx, wy = warp_tile(m)
        ops = [
            {"op": "flag.set", "id": reg.fid(hide), "value": False},
            {"op": "flag.set", "id": reg.fid("FLAG_ENGAGING_STATIC_POKEMON"), "value": False},
        ]
        on, off = [], [hide, "FLAG_ENGAGING_STATIC_POKEMON"]
        want_vars: list[dict] = []
        want_items: list[dict] = []
        for kind, name, value in extra:
            if kind == "flag":
                ops.append({"op": "flag.set", "id": reg.fid(name), "value": value})
                (on if value else off).append(name)
            elif kind == "var":
                ops.append({"op": "variable.set", "id": reg.vid(name), "value": value})
                want_vars.append({"name": name, "value": value})
            elif kind == "item":
                ops.append({"op": "inventory.quantity.set", "item": reg.iid(name), "quantity": value})
                want_items.append({"name": name, "quantity": value})
            else:
                raise SystemExit(f"legendary {sid}: unknown op kind {kind}")
        ops.append({"op": "location.warp", "map": m["map_id"], "warp": warp})
        johto = preset["badges_johto"]
        scenes.append({
            "id": f"legendary-{sid}",
            "title": title,
            "kind": "legendary",
            "game": game,
            "base_preset": base,
            "badges": {"johto": johto, "kanto": kanto},
            "ops": ops,
            "expect": {"map": m["map_id"], "warp": warp, "x": wx, "y": wy,
                       "badges_johto": johto, "badges_kanto": kanto,
                       "flags_on": on, "flags_off": off,
                       "vars": want_vars, "items": want_items},
            "actor": {"sprite": actor["spriteId"], "x": actor["x"], "z": actor["z"],
                      "script": actor.get("scriptId"), "hide_flag": hide},
            "sources": sources + [
                f"registry/hgss-us.warp-spawns.json map {m['map_id']} {map_sym} warp {warp} tile ({wx},{wy})",
                f"registry/presets/{base}.json ({preset['game']}, Johto {preset['badges_johto']}, Kanto {preset['badges_kanto']})"],
            "note": ("The actor is placed by clearing its zone-event hide flag, and the map's own "
                     "init script keeps it visible in this state (tools/scene_actor_check.py); the "
                     "cutscene that normally clears the flag is not replayed."),
        })
    return scenes


def make_milestone_scenes(reg: Registry) -> list[dict]:
    scenes = []
    for (sid, title, base, map_sym, extra, sources) in MILESTONES:
        m = reg.by_symbol[map_sym]
        warp, wx, wy = warp_tile(m)
        ops: list[dict] = []
        on, off = [], []
        want_vars, want_items = [], []
        for kind, name, value in extra:
            if kind == "flag":
                ops.append({"op": "flag.set", "id": reg.fid(name), "value": value})
                (on if value else off).append(name)
            elif kind == "var":
                ops.append({"op": "variable.set", "id": reg.vid(name), "value": value})
                want_vars.append({"name": name, "value": value})
            elif kind == "item":
                ops.append({"op": "inventory.quantity.set", "item": reg.iid(name), "quantity": value})
                want_items.append({"name": name, "quantity": value})
            else:
                raise SystemExit(f"milestone {sid}: unknown op kind {kind}")
        ops.append({"op": "location.warp", "map": m["map_id"], "warp": warp})
        preset = next(p for p in reg.presets if p["name"] == base)
        expect = {"map": m["map_id"], "warp": warp, "x": wx, "y": wy,
                  "badges_johto": preset["badges_johto"], "badges_kanto": preset["badges_kanto"],
                  "flags_on": on, "flags_off": off}
        if want_vars:
            expect["vars"] = want_vars
        if want_items:
            expect["items"] = want_items
        scenes.append({
            "id": sid,
            "title": title,
            "kind": "event",
            "game": GAME,
            "base_preset": base,
            "badges": {"johto": preset["badges_johto"], "kanto": preset["badges_kanto"]},
            "ops": ops,
            "expect": expect,
            "sources": sources + [
                f"registry/hgss-us.warp-spawns.json map {m['map_id']} {map_sym} warp {warp} tile ({wx},{wy})"],
            "note": "State + spawn. Cutscene triggers on the map are the game's own (docs/state-log.md F11); this scene does not force a trigger variable.",
        })
    return scenes


# Some maps move the player with their own init scripts: the tile in the save is the entry point,
# not the tile the player stands on once the map has loaded. The boot check asserts the post-load
# tile for these scenes; the save audit still asserts the warp tile.
#   e4-lance: scr_seq_0597_T10R0601_hdr.s:10-14 runs scr_seq_T10R0601_001 from OnFrameTable while
#             VAR_UNK_40C8 == 0, and that script applies _0048 = WalkNormalNorth 6
#             (scr_seq_0824_T10R0601.s:31-33), which takes the player from (6,28) to (6,22).
POST_LOAD_MOVE = {
    "e4-lance": {"x": 6, "y": 22,
                 "sources": ["scr_seq_0597_T10R0601_hdr.s:10-14 (OnFrameTable entry, VAR_UNK_40C8 == 0)",
                             "scr_seq_0824_T10R0601.s:12-33 (entry script: WalkNormalNorth 6)"]},
}


def make_e4_scenes(reg: Registry) -> list[dict]:
    rooms = [
        ("e4-will", "Elite Four: Will", "MAP_POKEMON_LEAGUE_WILL_ROOM", "SPRITE_GSBIGFOUR1"),
        ("e4-koga", "Elite Four: Koga", "MAP_POKEMON_LEAGUE_KOGA_ROOM", "SPRITE_GSBIGFOUR2"),
        ("e4-bruno", "Elite Four: Bruno", "MAP_POKEMON_LEAGUE_BRUNO_ROOM", "SPRITE_GSBIGFOUR3"),
        ("e4-karen", "Elite Four: Karen", "MAP_POKEMON_LEAGUE_KAREN_ROOM", "SPRITE_GSBIGFOUR4"),
        ("e4-lance", "Champion: Lance", "MAP_POKEMON_LEAGUE_LANCE_ROOM", "SPRITE_WATARU"),
    ]
    scenes = []
    m = reg.by_symbol["MAP_POKEMON_LEAGUE_ENTRANCE"]
    warp, wx, wy = warp_tile(m)
    scenes.append({
        "id": "e4-before", "title": "Pokemon League entrance, before the Elite Four", "kind": "e4",
        "game": GAME, "base_preset": "hg-e4-gate", "badges": {"johto": 255, "kanto": 0},
        "ops": [{"op": "location.warp", "map": m["map_id"], "warp": warp}],
        "expect": {"map": m["map_id"], "warp": warp, "x": wx, "y": wy, "badges_johto": 255,
                   "badges_kanto": 0, "flags_on": [], "flags_off": []},
        "sources": ["registry/presets/index.json hg-e4-gate (real save at the League entrance)"],
        "note": "Real played milestone; the warp is the save's own spawn.",
    })
    for sid, title, map_sym, sprite in rooms:
        m = reg.by_symbol[map_sym]
        warp, wx, wy = warp_tile(m)
        objs = [o for o in zone_objects(m["zone_event_file"]) if o.get("spriteId") == sprite]
        actor = objs[0] if objs else None
        if actor is None:
            raise SystemExit(f"{sid}: no {sprite} object in {m['zone_event_file']}")
        expect = {"map": m["map_id"], "warp": warp, "x": wx, "y": wy, "badges_johto": 255,
                  "badges_kanto": 0, "flags_on": [], "flags_off": []}
        sources = [f"{m['zone_event_file']} (room object)",
                   "docs/state-log.md F14 (E4 rooms have no story guard for the battle)"]
        note = "Warp into the room; the member is a room object, talked to from the entrance tile."
        move = POST_LOAD_MOVE.get(sid)
        if move:
            expect["spawn_after"] = {"x": move["x"], "y": move["y"]}
            sources += move["sources"]
            note = ("Warp into the room. The map's own frame script walks the player in from the "
                    "entry tile before any input, so the runtime position is the post-load tile; "
                    "the save keeps the entry warp tile.")
        scenes.append({
            "id": sid, "title": title, "kind": "e4", "game": GAME, "base_preset": "hg-e4-gate",
            "badges": {"johto": 255, "kanto": 0},
            "ops": [{"op": "location.warp", "map": m["map_id"], "warp": warp}],
            "expect": expect,
            "actor": None if actor is None else {"sprite": actor["spriteId"], "x": actor["x"],
                                                 "z": actor["z"], "script": actor.get("scriptId")},
            "sources": sources,
            "note": note,
        })
    m = reg.by_symbol["MAP_POKEMON_LEAGUE_HALL_OF_FAME"]
    warp, wx, wy = warp_tile(m)
    scenes.append({
        "id": "e4-after", "title": "Champion beaten (Hall of Fame)", "kind": "e4", "game": GAME,
        "base_preset": "hg-champion", "badges": {"johto": 255, "kanto": 0},
        "ops": [{"op": "location.warp", "map": m["map_id"], "warp": warp}],
        "expect": {"map": m["map_id"], "warp": warp, "x": wx, "y": wy, "badges_johto": 255,
                   "badges_kanto": 0, "flags_on": [], "flags_off": []},
        "sources": ["registry/presets/index.json hg-champion (real save on the Hall of Fame map)"],
        "note": "Real played milestone.",
    })
    return scenes


def mt_silver_unlock_ops(reg: Registry):
    """Ops + expectations for the full Mt. Silver / Red unlock state (see MT_SILVER_UNLOCK_EXTRAS)."""
    ops: list[dict] = []
    on: list[str] = []
    off: list[str] = []
    want_vars: list[dict] = []
    want_items: list[dict] = []
    for kind, name, value in MT_SILVER_UNLOCK_EXTRAS:
        if kind == "flag":
            ops.append({"op": "flag.set", "id": reg.fid(name), "value": value})
            (on if value else off).append(name)
        elif kind == "var":
            ops.append({"op": "variable.set", "id": reg.vid(name), "value": value})
            want_vars.append({"name": name, "value": value})
        elif kind == "item":
            ops.append({"op": "inventory.quantity.set", "item": reg.iid(name), "quantity": value})
            want_items.append({"name": name, "quantity": value})
        else:
            raise SystemExit(f"unknown mt-silver op kind {kind}")
    return ops, on, off, want_vars, want_items


def make_red_scene(reg: Registry) -> list[dict]:
    m = reg.by_symbol["MAP_MOUNT_SILVER_CAVE_SUMMIT"]
    warp, wx, wy = warp_tile(m)
    actor = next(o for o in zone_objects(m["zone_event_file"]) if o["spriteId"] == "SPRITE_RED")
    ops, on, off, want_vars, want_items = mt_silver_unlock_ops(reg)
    ops.append({"op": "flag.set", "id": reg.fid("FLAG_ENGAGING_STATIC_POKEMON"), "value": False})
    off.append("FLAG_ENGAGING_STATIC_POKEMON")
    ops.append({"op": "location.warp", "map": m["map_id"], "warp": warp})
    return [{
        "id": "legendary-red",
        "title": "Red, Mt. Silver summit",
        "kind": "legendary",
        "game": GAME,
        "base_preset": "hg-kanto-4",
        "badges": {"johto": 255, "kanto": 255},
        "ops": ops,
        "expect": {"map": m["map_id"], "warp": warp, "x": wx, "y": wy, "badges_johto": 255,
                   "badges_kanto": 255, "flags_on": on, "flags_off": off,
                   "vars": want_vars, "items": want_items},
        "actor": {"sprite": actor["spriteId"], "x": actor["x"], "z": actor["z"],
                  "script": actor.get("scriptId"), "hide_flag": actor.get("eventFlag")},
        "sources": [
            "zone_event 419_D41R0108.json (SPRITE_RED at 30,11)",
            "fixtures/event-prereqs.json kanto-arc: FLAG_UNLOCKED_MT_SILVER=true at 16 badges",
            "census: Pokemon_SoulSilver16gyns.sav owns FLAG_UNLOCKED_MT_SILVER with Kanto 255",
        ] + MT_SILVER_UNLOCK_SOURCES,
        "note": "No HG save in the corpus holds 5-8 Kanto badges; the last four badges and the unlock come from the badge rules (F8/F13). The Mt. Silver state is the whole chain the game writes, not just the one flag.",
    }]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(ROOT / "registry" / "hgss-us.scenes.json"))
    args = ap.parse_args()
    reg = Registry()
    scenes = []
    scenes += make_gym_scenes(reg)
    scenes += make_e4_scenes(reg)
    scenes += make_legendary_scenes(reg)
    scenes += make_red_scene(reg)
    scenes += make_milestone_scenes(reg)
    ids = [s["id"] for s in scenes]
    if len(ids) != len(set(ids)):
        raise SystemExit("duplicate scene ids")
    doc = {
        "schema": "hgss-save.scenes/v1",
        "source": {
            "badge_rules": "registry/hgss-us.badge-rules.json",
            "warps": "registry/hgss-us.warp-spawns.json",
            "symbols": "registry/hgss-us.symbols.json",
            "presets": "registry/presets/index.json",
            "zone_events": "pokeheartgold/files/fielddata/eventdata/zone_event",
        },
        "counts": {
            "total": len(scenes),
            "gym": sum(1 for s in scenes if s["kind"] == "gym"),
            "e4": sum(1 for s in scenes if s["kind"] == "e4"),
            "legendary": sum(1 for s in scenes if s["kind"] == "legendary"),
            "event": sum(1 for s in scenes if s["kind"] == "event"),
        },
        "scenes": scenes,
    }
    Path(args.out).write_text(json.dumps(doc, indent=1), encoding="utf-8")
    print(f'{len(scenes)} scenes -> {args.out}', flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())



