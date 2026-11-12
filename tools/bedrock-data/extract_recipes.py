"""Builds Zenith's embedded `recipes.json.gz` from a stock BDS install's behaviour packs.

    python extract_recipes.py <bds root> <item_registry.json> <output.gz> [--verify <capture dir>]

Source of truth is the same data BDS loads (see bds_pack.py): the layered `recipes` catalog of the `vanilla*` packs.
No numeric id and no wire capture is baked into the output - everything is keyed by item NAME and resolved against
Zenith's own palettes at load time. `item_registry.json` (names only) is used to check that every name exists.

What is emitted
  * every `minecraft:recipe_shaped` / `recipe_shapeless` in the `crafting_table` station whose ingredients and
    result resolve to a registry item (legacy spellings through ALIASES, legacy `data` values through DATA_NAMES);
  * the colour-dyeing families BDS generates in code (wool, wool slab/stairs, shulker box), built here from the colour
    list so their names are real flattened item names instead of BDS's `name:aux` strings.
What is NOT emitted (and is counted/printed, never guessed)
  * recipes that need an item *aux/damage value Zenith's item model does not carry* (banner colour, bed colour,
    firework star colour, potion arrows, ...). They come back when stacks gain an aux field.
  * recipes of other stations (furnace, smithing, stonecutter, ...): other systems' data.

`--verify` compares against a CraftingData capture: every item a stock BDS can craft in a grid must be craftable here,
except those listed as aux-bearing. The capture is only an oracle for this check, never an input to the output.
"""
import argparse
import collections
import gzip
import json
import sys

import bds_pack

STATION = "crafting_table"

# Legacy item spellings still used by pack JSON -> the name in the current item registry.
ALIASES = {
    "minecraft:appleenchanted": "minecraft:enchanted_golden_apple",
    "minecraft:carrotonastick": "minecraft:carrot_on_a_stick",
    "minecraft:chain": "minecraft:iron_chain",
    "chain": "minecraft:iron_chain",
    "minecraft:chorus_fruit_popped": "minecraft:popped_chorus_fruit",
    "minecraft:darkoak_sign": "minecraft:dark_oak_sign",
    "minecraft:emptymap": "minecraft:empty_map",
    "minecraft:horsearmorleather": "minecraft:leather_horse_armor",
    "minecraft:melon": "minecraft:melon_slice",
    "minecraft:netherstar": "minecraft:nether_star",
    "minecraft:quartz_block:0": "minecraft:quartz_block",
    "minecraft:record_5": "minecraft:music_disc_5",
    "minecraft:reeds": "minecraft:sugar_cane",
    "minecraft:sealantern": "minecraft:sea_lantern",
    "minecraft:sign": "minecraft:oak_sign",
    "minecraft:speckled_melon": "minecraft:glistering_melon_slice",
    "minecraft:turtle_shell_piece": "minecraft:turtle_scute",
}

# Dye colours in the order Bedrock numbers them (wool/bed/banner colour index) and the dye item for each.
COLOURS = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan",
           "purple", "blue", "brown", "green", "red", "black"]
DYES = ["ink_sac", "red_dye", "green_dye", "cocoa_beans", "lapis_lazuli", "purple_dye", "cyan_dye", "light_gray_dye",
        "gray_dye", "pink_dye", "lime_dye", "yellow_dye", "light_blue_dye", "magenta_dye", "orange_dye", "bone_meal",
        "black_dye", "brown_dye", "blue_dye", "white_dye"]
WOODS = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "bamboo"]

