"""How much of what BDS sends in CraftingData comes straight from its behaviour-pack recipe JSON?

Usage: python pack_recipe_coverage.py <bds root> <capture dir>

Reads the layered `recipes` catalog (see bds_pack.py) and compares it with a CraftingData capture (the oracle).
Prints, for the crafting-grid recipes the wire carries:
  * clean      - resolvable from the pack alone (names exist in the item registry, no legacy aux/data needed)
  * aux/legacy - pack recipe that needs BDS's built-in legacy (name, data) -> flattened-item table (lives in the exe)
  * wire-only  - sent on the wire but not in any pack file: generated in code (colour-variant dyeing, ...)
It is a measurement, not an importer: shipped data is still extract_recipes.py (capture) until the gaps close.
"""
import collections
import json
import re
import sys

import bds_pack

GRID = {"crafting_table", "deprecated", ""}


def main(bds, capture):
    recipes = bds_pack.load_category(bds, "recipes")
    registry = {e["name"] for e in json.load(open(f"{capture}/item_registry.json"))["itemstates"]}
    wire = json.load(open(f"{capture}/crafting_data.json"))
    grid_ids = {r["recipe_id"] for k in ("shaped_recipes", "shapeless_recipes") for r in wire[k] if r["block"] in GRID}

    def known(name):
        return name in registry or f"minecraft:{name}" in registry

    status = {}
    for _, (_, blob) in recipes.items():
        for kind, body in json.loads(blob).items():
            if kind not in ("minecraft:recipe_shaped", "minecraft:recipe_shapeless"):
                continue
            entries = list(body["key"].values()) if "key" in body else body["ingredients"]
            result = body["result"] if isinstance(body["result"], dict) else body["result"][0]
            state = "clean"
            for e in entries + [result]:
                if "item" in e and not known(e["item"]):
                    state = "legacy"
                elif "item" in e and e.get("data", 0) and state == "clean":
                    state = "aux"
            status[body["description"]["identifier"]] = state

    count = collections.Counter(status.values())
    in_wire = {s: sum(1 for i, st in status.items() if st == s and i in grid_ids) for s in count}
    wire_only = sorted(grid_ids - set(status))
    families = collections.Counter(re.sub(r"[A-Z][a-z]+|\d+", "*", i)[:28] for i in wire_only)
    print(f"pack grid recipes: {dict(count)} (of which on the wire: {in_wire})")
    print(f"wire grid recipes: {len(grid_ids)}; wire-only (generated in code): {len(wire_only)}")
    for fam, n in families.most_common(10):
        print(f"  {n:5d}  {fam}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
