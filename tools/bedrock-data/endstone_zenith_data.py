"""Offline reference exporter; loaded only in an isolated Endstone/BDS instance."""
import json
from itertools import product
from pathlib import Path

import endstone
from endstone.plugin import Plugin


class DataExportPlugin(Plugin):
    api_version = "0.11"
    load = "POSTWORLD"

    def on_enable(self):
        candidates = json.loads(Path("block-candidates.json").read_text(encoding="utf-8"))
        domains_by_key = {"minecraft:corner": ["none", "inner_left", "inner_right", "outer_left", "outer_right"]}
        for candidate in candidates:
            for key, state in candidate["states"].items():
                domain = domains_by_key.setdefault(key, [])
                if state["value"] not in domain:
                    domain.append(state["value"])
        known_names = {candidate["name"].removeprefix("minecraft:") for candidate in candidates}
        itemstates = json.loads(Path("item-candidates.json").read_text(encoding="utf-8"))["itemstates"]
        new_types = []
        for item in itemstates:
            name = item["name"]
            if name.removeprefix("minecraft:") in known_names:
                continue
            try:
                block = self.server.create_block_data(name)
            except Exception:
                continue  # a vanilla item need not have a block type
            if block.type != name:
                continue
            known_names.add(name.removeprefix("minecraft:"))
            candidates.append({"name": name, "states": {}})
            new_types.append(name)
        blocks, errors = {}, []
        for candidate in candidates:
            name = candidate["name"]
            if not name.startswith("minecraft:"):
                name = "minecraft:" + name
            states = {
                key: bool(state["value"]) if state["type"] == "byte" else state["value"]
                for key, state in candidate["states"].items()
            }
            try:
                actual = self.server.create_block_data(name, states).block_states
                if any(actual.get(key) != value for key, value in states.items()):
                    raise ValueError(f"requested states changed: {actual!r}")
                added = {key: value for key, value in actual.items() if key not in states}
                domains = []
                for key, value in added.items():
                    if isinstance(value, bool):
                        domains.append((False, True))
                    elif key in domains_by_key:
                        domains.append(tuple(dict.fromkeys([value, *domains_by_key[key]])))
                    else:
                        raise ValueError(f"unhandled new state: {key}={value!r}")
                for values in product(*domains):
                    variant = dict(states, **dict(zip(added, values)))
                    try:
                        block = self.server.create_block_data(name, variant)
                    except ValueError:
                        continue  # unsupported enum values are excluded, never assigned an ID
                    if block.block_states != variant:
                        continue
                    typed = {
                        key: {"type": "byte" if isinstance(value, bool) else "int" if isinstance(value, int) else "string", "value": int(value) if isinstance(value, bool) else value}
                        for key, value in variant.items()
                    }
                    identity = (name, json.dumps(typed, sort_keys=True))
                    network_id = block.runtime_id
                    if network_id >= 1 << 31:
                        network_id -= 1 << 32
                    blocks[identity] = {"name": name, "states": typed, "network_id": network_id}
            except Exception as error:
                errors.append({"name": name, "states": states, "error": str(error)})
        output = {"minecraft_version": endstone.__minecraft_version__, "blocks": list(blocks.values()), "new_types": new_types, "errors": errors}
        Path("exported-blocks.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
        self.logger.info(f"Block data export: {len(blocks)} valid states, {len(errors)} errors")