# Items whose legacy `data` value selects a flattened item. Anything not listed here with data != 0 is unresolved.
DATA_NAMES = {
    "minecraft:dye": [f"minecraft:{n}" for n in DYES],
    "minecraft:boat": [f"minecraft:{w}_raft" if w == "bamboo" else f"minecraft:{w}_boat" for w in WOODS],
    "minecraft:chest_boat": [f"minecraft:{w}_chest_raft" if w == "bamboo" else f"minecraft:{w}_chest_boat" for w in WOODS],
    "minecraft:bucket": ["minecraft:bucket", "minecraft:milk_bucket"],
    "minecraft:banner_pattern": [f"minecraft:{n}_banner_pattern" for n in (
        "creeper", "skull", "flower", "mojang", "field_masoned", "bordure_indented")],
    "minecraft:carpet": [f"minecraft:{c}_carpet" for c in COLOURS],
    "minecraft:skull": ["minecraft:skeleton_skull", "minecraft:wither_skeleton_skull", "minecraft:zombie_head",
                        "minecraft:player_head", "minecraft:creeper_head", "minecraft:dragon_head",
                        "minecraft:piglin_head"],
    "minecraft:sandstone": ["minecraft:sandstone", "minecraft:chiseled_sandstone", "minecraft:cut_sandstone",
                            "minecraft:smooth_sandstone"],
    "minecraft:quartz_block": ["minecraft:quartz_block", "minecraft:chiseled_quartz_block", "minecraft:quartz_pillar"],
    "minecraft:purpur_block": ["minecraft:purpur_block", "minecraft:purpur_block", "minecraft:purpur_pillar"],
}

# Items whose pack `data` is a legacy facing/variant hint BDS itself discards (the wire says plain `name:0`).
IGNORED_DATA = {"minecraft:dispenser", "minecraft:dropper", "minecraft:piston", "minecraft:sticky_piston",
                "minecraft:bamboo_planks", "minecraft:mangrove_planks"}

# Every dye item that tints a colour: the plain dye plus the raw material that doubles as one.
COLOUR_DYES = {"white": ["white_dye", "bone_meal"], "orange": ["orange_dye"], "magenta": ["magenta_dye"],
               "light_blue": ["light_blue_dye"], "yellow": ["yellow_dye"], "lime": ["lime_dye"], "pink": ["pink_dye"],
               "gray": ["gray_dye"], "light_gray": ["light_gray_dye"], "cyan": ["cyan_dye"], "purple": ["purple_dye"],
               "blue": ["blue_dye", "lapis_lazuli"], "brown": ["brown_dye", "cocoa_beans"], "green": ["green_dye"],
               "red": ["red_dye"], "black": ["black_dye", "ink_sac"]}


class Unresolved(Exception):
    pass


def resolver(registry):
    def resolve(entry):
        name, data = entry["item"], entry.get("data", 0)
        name = ALIASES.get(name, name)
        if not name.startswith("minecraft:"):
            name = "minecraft:" + name
        if name in DATA_NAMES:
            table = DATA_NAMES[name]
            if data >= len(table):
                raise Unresolved(f"{name} data {data}")
            name = table[data]
        elif data and name not in IGNORED_DATA:
            raise Unresolved(f"aux:{name}")
        if name not in registry:
            raise Unresolved(f"unknown:{name}")
        return name
    return resolve


def ingredient(entry, resolve):
    if "tag" in entry:
        out = {"t": entry["tag"]}
    else:
        out = {"n": resolve(entry)}
    if entry.get("count", 1) != 1:
        out["c"] = entry["count"]
    return out


