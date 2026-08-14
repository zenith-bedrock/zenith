# Entity fidelity — real-client validation flows (Phase XXIII-B)

Practical, reproducible test flows for gathering the real-client evidence Phase XXIII-B needs before
any further tuning happens. This is a testing checklist, not architecture — see `docs/entity-fidelity.md`
for current runtime truth and `docs/history/phases/phase-xxiii-b-entity-fidelity-validation-findings.md`
for the full brief this responds to.

**Tooling limitation up front:** Zenith currently has no in-game `/teleport` or `/spawn <mob>` command
— only `/gamemode`, `/effect`, `/diagnostics`, `/help`, `/sample`. Every mob you encounter is one of
each species' single auto-bootstrap spawn (near wherever the first player was when that system first
ticked), not something you can place on demand. The flows below work around that with Creative-mode
flight for positioning and patience for re-encountering a species after it despawns/respawns. If this
turns out to be too limiting, say so and a debug-only spawn/teleport command (Part 25 of the brief
explicitly allows a dev-only diagnostic command) is a reasonable small addition.

## Setup

1. In `zenith.yml`, set:
   ```yaml
   log:
     server: debug
     to-file: true
   ```
   This gives the richest possible evidence: `logs/zenith-<run>.log` (verbose text) and
   `logs/zenith-diagnostics-<run>.jsonl` (tick timings/counters every ~30s). Send me both files after
   each session — I can't see your screen, but I can read wire-level and timing evidence from these.
2. `/gamemode creative` early in the session — flight makes it far easier to hold a fixed distance,
   view a mob from multiple angles, or reposition without triggering unwanted combat.
3. Keep a note of **wall-clock or in-game timestamps** for anything you want me to correlate against
   the log file (e.g. "the zombie fight around 15:07:40 felt off").
4. If you have a way to record short clips, even a phone video, that's more useful than a text
   description for anything involving motion/timing (turning speed, attack cadence).

## How to report findings

For each issue, give me as much of this as you can — even a partial version is useful, I'll fill in
gaps from code:

```text
Actor:            (Zombie / Skeleton / Spider / Cow / Creeper / Golem / other)
Scenario:         (which flow below, or free-form)
What you saw:
What you expected:     (optional — vanilla memory is fine, doesn't need to be precise)
Timestamp/clip:        (optional)
```

