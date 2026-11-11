"""Turns a CraftingData capture from a real BDS into Zenith's embedded `recipes.json.gz`.

Input  : <capture>/crafting_data.json and <capture>/item_registry.json, both decoded from the packets a
         stock BDS sends at login (see README, "Recipes"). The capture is the source of truth because it is
         exactly what a real client is told: BDS has already expanded tags/colour variants and assigned
         the descriptors the client matches against. The behaviour-pack JSON in `recipes.brarchive` is NOT
         what goes on the wire (legacy names, un-expanded), so it is not used.
Output : gzip'd JSON with only the recipes a crafting grid can satisfy (stations `crafting_table` and
         `deprecated`); furnace/stonecutter/etc. are other systems' data.

Everything keyed by item NAME, never by BDS numeric ids: ids come from Zenith's own palette at load time.
Recipes using an ingredient/aux form Zenith's matcher does not model are dropped and counted, not guessed.
"""
import argparse, gzip, json, sys, collections

GRID_STATIONS = {"crafting_table", "deprecated", ""}


def ingredient(i, skipped):
    t = i.get("type")
    if t == "invalid":
        return None  # empty cell
    d = i["descriptor_type"]
    meta = i.get("metadata", 0)
    if meta not in (0, 32767):
        skipped["specific_aux"] += 1
        raise ValueError
    if d == "name":
        out = {"n": i["name"]}
    elif d == "item_tag":
        out = {"t": i["tag"]}
    else:
        skipped["descriptor_" + d] += 1
        raise ValueError
    if i.get("count", 1) != 1:
        out["c"] = i["count"]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("output")
    a = ap.parse_args()
    cd = json.load(open(f"{a.capture}/crafting_data.json"))
    reg = json.load(open(f"{a.capture}/item_registry.json"))["itemstates"]
    name_of = {e["runtime_id"]: e["name"] for e in reg}
    skipped = collections.Counter()
    shaped, shapeless = [], []

    def outputs(r):
        res = []
        for o in r["output"]:
            if o["extra"].get("has_nbt"):
                skipped["output_nbt"] += 1
                raise ValueError
            e = {"n": name_of[o["network_id"]], "c": o["count"]}
            if o["metadata"]:
                e["a"] = o["metadata"]
            if o["block_runtime_id"]:
                e["b"] = o["block_runtime_id"]
            res.append(e)
        return res

    for kind, lists in (("shaped", ["shaped_recipes"]), ("shapeless", ["shapeless_recipes", "shulker_box_recipes"])):
        for lst in lists:
            for r in cd[lst]:
                if r["block"] not in GRID_STATIONS:
                    skipped["station_" + r["block"]] += 1
                    continue
                try:
                    cells = [ingredient(i, skipped) for i in r["input"]]
                    rec = {"id": r["recipe_id"], "out": outputs(r), "st": r["block"] or "crafting_table"}
                except (ValueError, KeyError):
                    skipped["dropped"] += 1
                    continue
                if kind == "shaped":
                    rec.update(w=r["width"], h=r["height"], sym=bool(r.get("assume_symmetry")), in_=cells)
                    shaped.append(rec)
                else:
                    rec["in_"] = [c for c in cells if c is not None]
                    shapeless.append(rec)

    for r in shaped + shapeless:
        r["in"] = r.pop("in_")
    doc = {"source": "BDS CraftingData capture", "shaped": shaped, "shapeless": shapeless}
    with gzip.GzipFile(a.output, "wb", mtime=0) as f:
        f.write(json.dumps(doc, separators=(",", ":"), sort_keys=True).encode())
    print(f"shaped={len(shaped)} shapeless={len(shapeless)} skipped={dict(skipped)}", file=sys.stderr)


main()
