# ADR §152 — Bound transport receive windows and admission

**Context:** The second protocol audit found invented ACK ranges, unbounded receive gaps and
ordered queues, missing wrap in inner 24-bit indices, and native NetherNet queues whose budgets
were applied only after negotiation. A valid native peer could also advertise a smaller receive
message size than the segmenter used.

**Choice:** ACK/NACK accepts only the exact bounded range supplied. RakNet uses circular 24-bit
comparisons, bounded pending datagrams, and per-session ordered frame/byte budgets, closing on
overflow. NetherNet reserves shared per-peer message/byte/channel capacity before copying in
callbacks, including negotiation; overflow marks failure for the owner to close safely outside
the native callback. Outgoing segmentation uses the minimum local/remote message size.

**Ownership:** These are transport handoff locks and resource accounting, not gameplay locks.
Never dispose native peers from a message callback. Capture the first legitimate packet during
channel announcement as before. Do not silently discard reliable messages on capacity overflow.

**Other invariants:** Stop Bedrock dispatch after closing, reject overflowing varints, and return
pooled inflate buffers on every failure before ownership transfers to the caller. Existing
channel-0 delivery, retransmission identity and single-writer gameplay remain unchanged.

**Verification:** Bounded leaf tests for exact ACKs, gaps, bytes/counts, wrap and legitimate reordering;
batch teardown/handler transition tests; pool ownership and varint bounds; native NetherNet
negotiation with a small remote message limit. No load test materializes millions of entries.
