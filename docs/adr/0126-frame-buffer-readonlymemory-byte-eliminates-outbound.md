# ADR §126 — `Frame.Buffer` → `ReadOnlyMemory<byte>` — eliminates outbound fragmentation's per-fragment copy

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §126`.

**Context:** Same external-review pass as ADR §124/§125. This claim — `RakNetSession`'s outbound
split loop allocates a new `byte[]` per fragment (`frame.Buffer.AsSpan(i, chunkLength).ToArray()`) —
turned out to already have a much more thorough prior audit:
`docs/history/phases/phase-xxvii-network-hot-path-findings.md` confirmed the same finding and
deliberately deferred fixing it to its own phase, since the prerequisite (`Frame.Buffer` no longer
being `byte[]`) looked like it would touch the ordering queue, fragment reassembly queue,
retransmission buffer, and encode simultaneously.

Re-deriving the actual blast radius (not just trusting that estimate) found it smaller and safer:
no `ArrayPool` needed (a `ReadOnlyMemory<byte>` slice of a GC-owned array has no use-after-return
risk — the GC keeps the backing array alive as long as any slice is reachable), inbound paths need
no behavior change (`HandleIncomingBatch` still needs a real `byte[]` for its `BinaryStream`
constructor, so those 4 call sites just gained an explicit `.ToArray()` where an implicit
`byte[]`-ness used to paper over it — same cost as before, not a regression), and `byte[]` → 
`ReadOnlyMemory<byte>` is an implicit conversion, so nearly every existing `Buffer = someByteArray`
call site kept compiling unchanged. The real blast radius was mechanical: 19 test call sites using
collection-expression syntax (`Buffer = [1, 2, 3]`, which doesn't target `ReadOnlyMemory<byte>`)
needed `Buffer = new byte[] { 1, 2, 3 }`, plus a couple of `.Span`/`.ToArray()` fixes at indexer/
`SequenceEqual` call sites.

**Choice:** `Frame.Buffer: byte[]` → `ReadOnlyMemory<byte>`. `SendFrameLocked`'s split loop:
`Buffer = frame.Buffer.AsSpan(i, chunkLength).ToArray()` → `Buffer = frame.Buffer.Slice(i, chunkLength)`
— the actual fix, zero-allocation slice instead of a copy. `Frame.Encode()`: `writer.Write(Buffer)` →
`writer.Write(Buffer.Span)` (`BinaryStream.Write` already accepted spans). The one real tradeoff —
a large payload's backing array now stays alive until every fragment referencing it is ACKed or
dropped, instead of becoming garbage right after fragmentation — is bounded by ADR §125's
`MaxOutputBacklogBytes` (8 MB): a session can't have more outstanding than that ceiling regardless,
so this doesn't need a separate cap.

**Measured (new `OutboundFragmentationBenchmarks`, ShortRun, both through the real `SendFrame`
pipeline — not a synthetic isolated copy, which would understate the pipeline's other costs and
overstate the delta):**

| Payload | Before (copy) | After (slice) | Δ time | Δ allocated |
|---|---:|---:|---:|---:|
| 64 KiB | 113.0 µs, 221.42 KB | 47.1 µs, 156.09 KB | ~2.4x faster | −65.3 KB (≈ the payload size, as expected) |
| 1 MiB | 1,691.9 µs, 3,537.12 KB | 727.4 µs, 2,491.84 KB | ~2.3x faster | −1,045.3 KB (≈ the payload size) |

The remaining allocation (156 KB / 2.49 MB) is the send pipeline's other costs — `FrameSet.Encode()`'s
intermediate buffers, per-fragment `Frame`/list objects — explicitly out of scope here, same as
Phase XXVII left them (its own evidence-gated pass, not touched this time either).

**Status (ago 2026):** Shipped — `Frame.cs`, `RakNetSession.cs`. New tests:
`OutboundFragmentSlicingRoundTripTests` (a 5000-byte reliable send through real MTU-576
fragmentation, captured as raw datagrams, fed into a receiving session, reassembles byte-for-byte
identical to the original — proves slicing didn't corrupt fragment boundaries, not just that it
compiles). New benchmark: `OutboundFragmentationBenchmarks` (measured by temporarily reverting the
one changed line, running the real pipeline both ways, then restoring — not by trusting a synthetic
isolated-copy baseline).