def from_pack(bds, registry, report):
    resolve = resolver(registry)
    recipes = {}  # by identifier: a later pack's recipe replaces an earlier one with the same identifier
    order = {p: i for i, p in enumerate(bds_pack.pack_order(bds))}
    files = sorted(bds_pack.load_category(bds, "recipes").items(), key=lambda kv: (order[kv[1][0]], kv[0]))
    for _, (_, blob) in files:
        for kind, body in json.loads(blob).items():
            if kind not in ("minecraft:recipe_shaped", "minecraft:recipe_shapeless"):
                continue
            rid = body["description"]["identifier"]
            if STATION not in body.get("tags", []):
                report["other_station"] += 1
                continue
            results = [body["result"]] if isinstance(body["result"], dict) else body["result"]
            try:
                outs = [{"n": resolve(r), "c": r.get("count", 1)} for r in results]
                if kind.endswith("shaped"):
                    pattern, key = body["pattern"], body["key"]
                    width = max(len(row) for row in pattern)
                    cells = []
                    for row in pattern:
                        for ch in row.ljust(width):
                            cells.append(None if ch == " " else ingredient(key[ch], resolve))
                    rec = {"id": rid, "w": width, "h": len(pattern), "sym": True, "in": cells, "out": outs,
                           "st": STATION}
                else:
                    rec = {"id": rid, "in": [ingredient(i, resolve) for i in body["ingredients"]], "out": outs,
                           "st": STATION}
            except Unresolved as e:
                report["unresolved:" + str(e).split(":")[0].split(" ")[0]] += 1
                report_detail.setdefault(str(e), []).append(rid)
                continue
            recipes[rid] = rec
    return list(recipes.values())


report_detail = {}


SLABS = {  # material -> slab: three in a row make six
    "cobblestone": "cobblestone_slab", "nether_brick": "nether_brick_slab", "sandstone": "sandstone_slab",
    "stone": "normal_stone_slab", "mossy_stone_bricks": "mossy_stone_brick_slab", "brick_block": "brick_slab",
    "stone_bricks": "stone_brick_slab", "smooth_stone": "smooth_stone_slab", "red_sandstone": "red_sandstone_slab",
    "prismarine_bricks": "prismarine_brick_slab", "dark_prismarine": "dark_prismarine_slab",
    "mossy_cobblestone": "mossy_cobblestone_slab", "red_nether_brick": "red_nether_brick_slab",
    "smooth_sandstone": "smooth_sandstone_slab", "andesite": "andesite_slab", "diorite": "diorite_slab",
    "granite": "granite_slab", "polished_granite": "polished_granite_slab",
    "polished_andesite": "polished_andesite_slab", "polished_diorite": "polished_diorite_slab",
    "smooth_red_sandstone": "smooth_red_sandstone_slab", "cut_red_sandstone": "cut_red_sandstone_slab",
    "cut_sandstone": "cut_sandstone_slab", "smooth_quartz": "smooth_quartz_slab", "quartz_block": "quartz_slab",
    "prismarine": "prismarine_slab", "end_bricks": "end_stone_brick_slab", "purpur_block": "purpur_slab",
}
CHISELED = {"quartz_slab": "chiseled_quartz_block", "stone_brick_slab": "chiseled_stone_bricks",
            "red_sandstone_slab": "chiseled_red_sandstone", "sandstone_slab": "chiseled_sandstone",
            "purpur_slab": "purpur_pillar"}
WOOD_SETS = {"oak": ("wooden_button", "wooden_pressure_plate", "trapdoor"), "spruce": None, "birch": None,
             "jungle": None, "acacia": None, "dark_oak": None}


def code_recipes():
    """Vanilla recipes BDS registers in code instead of shipping as pack JSON (slabs, small wood parts, ...)."""
    out = []

    def shaped(rid, w, h, cells, result, count=1):
        out.append({"id": rid, "w": w, "h": h, "sym": True, "st": STATION,
                    "in": [None if c is None else {"n": f"minecraft:{c}"} for c in cells],
                    "out": [{"n": f"minecraft:{result}", "c": count}]})

    for material, slab in SLABS.items():
        shaped(f"{slab}_from_{material}", 3, 1, [material] * 3, slab, 6)
    for slab, block in CHISELED.items():
        shaped(f"{block}_from_{slab}", 1, 2, [slab] * 2, block)
    for wood, names in WOOD_SETS.items():
        button, plate, trapdoor = names or (f"{wood}_button", f"{wood}_pressure_plate", f"{wood}_trapdoor")
        planks = f"{wood}_planks"
        shaped(f"{button}_from_{planks}", 1, 1, [planks], button)
        shaped(f"{plate}_from_{planks}", 2, 1, [planks] * 2, plate)
        shaped(f"{trapdoor}_from_{planks}", 3, 2, [planks] * 6, trapdoor, 2)
    for chiseled, slab in (("chiseled_sandstone", "sandstone_slab"), ("chiseled_red_sandstone", "red_sandstone_slab")):
        shaped(f"{slab}_from_{chiseled}", 3, 1, [chiseled] * 3, slab, 6)
    out.append({"id": "firework_rocket_from_paper", "st": STATION,
                "in": [{"n": "minecraft:paper"}, {"n": "minecraft:gunpowder"}],
                "out": [{"n": "minecraft:firework_rocket", "c": 3}]})
    shaped("stick_from_bamboo", 1, 2, ["bamboo"] * 2, "stick")
    shaped("decorated_pot_from_bricks", 3, 3, [None, "brick", None, "brick", None, "brick", None, "brick", None],
           "decorated_pot")
    return out


