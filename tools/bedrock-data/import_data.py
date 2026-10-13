"""Import a verified BDS/Endstone data bundle; no network access or server boot."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import struct


def text(value):
    encoded = value.encode("utf-8")
    return struct.pack(">H", len(encoded)) + encoded


def state_compound(states):
    result = bytearray()
    for name, state in states.items():
        kind, value = state["type"], state["value"]
        if kind == "byte":
            tag, body = 1, struct.pack(">b", int(value))
        elif kind == "int":
            tag, body = 3, struct.pack(">i", value)
        elif kind == "string":
            tag, body = 8, text(value)
        else:
            raise ValueError(f"Unsupported block state type: {kind}")
        result += bytes([tag]) + text(name) + body
    return bytes(result) + b"\0"


def block_palette(blocks):
    result = bytearray(b"\x0a\0\0\x09" + text("blocks") + b"\x0a" + struct.pack(">i", len(blocks)))
    for block in blocks:
        result += b"\x03" + text("network_id") + struct.pack(">i", block["network_id"])
        result += b"\x08" + text("name") + text(block["name"])
        result += b"\x0a" + text("states") + state_compound(block["states"]) + b"\0"
    return gzip.compress(bytes(result) + b"\0", mtime=0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, default=Path("src/zenith/data"))
    parser.add_argument("--bedrock-version", required=True)
    parser.add_argument("--protocol", required=True, type=int)
    parser.add_argument("--bds-sha256", required=True)
    parser.add_argument("--endstone-version", required=True)
    args = parser.parse_args()
    export = json.loads((args.source / "server/exported-blocks.json").read_text(encoding="utf-8"))
    client_version = ".".join(args.bedrock_version.split(".")[:3])
    exported_version = export["minecraft_version"]
    if not exported_version.startswith("1."):
        exported_version = "1." + exported_version
    if exported_version != client_version or export["errors"]:
        raise ValueError("Block export version differs or unresolved export errors remain.")
    capture = args.source / "capture"
    items = json.loads((capture / "item_palette.json").read_text(encoding="utf-8"))["items"]
    creative = json.loads((capture / "creative_items.json").read_text(encoding="utf-8"))
    if len({item["name"] for item in items}) != len(items):
        raise ValueError("Duplicate item names in the captured registry.")
    captured_items = json.loads((capture / "item_registry-decoded.json").read_text(encoding="utf-8"))["itemstates"]
    defined_names = {item["name"] for item in captured_items if item["nbt"]["value"]}
    imported_names = {item["name"] for item in items if item.get("component_nbt_b64")}
    if defined_names != imported_names:
        raise ValueError("The imported component definitions differ from the captured registry.")
    assets = {
        "block_palette.nbt": block_palette(export["blocks"]),
        "item_palette.json": (json.dumps({"items": items}, indent=2) + "\n").encode(),
        "creative_items.json": (json.dumps(creative, indent=2) + "\n").encode(),
        "biome_definitions.bin": (capture / "biome_definitions.bin").read_bytes(),
    }
    if any(not payload for payload in assets.values()):
        raise ValueError("An imported asset is empty.")
    manifest = {
        "minecraft_version": client_version,
        "protocol": args.protocol,
        "source": {
            "bds_version": args.bedrock_version,
            "bds_url": f"https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-{args.bedrock_version}.zip",
            "bds_archive_sha256": args.bds_sha256,
            "endstone_version": args.endstone_version,
            "block_candidates": "previous Zenith palette plus minecraft-data 3.117.0 block states; new types from the captured BDS ItemRegistry",
            "block_validation": "each state and runtime ID queried from BDS via Endstone create_block_data; new state domains accepted only when returned unchanged",
        },
        "counts": {
            "block_states": len(export["blocks"]),
            "block_types": len({block["name"] for block in export["blocks"]}),
            "items": len(items),
            "item_component_definitions": sum(bool(item.get("component_nbt_b64")) for item in items),
            "creative_groups": len(creative["groups"]),
            "creative_items": len(creative["items"]),
            "biomes": first_varuint(assets["biome_definitions.bin"]),
        },
        "assets": {name: {"bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()} for name, payload in assets.items()},
        "captured_packet_bodies": {name: hashlib.sha256((capture / (name + ".bin")).read_bytes()).hexdigest() for name in ("item_registry", "creative_content", "biome_definitions")},
    }
    args.output.mkdir(parents=True, exist_ok=True)
    for name, payload in assets.items():
        (args.output / name).write_bytes(payload)
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["counts"], indent=2))


def first_varuint(payload):
    value = 0
    for index, byte in enumerate(payload[:5]):
        value |= (byte & 127) << (7 * index)
        if byte < 128:
            return value
    raise ValueError("Invalid leading biome array count.")


if __name__ == "__main__":
    main()
