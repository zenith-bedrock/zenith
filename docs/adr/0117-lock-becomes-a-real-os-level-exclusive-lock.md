# ADR §117 — `LOCK` becomes a real OS-level exclusive lock, not a marker file

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §117`.

**Choice:** Continued the §115/§116 comparison against `mojang-leveldb` and found the most
consequential gap of the three passes: ZLDB's `LOCK` file was created (`if (!File.Exists) WriteAllText`)
but never actually held open exclusively — nothing stopped two `DB` instances, same process or
different processes, from opening the same directory at once. Each would run its own independent
`_mem`/`_journal`/`_nextFileNum`, silently clobbering the other's WAL/snapshot on flush — the
underlying reason real LevelDB's `Env::LockFile` (POSIX `flock`, Windows `LockFileEx`) exists at
all. Fixed: `Open()` now opens `LOCK` with `FileShare.None` and holds that `FileStream` for the
DB's entire lifetime; `.NET`'s own file-sharing model gives mandatory locking for free (no
per-OS P/Invoke needed, unlike native LevelDB's `Env` abstraction). A second open on the same
directory now throws `InvalidOperationException` immediately instead of corrupting data silently
later.

**Regression caught during this same pass, not shipped separately:** the first version acquired the
lock but only released it on a *successful* `Open()` — an `Open()` that throws partway through
(e.g. `TableFile.TryLoadInto`'s `InvalidDataException` on a corrupted snapshot, thrown well after
the lock is taken) leaked the lock handle, permanently locking the directory out of any future
attempt, including `Repair` — which needs to reopen the very directory `Open()` just failed on to
do its job. Fixed in the same pass: the constructor now wraps `Open()` in try/catch and releases
`_journal`/`_lockHandle` on any exception before rethrowing. Caught by a dedicated regression test,
not by accident.

**Why now:** third and (for now) final pass of the `mojang-leveldb` comparison the user asked to
continue after §115/§116. Same non-goal boundary as both — no compaction/LSM territory touched.

**Status (ago 2026):** Shipped — `DB.cs`'s lock acquisition/release, constructor-level cleanup on a
failed open. `libs/leveldb.Tests` grew from 19 to 22 cases (concurrent-open rejection, lock release
on `Close()`, lock release on a failed `Open()`); all 22 green, full `zenith.sln`
build/1019-test run unaffected.