def generated(registry):
    """Dyeing families BDS builds in code. A coloured block + any other dye -> that colour."""
    out = []

    def family(prefix, member, undyed=None):
        sources = [member(c) for c in COLOURS] + ([undyed] if undyed else [])
        for target in COLOURS:
            result = member(target)
            for source in sources:
                if source == result:
                    continue
                for dye in COLOUR_DYES[target]:
                    rid = f"{prefix}_{target}_from_{source.split(':')[1]}_with_{dye}"
                    out.append({"id": rid, "in": [{"n": f"minecraft:{dye}"}, {"n": source}],
                                "out": [{"n": result, "c": 1}], "st": STATION})
    family("dye_wool", lambda c: f"minecraft:{c}_wool")
    family("dye_wool_slab", lambda c: f"minecraft:{c}_wool_slab")
    family("dye_wool_stairs", lambda c: f"minecraft:{c}_wool_stairs")
    family("dye_shulker_box", lambda c: f"minecraft:{c}_shulker_box", undyed="minecraft:undyed_shulker_box")
    family("dye_bundle", lambda c: f"minecraft:{c}_bundle", undyed="minecraft:bundle")
    family("dye_cushion", lambda c: f"minecraft:{c}_cushion")
    family("dye_harness", lambda c: f"minecraft:{c}_harness")
    for c in COLOURS:  # a harness is made from a wool of its colour
        out.append({"id": f"harness_{c}", "w": 3, "h": 2, "sym": False, "st": STATION,
                    "in": [{"n": "minecraft:leather"}] * 3 + [{"n": "minecraft:glass"}, {"n": f"minecraft:{c}_wool"},
                                                              {"n": "minecraft:glass"}],
                    "out": [{"n": f"minecraft:{c}_harness", "c": 1}]})
    out += code_recipes()
    for r in out:
        for c in filter(None, r["in"]):
            assert c["n"] in registry, f"generated recipe {r['id']} uses unknown item {c['n']}"
        assert r["out"][0]["n"] in registry, f"generated recipe {r['id']} makes unknown item {r['out'][0]['n']}"
    return out


def canonical(recipe):
    """Order/representation-independent identity of a recipe: station-agnostic shape, ingredients and result."""
    def cell(c):
        return None if c is None else ("t", c["t"]) if "t" in c else ("n", c["n"])

    def expand(c):
        return [cell(c)] * c.get("c", 1)

    outs = tuple((o["n"], o["c"]) for o in recipe["out"])
    if "w" in recipe:
        return ("shaped", recipe["w"], recipe["h"], tuple(cell(c) for c in recipe["in"]), outs)
    cells = [x for c in recipe["in"] for x in expand(c)]
    return ("shapeless", tuple(sorted(cells, key=str)), outs)


# Items whose colour/variant lives in an aux value Zenith's item model does not carry (even when that value is 0).
AUX_ITEMS = {"minecraft:bed", "minecraft:banner", "minecraft:firework_star", "minecraft:arrow", "minecraft:planks",
             "minecraft:empty_map", "minecraft:suspicious_stew"}


