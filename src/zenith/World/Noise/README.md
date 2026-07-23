# World/Noise

Vendored **Auburn FastNoiseLite** (`FastNoiseLite.cs`, MIT) — ADR §72.

There is no official NuGet for the C# port; do not add third-party wrappers (`VL.FastNoiseLite`, etc.).
Configure instances only via `OverworldNoiseFields` (seed / frequency / FBm).
