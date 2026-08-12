# Protocol import pipeline

`tools/protocol-import` is an offline audit/scaffolding tool. It never changes packet sources
on `pull`, and it does not make Tier B packets generated. Packet wire ownership stays in
`Packets/`; Protocol, Session, RakNet and gameplay are out of scope.

## Deterministic cache

`protocol-import pull --source mojang --ref r/26_u4` resolves the ref to a commit SHA before it
downloads files. It writes a complete immutable snapshot under the source cache, hashes every
file, then atomically switches the active manifest pointer. A reader consequently sees either a
previous complete snapshot or a new complete snapshot. `protocol-import report` shows the
requested ref, resolved SHA, file count and any missing/hash-mismatched file.

Caches created before the manifest format are readable for offline compatibility, but are marked
legacy/unverified. Pull again before using one as bump evidence.

## Coverage policy

`protocol-import report --source mojang` reports local generated packet files, local manual
packet files, source arrays needing capability review, and RED constructs requiring manual
handling. RED is evidence, not a failure to force codegen: unions, unknown schema shapes,
conversions, validation, NBT/JSON and complex control flow remain Tier B under ADR §76.

The schema IR preserves a field's construct (`Scalar`, `Array`, `Union`, `Constant`, `Unknown`),
reference where available, and an explicit unsupported reason. A generator capability may be
added only after at least two real users justify it; no packet-name-specific generation is
allowed.

## Current gap classes

- primitive arrays need a general array codec before they can be generated;
- nested arrays/types beyond the existing simple `Read`/`Write` shape need a deliberate generic
  representation;
- unions/discriminators and prose-only conditional presence stay manual until upstream encodes
  enough machine-readable information and more than one real packet needs the capability.

## Bump diff

Pull both references once, then run `protocol-import diff --source mojang --from r/26_u3 --to
r/26_u4`. The command compares their immutable manifests, reports added/removed packets and
field-level additions, removals, optionality and wire-shape changes. Scalar changes are GREEN;
arrays/references and removals are YELLOW; unions or unknown shapes are RED. It does not generate
or replace a packet. The older `diff <packet> --file <path>` form remains the local Tier A check.

The smallest useful protocol-bump workflow is: pull pinned snapshots, inspect `report`, run the
snapshot `diff`, then review the named RED/YELLOW constructs rather than attempting to regenerate
all packets.

## Guided upgrade

`protocol-import upgrade --source mojang --from r/26_u3 --to r/26_u4` validates both manifests,
runs the same deterministic snapshot diff, and prints one migration plan: added/removed/changed
packets, generated impact, required human review, manual Tier B work and recommended actions.
It returns non-zero when RED work exists or either snapshot is inconsistent. It is read-only:
developers choose and review any Tier A regeneration afterwards.
