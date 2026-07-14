# Why Zenith — and why it can be the future

This is intentional positioning, not a claim that Zenith replaces PocketMine or BDS today. Early software that lies about maturity loses trust; early software that is **clear about the bet** attracts the right collaborators.

## The problem with “yet another Bedrock server”

Most rewrites fail the same way:

1. Clone packet handlers from somewhere that mixes game rules into decode
2. Add a plugin API in month one so demos look alive
3. Bind native LevelDB / snappy / zlib stacks that nobody on the team owns
4. Discover protocol version skew and try to paper over it with forks
5. Drown in architecture fashion (ECS, actors, job systems) before chat works

Zenith's counter-program is boring on purpose: **finish the spine with honest layers**, own the binary formats, freeze decorative infrastructure, document non-goals.

## What “the future” means here

Not “most servers will run Zenith next year.” It means:

### 1. Protocol and gameplay stay separable forever

Bedrock will keep moving. Separating Packets / Protocol / Gameplay is how you survive packet ID churn without rewriting survival rules. That is a **maintenance future**, not a blog slogan.

### 2. Formats as libraries, not folklore

`Zenith.Nbt` and `Zenith.LevelDB` are reusable, tested, and dependency-graph leaves. Tools, converters, and future features (creative inventory dumps, overlay repair, editors) can sit on the same libs the server uses — one encode path.

Managed LevelDB also avoids the “works on my Linux CI, dies on Windows hosts” class of native drama for Zenith's own key space.

### 3. .NET as a serious Bedrock host language

C# gives:

- memory-conscious decode (`ref struct`, spans)
- first-class async/`ValueTask` for storage without forcing rewrite on day one
- a professional tooling story for studios that already ship .NET

The future of custom Bedrock software is not only PHP and Java. Zenith plants a flag where Studio / Rider / `dotnet test` live.

### 4. Extension points after domains exist

Plugin ecosystems that attach to raw packets ossify bad boundaries. Zenith delays plugins until World, inventory, visibility, and chat have real shapes — then plugins hang off **gameplay**, matching how modern engines prefer modding.

That is slower marketing; faster decade.

### 5. Ops that match how binaries ship

Config beside the exe, LevelDB open failure that **does not** silently drop to empty RAM worlds, auth verify as an explicit public-exposure switch — boring reliability that production operators remember.

## Honest present

You should **not** use Zenith today if you need:

- production survival gameplay parity
- a plugin marketplace
- Mojang world import
- zero protocol surprises with every client update

You **should** look at Zenith if you want to:

- learn / contribute to a layered Bedrock stack in C#
- reuse NBT or KV pieces
- help define the extension model correctly the first time
- build tools around the same libraries as the server

## How we'll know the bet is working

Leading indicators (years, not weeks):

| Signal | Meaning |
|--------|---------|
| Protocol bumps touch Packets/Protocols, not World rules | Layering held |
| Third parties depend on `Zenith.Nbt` / LevelDB alone | Leaf libs won |
| Gameplay systems stay free of `DataPacket` | Freeze culture held |
| Overlay/L0 storage evolves without rewriting handlers | Persistence boundary held |
| A small, boring plugin/mod API appears **late** and stays thin | Patience paid off |

## Closing

PocketMine proved community Bedrock servers can thrive. Official BDS proved vanilla fidelity without source. Zenith's bet is the missing third curve: **an open, modern .NET Bedrock platform with ruthless layering** — built slowly enough that it can still matter when Minecraft's binary surface has moved on again.

Create something extraordinary, but create it with a spine that survives contact with the protocol.

— See [Architecture](architecture.md), [Decisions](decisions.md), [Comparison](comparison.md), [DX](dx.md).
