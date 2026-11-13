# ADR §8 — Config beside the executable

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §8`.

**Choice:** `Path.Combine(AppContext.BaseDirectory, "zenith.yml")`.

**Why:** Relative `zenith.yml` followed the process cwd — broken for service hosts / debuggers / double-click launches. BaseDirectory matches “config next to the binary” mental model.
