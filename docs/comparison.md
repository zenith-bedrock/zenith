# Comparison

Honest positioning for developers choosing a Bedrock stack. Zenith is **early**; comparisons emphasize architecture and DX trajectory, not feature parity checklists.

## Snapshot

| | **Zenith** | **PocketMine-MP** | **Nukkit / NukkitX family** | **Bedrock Dedicated Server (BDS)** | **Go / V indie stacks** (e.g. Dragonfly-adjacent, research ports) |
|--|------------|-------------------|----------------------------|-------------------------------------|------------------------------------------------------------------|
| Language | C# (.NET) | PHP | Java | C++ (closed core) | Go / V |
| Transport | First-party RakNet | PM / raklib lineage | First/third-party UDP stacks | Mojang | Often first-party |
| Extensibility model | None yet (intentional freeze) | Plugins (PHP) | Plugins (Java) | Behavior packs / limited | Libraries / forks |
| Hot-path control | Full source in-repo | High, but PHP runtime | High on JVM | Black box | High |
| World storage | Owned managed LevelDB (`c:`/`ov:`) | Region/format PM uses | Java LevelDB variants | Mojang LevelDB | Varies |
| Maturity | Early / not production | Mature ecosystem | Mature forks | Production official | Varies |
| License | LGPL-3.0 | Mixed/open (project-specific) | Open (project-specific) | Proprietary server | Typically open |

## Vs PocketMine-MP

PocketMine is the reference **community** Bedrock (and historically PE) server: huge plugin corpus, battle-tested gameplay loops, PHP familiarity for many server owners.

| PocketMine strength | Zenith counter / stance |
|---------------------|-------------------------|
| Plugins tomorrow | We **delay** plugin API until domains are stable — avoids a frozen bad API |
| Years of edge-case survival | We relearn some edges deliberately; architecture docs try to encode lessons early |
| PHP ops ubiquity | .NET tooling, AOT/trim prospects, structured concurrency, spans/`ref struct` decode |
| “Just run a server” | Zenith is still a **codebase** first, product second |

Philosophically: PM optimizes for **extend everything now**. Zenith optimizes for **layers that stay honest** while the protocol surface is still being finished. When Zenith opens extension points, they should sit on Gameplay — not on raw packet listeners.

## Vs Nukkit-family (Java)

Similar story to PM: JVM servers with plugin ecosystems, solid for communities that already live in Java.

Zenith's bet:

- **One process model** with clearer ownership of RakNet + game tick separation
- **Leaf libraries** (Nbt, LevelDB) reusable outside the server — not buried inside plugin classloaders
- Avoid “god listener” patterns that make protocol upgrades painful

Java stacks remain excellent if your team and plugins already live there. Zenith is not a rewrite of Nukkit in C#.

## Vs official BDS

BDS wins on **protocol compatibility** and vanilla feature completeness. It loses on:

- source-level server logic
- embedding into custom tooling
- rewriting transport or persistence

Zenith is for people who need to **own** the server. If you need “works like Realms with zero code,” use BDS.

## Vs other modern open stacks

Research and indie servers (Go Dragonfly lineage, V ports like Vedrock-style projects, etc.) share DNA with Zenith: rewrite transport, sane tick loops, careful protocol work.

Zenith differences:

- **.NET** as the home language (IDE, analyzers, test ecosystem, Windows-first ops comfort)
- Documented **anti-patterns freeze** (no Actor/ECS for cosmetics)
- Format ownership: NBT endian modes and LevelDB without pretending to be Mojang-compatible yet

We use those codebases as **references**, not as something to clone blindly.

## What Zenith intentionally declines to copy

| Pattern common elsewhere | Zenith stance |
|--------------------------|---------------|
| Plugin bus before core solid | Freeze |
| World CoW of entire columns early | Overlay + UpdateBlock |
| Silent fallback InMemory when LevelDB fails | Hard fail (ops visibility) |
| Env-var configuration sprawl | Single `zenith.yml` beside the exe |
| Mixing encode + game rules in one class | Packets ≠ Protocol ≠ Gameplay |

## Bottom line

Pick **PocketMine / Nukkit** for mature plugin economies today.  
Pick **BDS** for vanilla fidelity without source.  
Pick **Zenith** if you want a modern .NET Bedrock **platform** — layered, testable leaf libs, and a commit history that shows deliberate structure — and you accept early-stage gaps.

See also: [Why Zenith / future](why-zenith.md), [DX](dx.md).
