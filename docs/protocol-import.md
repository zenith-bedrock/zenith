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

`protocol-import coverage --source mojang` reports schema coverage, local generated packet files, local manual
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
It returns non-zero when RED work exists or either snapshot is inconsistent. Without `--apply-green`,
it does not edit packet sources. It also writes a durable report to
`docs/protocol-upgrades/<target-ref>.md` (or `--report <FILE>`) with both immutable snapshots,
changed packets, decisions and manual actions. GREEN identifies candidates for scaffolding or
attribute/test updates; YELLOW is suggestion-only and RED remains blocked from automation.

Use `--apply-green` only after reviewing the report. It writes a non-overwriting partial
`<Packet>.ProtocolUpgrade.g.cs` and a basic decode test only for an **added scalar field** on an
existing `[GamePacket]` class. It never touches manual packets, arrays, unions, constants or an
existing generated-upgrade file; those remain suggestions/manual work.

## Continuous governance

`protocol-import compatibility --source mojang --json` writes one machine-readable summary for
CI or release evidence: the server Bedrock version (read from `ServerIdentity` unless
`--protocol` is supplied), scalar schema coverage, generated/manual packet counts and ratio,
RED/YELLOW counts **and items**, and whether a generated scalar packet is stale against the verified
snapshot. It returns non-zero only when the snapshot itself is invalid.

`protocol-import validate --source mojang --json` is the quality gate. It fails on a missing or
hash-inconsistent manifest, an empty cache, or a generated `[GamePacket]` whose known scalar
schema field set no longer matches. It intentionally does **not** fail merely because YELLOW or
RED exists: those are known capability/manual work and are reported by compatibility instead of
being misrepresented as a codegen defect. Run `pull` for the pinned reference first; the cache is
local by design and is not silently downloaded by validation.

## Contributor workflow

The normal order is intentionally explicit. The importer never updates itself from the network
during `report`, `diff`, `upgrade`, `compatibility` or `validate`:

```powershell
# 1. Capture the current and candidate schemas once, by immutable commit SHA.
dotnet run --project tools/protocol-import -- pull --source mojang --ref r/26_u3
dotnet run --project tools/protocol-import -- pull --source mojang --ref r/26_u4

# 2. Confirm provenance/capability before deciding a migration.
dotnet run --project tools/protocol-import -- report --source mojang
dotnet run --project tools/protocol-import -- coverage --source mojang

# 3. Inspect the schema delta, then write the durable upgrade record.
dotnet run --project tools/protocol-import -- diff --source mojang --from r/26_u3 --to r/26_u4
dotnet run --project tools/protocol-import -- upgrade --source mojang --from r/26_u3 --to r/26_u4

# 4. Before review/CI, enforce the verified snapshot and generated scalar contract.
dotnet run --project tools/protocol-import -- validate --source mojang --json
```

Use `upgrade` whenever changing the Bedrock reference/version, even when the apparent packet
delta is small: its committed report records exactly which snapshot, GREEN/YELLOW/RED decisions
and manual actions were reviewed. Use a one-packet `diff <packet> --file <path>` for focused
review of a packet already migrated to `[GamePacket]`.

Do **not** use `--apply-green` as a general packet migration switch. It is appropriate only after
reviewing the upgrade report and only for a new scalar field on an existing generated packet. Do
not automate Tier B packets, arrays/references, unions, constants, validation/conversion rules,
NBT/JSON shapes or a file which already has upgrade output. In all of these cases, let the command
produce the suggestion/report and make the packet change by hand with a wire round-trip test.

## CI examples

CI should pin and pull the source reference first, then treat the JSON output as a build artifact
or policy input. It must not use a developer's unverified `.cache`:

```yaml
- name: Prepare protocol snapshot
  run: dotnet run --project tools/protocol-import -- pull --source mojang --ref r/26_u4 --cache "$RUNNER_TEMP/protocol-cache"

- name: Validate protocol import contract
  run: dotnet run --project tools/protocol-import -- validate --source mojang --cache "$RUNNER_TEMP/protocol-cache" --json

- name: Emit compatibility evidence
  run: dotnet run --project tools/protocol-import -- compatibility --source mojang --cache "$RUNNER_TEMP/protocol-cache" --json > protocol-compatibility.json
```

`validate` exits `0` only for a manifest-valid, nonempty schema snapshot whose local generated
scalar fields match; it exits `1` for invalid cache/manifest or generated drift. `compatibility`
also exits `1` if its snapshot is invalid, but RED/YELLOW alone remain data in its JSON because
they are deliberate manual/capability classifications, not a reason to pretend all packets are
generatable.
