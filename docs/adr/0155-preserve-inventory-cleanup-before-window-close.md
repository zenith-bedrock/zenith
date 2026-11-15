# ADR §155 — Preserve inventory cleanup before window close

The Cinnabar local-world path uses BDS. Its client sends ingredient/cursor return
and overflow-drop requests before ContainerClose without waiting for replies.
Zenith's separate window-first tick pass currently closes the container before
these earlier requests can commit, invalidating their otherwise-correct generation.

Use one per-player FIFO for window and inventory requests. The gameplay tick
consumes open, transfer, cleanup, close and reopen in their submission order.
Reserve generations for queued opens and bind subsequent ISR admission to the
projected session, including queued closes. Failed opens cannot grant access to
an older window. Bounded per-kind capacities, replay protection, stack validation
and rollback remain unchanged.

The existing PlayerWindows owns this bounded inventory lifecycle queue; no
scheduler or general dispatcher is introduced. No automatic second return of grid items
is added: the client cleanup is real inventory movement, and a redundant server
return could duplicate it. Rejected cleanup retains the original authoritative
items. A bare close without cleanup remains a separate behavior to establish.

Verification covers table/personal/cursor cleanup, overflow drops, replay,
requests after close, pending opens, reopened generations and queue pressure.
