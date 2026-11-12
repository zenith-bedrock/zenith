"""Reader for a stock BDS install's layered behaviour packs (`behavior_packs/vanilla*`).

How BDS itself loads its data catalogs (observed, not copied from anything):
  * `behavior_packs/` holds a `vanilla` baseline plus one pack per game version (`vanilla_1.14` ... `vanilla_1.26.63`).
  * Packs apply in version order; a file with the same name in a later pack REPLACES the earlier one.
  * Files ship inside `.brarchive` containers: 8-byte magic 7d2725b1a0527026, u32 count, u32 version, then `count`
    256-byte records (u8 name length, name, zero pad, u32 offset, u32 size; offset is relative to the data area that
    starts right after the table), then the blobs.
Sibling loose files in a pack directory are read too, so a plain pack on disk works the same way.
"""
import os
import re
import struct

MAGIC = bytes.fromhex("7d2725b1a0527026")
RECORD = 256
HEADER = 16


def read_brarchive(path):
    data = open(path, "rb").read()
    if data[:8] != MAGIC:
        raise ValueError(f"{path}: not a brarchive")
    (count,) = struct.unpack_from("<I", data, 8)
    base = HEADER + count * RECORD
    out = {}
    for i in range(count):
        rec = data[HEADER + i * RECORD: HEADER + (i + 1) * RECORD]
        name = rec[1:1 + rec[0]].decode("utf-8")
        off, size = struct.unpack("<II", rec[-8:])
        out[name] = data[base + off: base + off + size]
    return out


def _version_key(pack):
    m = re.search(r"_(\d+(?:\.\d+)*)$", pack)
    return tuple(int(x) for x in m.group(1).split(".")) if m else ()


def pack_order(bds_root):
    """`vanilla` first, then versioned packs ascending. Chemistry/editor/experimental packs are not part of base data."""
    root = os.path.join(bds_root, "behavior_packs")
    packs = [d for d in os.listdir(root) if d == "vanilla" or re.fullmatch(r"vanilla_\d+(\.\d+)*", d)]
    return sorted(packs, key=lambda p: (p != "vanilla", _version_key(p)))


def load_category(bds_root, category):
    """Merged `{file name: (pack, bytes)}` for one catalog (`recipes`, `items`, `item_catalog`, `biomes`, ...)."""
    root = os.path.join(bds_root, "behavior_packs")
    merged = {}
    for pack in pack_order(bds_root):
        for archive in (os.path.join(root, pack, "__brarchive", f"{category}.brarchive"),
                        os.path.join(root, pack, f"{category}.brarchive")):
            if os.path.exists(archive):
                for name, blob in read_brarchive(archive).items():
                    merged[name] = (pack, blob)
        loose = os.path.join(root, pack, category)
        if os.path.isdir(loose):
            for name in os.listdir(loose):
                p = os.path.join(loose, name)
                if os.path.isfile(p):
                    merged[name] = (pack, open(p, "rb").read())
    return merged
