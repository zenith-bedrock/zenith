# ADR §100 — Command surface may grow before plugins, without becoming gameplay authority

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §100`.

**Choice:** `/` commands are a permitted product and developer-experience surface, not a frozen architecture subsystem and not an ECS prerequisite. The command core is protocol-independent: definitions, aliases, typed argument/overload selection, validation, permissions, suggestions and result/error feedback do not reference Bedrock packets. A thin Bedrock adapter translates core definitions/suggestions to client metadata/autocomplete and translates wire input back to the core. Command acceptance delegates to a gameplay runtime API or submits an intent under ADR §97; it never grants a session handler direct authority over world, player, inventory or combat state.

**Why:** a useful command set and client autocomplete solve operator and player UX independently of the still-frozen plugin API. A protocol-free core keeps future Bedrock wire churn from defining domain commands and makes the eventual extension boundary possible without committing to it now. Waiting for plugins would incorrectly couple two decisions; publishing plugin hooks now would freeze an extension contract before gameplay and plugin boundaries are known.

**First proof:** use only a few representative commands, collectively covering aliases; typed arguments; enums; optional arguments; ranges; player targets; multiple syntaxes/overloads; permissions; suggestions/autocomplete; and consistent feedback/errors. The core tests these without Bedrock types; adapter tests prove metadata/autocomplete; a real-client completion smoke verifies the wire edge.

**Non-goals:** a universal command/event framework, plugin command API, `IPluginCommand`, dynamic discovery, DI, public hooks, permission ecosystem clone, or routing gameplay through commands. Start the reusable command layer only when at least two current commands share the need; before then, retain a focused command path such as `/gamemode`.

**Verification:** `CommandCatalogTests`, `BedrockCommandAdapterTests`, and the real-client smoke that decoded `AvailableCommands` and exercised `/gamemode`; future commands must retain the same core/adapter split.

**Status (Aug 2026):** Policy recorded; the first small core/Bedrock slice is implemented and tested. Plugin-facing registration and discovery remain deferred.