I'll classify each as `BUG | TUNING | MISSING VANILLA BEHAVIOR | PROTOCOL/PROJECTION | LOCAL
NAVIGATION | UNKNOWN` and fix in that priority order.

---

## Flow 1 — Rotation projection sanity (do this first)

This validates the biggest fix from the last round (movement packets used to send yaw=0). Do this
before anything else — if it's still broken, everything downstream is noise.

For **Zombie, Skeleton, Spider, Cow, Creeper, Golem** (encounter each naturally):

1. **Stand still and watch it spawn/idle.** Does it face a sensible direction, or immediately snap to
   face something odd (e.g. always facing world +Z/south right after spawn)?
2. **Let it notice and approach you.** Does its body turn to face you as it closes distance, or does
   it slide toward you while facing a fixed/wrong direction?
3. **Walk in a slow circle around it** (Creative flight helps here) while it's targeting you. Does it
   keep turning to face you smoothly, or does it snap/jitter/lag noticeably behind?
4. **Walk directly away, then double back past it on the other side** — this forces it through roughly
   a 180° turn. Does it turn the *short* way (matching what we tested in code — 179°→-179° should be a
   ~2° step, not a ~358° spin)? A visible full-circle spin here would mean the wraparound fix isn't
   reaching the wire correctly.
5. **Stop moving entirely while it's still targeting you.** Does it keep facing you (holding), or does
   its facing freeze at whatever it last was?

## Flow 2 — Zombie deep pass (reference-quality target)

1. Let a Zombie notice you from moderate range. Note roughly how far away it "wakes up" and starts
   approaching (in blocks, eyeballed against the terrain grid is fine).
2. Time its approach: pick two landmarks ~10 blocks apart, time how long it takes to cross that gap
   while chasing you in a straight line. This gives blocks/second.
3. Let it reach melee range. Note:
   - Does damage feel instant on first contact, or is there a visible windup/swing before you take
     damage?
   - How often does it hit you (rough seconds between hits)?
   - Do you see any swing/attack animation on the zombie itself?
4. Take a non-lethal hit from it (or let it hit you) — actually, invert this: **you hit it** without
   killing it. Confirm: does it visibly react (flash/flinch)? Does it get knocked back? Does it keep
   chasing you afterward, or does it pause/reset?
5. Kill it. Confirm: death animation plays, corpse doesn't vanish instantly *and* doesn't linger
   oddly, loot (rotten flesh) appears where it died, XP orb behavior if visible.
6. **Obstacle scenarios** — lead a zombie into: a single free-standing block in its path, a 2-block-tall
   wall, a corner/doorway, a 1-block step up, a small 1-block-deep pit. For each, note: does it path
   around/over correctly, does it get stuck, does it oscillate side to side, does it slide along the
   obstacle unnaturally?

## Flow 3 — Skeleton spacing (brand-new behavior this round, most likely to be rough)

Skeleton previously never moved at all — it only rotated to shoot. This is the newest, least-tested
code in the whole set.

1. Approach a Skeleton from far away (>10 blocks) and note: does it hold ground, or start closing the
   gap? Roughly how far away does it start reacting?
2. Walk directly at it until you're very close (melee range). Does it back away? Does it back away
   smoothly, or does it flicker/oscillate (step back, step forward, step back)? This oscillation is
   the specific failure mode the brief calls out — if you see it, that's valuable evidence.
3. From medium range, strafe sideways (walk in an arc around it rather than straight at/away). Does it
   reposition sensibly, or does it behave erratically?
4. Watch for shots: does it visibly aim at you before firing, or does the arrow seem to come from a
   direction the skeleton wasn't facing?
5. Stand on a block 2-3 higher than the skeleton, then 2-3 lower. Does anything about its aim/behavior
   change, or does it seem to ignore your elevation? (We know pitch/vertical aim isn't implemented yet
   — this confirms whether that's actually noticeable or not worth doing.)
6. Take a hit — does the arrow visibly travel to you, or does damage just apply with no visible
   projectile?

## Flow 4 — Spider distinctiveness

1. Compare side-by-side impressions (even from memory/back-to-back encounters) against the Zombie
   flow: does Spider *feel* faster? Does it turn differently? Does its melee timing feel different?
2. Get poisoned by one. Confirm the poison effect actually applies (screen tint / effect icon / damage
   over time) — this is a pre-existing feature, just confirming it still works.
3. Note anything about how it approaches — Zombie should walk straight-ish at you; does Spider do
   anything visually distinct (the brief specifically worries Spider currently reads as "faster
   Zombie + Poison" and nothing else)?

## Flow 5 — Cow passive fidelity (idle/wander is where bad rotation is most obvious)

1. Watch a Cow that hasn't noticed you (stand still and observe from a distance) for at least 30-60
   seconds. Does it wander in a way that looks natural — walks a bit, pauses, picks a new direction,
   walks again? Or does it feel jittery/robotic?
2. Specifically watch its turning at the moment it picks a new wander direction — smooth turn into the
   new heading, or a snap?
3. Does it ever appear to look at you without moving toward you (idle curiosity), or does it only ever
   look in its movement direction?
4. Feed it (if you have wheat) and confirm the feed interaction still works, then hit it and confirm
   hurt feedback + it doesn't otherwise change behavior oddly (fleeing, freezing, etc. — Cow has no
   flee AI currently, so it shouldn't run from you; note if it does since that'd be unexpected).

## Flow 6 — Creeper (legacy actor, spot-check only)

1. Approach until it starts fusing. Do you see **any** visual/audio cue that it's about to explode
   (flashing white, hissing) — this round added the `Ignited` flag; confirm it actually shows.
2. Back away before it explodes. Does it visibly stop fusing (cue disappears), or does it explode
   anyway / stay in a fused-looking state?
3. Let one fully explode near you (from a safe-ish distance). Confirm: explosion particle/sound,
   damage falloff by distance feels roughly right, and — this round's other new fix — **do nearby
   blocks actually get destroyed**, or does the terrain stay intact?
4. Note its approach behavior/turning same as the Zombie flow, just briefly — it shares the same
   `LookMath` turning code, so this is mostly a sanity check, not new territory.

## Flow 7 — Golem (spot-check only)

1. Confirm it exists in your world (it may take a while to naturally bootstrap-spawn) and that basic
   melee combat against it works: it approaches, attacks, you can hurt/kill it, loot drops.
2. Same turning/rotation sanity as Flow 1.

## Flow 8 — General network/perf sanity

While doing all of the above, note anything that felt like lag, stutter, or delayed reactions —
distinct from a specific mob's AI. Cross-reference the timestamp against the diagnostics `.jsonl` and
I'll check tick timings for that window.

---

Send me: the two log files, plus your findings in whatever structure is convenient (the template
above if you have time, free-form notes are also fine — I'd rather have honest impressions than a
perfectly filled form). I'll classify and act from there.