def wire_recipes(capture, names):
    """CraftingData grid recipes as emitter-shaped dicts; ones needing an aux value are returned separately."""
    wire = json.load(open(f"{capture}/crafting_data.json"))
    plain, aux = [], []
    for kind in ("shaped_recipes", "shapeless_recipes", "shulker_box_recipes"):
        for r in wire[kind]:
            if r["block"] not in ("crafting_table", ""):
                continue
            needs_aux = False
            cells = []
            for i in r["input"]:
                if i["type"] == "invalid":
                    cells.append(None)
                    continue
                if i["descriptor_type"] == "item_tag":
                    c = {"t": i["tag"]}
                else:
                    name = i["name"]
                    head, _, tail = name.rpartition(":")
                    if tail.isdigit() and head.count(":") >= 1:  # BDS writes "name:aux" for some ingredients
                        needs_aux |= tail != "0"
                        name = head
                    needs_aux |= i["metadata"] not in (0, 32767) and not name.endswith(":0")
                    c = {"n": name}
                if i["count"] != 1:
                    c["c"] = i["count"]
                cells.append(c)
            outs = []
            for o in r["output"]:
                needs_aux |= o["metadata"] != 0
                outs.append({"n": names[o["network_id"]], "c": o["count"]})
            needs_aux |= any(c and c.get("n") in AUX_ITEMS for c in cells) or any(o["n"] in AUX_ITEMS for o in outs)
            rec = {"id": r["recipe_id"], "in": cells, "out": outs}
            if "width" in r:
                rec.update(w=r["width"], h=r["height"])
            else:
                rec["in"] = [c for c in cells if c is not None]
            (aux if needs_aux else plain).append(rec)
    return plain, aux


def verify(capture, recipes, names):
    plain, aux = wire_recipes(capture, names)
    ours = {canonical(r) for r in recipes}
    missing = [r for r in plain if canonical(r) not in ours]
    print(f"verify: {len(plain)} aux-free wire grid recipes; {len(missing)} have no equivalent here; "
          f"{len(aux)} need an aux model (skipped by design)")
    wire_keys = {canonical(r) for r in plain}
    extra = [r for r in recipes if canonical(r) not in wire_keys]
    print(f"verify: {len(extra)} emitted recipes have no aux-free equivalent on the wire"
          + (f", e.g. {[r['id'] for r in extra[:8]]}" if extra else ""))
    for r in missing[:int(__import__("os").environ.get("VMAX", "40"))]:
        print("   missing:", r["id"], [c and (c.get("n") or c.get("t")) for c in r["in"]], "->", r["out"])
    return len(missing)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("bds")
    ap.add_argument("item_registry")
    ap.add_argument("output")
    ap.add_argument("--verify")
    a = ap.parse_args()
    states = json.load(open(a.item_registry))["itemstates"]
    registry = {e["name"] for e in states}
    report = collections.Counter()

    recipes = from_pack(a.bds, registry, report) + generated(registry)
    seen, unique = set(), []
    for r in recipes:
        if r["id"] in seen:
            raise SystemExit(f"duplicate recipe id {r['id']}")
        seen.add(r["id"])
        unique.append(r)

    shaped = [r for r in unique if "w" in r]
    shapeless = [r for r in unique if "w" not in r]
    doc = {"source": "BDS behaviour packs (layered vanilla*)", "shaped": shaped, "shapeless": shapeless}
    with gzip.GzipFile(a.output, "wb", mtime=0) as f:
        f.write(json.dumps(doc, separators=(",", ":"), sort_keys=True).encode())
    print(f"shaped={len(shaped)} shapeless={len(shapeless)} not emitted={dict(report)}", file=sys.stderr)
    for reason, ids in sorted(report_detail.items()):
        print(f"  {reason}: {len(ids)} recipe(s), e.g. {ids[0]}", file=sys.stderr)
    if a.verify:
        sys.exit(1 if verify(a.verify, unique, {e["runtime_id"]: e["name"] for e in states}) else 0)


if __name__ == "__main__":
    main()
