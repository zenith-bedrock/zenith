# ADR §2 — `BinaryStream` as `ref struct`

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §2`.

**Choice:** Migrate readers/writers to `ref struct` and thread `ref` through Decode APIs (`d63e7b2`).

**Why:** Hot packet paths allocate less and make lifetime mistakes visible at compile time. Cost: API churn once — paid early so every later packet inherits the discipline.
