# ADR §1 — Own the stack from UDP up — don't wrap a PHP/Java runtime

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §1`.

**Choice:** C# / .NET with in-repo `raknet`, then game code on top.

**Why:** Bedrock is a binary protocol problem first. Controlling the transport and decode path (`BinaryStream`, FrameSet, reliability) avoids fighting an alien GC and FFI story for hot paths. Commits from late 2024 are almost entirely RakNet until login works — that order was deliberate.
