# ADR §156 — Expose registered recipes and vanilla creative content

The grid recipe registry already loads the bundled vanilla recipes, but join only
unlocks BDS's starter list and never advances discovery. The creative catalog also
filters bundled entries through a small DigProfiles/Tools allowlist. These policies
hide available content independently of whether the client can display it.

Until progressive discovery is implemented, initialize the recipe book with all
registered grid recipes. Crafting still validates recipes, ingredients, shape and
station on the gameplay tick. This is an explicit Zenith discovery policy, not a
claim to reproduce BDS discovery progression or additional workstation mechanics.

Expose the bundled creative entries and groups with their actual block state,
auxiliary variant and item NBT. Keep existing pinned net IDs for their first exact
identity and assign distinct IDs for the remaining entries. Inventory identity,
wire projection and persistence must preserve NBT before those entries are exposed;
enchanted books must not collapse into interchangeable blank books. ItemUserData
retains an immutable serialized tag value alongside modeled wear; this preserves
metadata without claiming to implement every enchantment or special-item mechanic.
Introduce a variable-length slot-blob version for metadata stacks while retaining
read compatibility with v1-v4 and the existing v4 encoding for ordinary stacks.

Catalog visibility does not certify block physics, station behavior or every item
interaction. Those are separate gameplay capabilities, not reasons to silently
replace creative identities or indefinitely hide the recipe registry.

Verification: catalog-to-source coverage, aux/NBT distinctions, creative grant to
inventory, metadata persistence and wire projection, legacy blobs, and full recipe
unlock coverage. No copied Cinnabar code or fixtures; use Zenith's bundled data.
