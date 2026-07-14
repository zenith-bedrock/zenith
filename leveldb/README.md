# Zenith.LevelDB

Managed key/value store used by Zenith chunk overlay storage (`c:` / `ov:` keys). Own on-disk format (ZLDB) — **not** binary-compatible with `LevelDB.Standard`, Mojang Bedrock worlds, or RocksDB.

## API (v1+)

```csharp
using var db = new DB(new Options { CreateIfMissing = true }, path);

db.Put(key, value);
db.Delete(key);

var batch = new WriteBatch();
batch.Put(keyA, valueA);
batch.Delete(keyB);
db.Write(batch, new WriteOptions { Sync = true }); // optional fsync of WAL

using var it = db.CreateIterator();
it.Seek(prefix);
while (it.IsValid()) { /* ... */ it.Next(); }

db.Close();
```

- **WriteBatch / `DB.Write`**: one journal record, then all ops applied to the memtable (atomic under the DB lock).
- **WriteOptions.Sync**: after WAL append, `FileStream.Flush(true)` before return.
- **Put / Delete**: one-op batches via `Write` (LevelDB-classic shape).

## Architecture today

```
memtable (SortedDictionary) + WAL (*.log) + single immutable table (*.ldb)
Flush = merge whole table + mem → new .ldb (full rewrite)
```

Journal still accepts legacy single-op records (type 1/2) for replay; new writes use type 3 batch.

## Gaps vs classic LevelDB / RocksDB (backlog)

Lições práticas do RocksDB aplicadas a um store de overlays esparsos — **sem** P/Invoke nativo.

| Prioridade | Gap Zenith | Lição RocksDB / clássico | Ação futura |
|------------|------------|--------------------------|-------------|
| P0 (feito) | WriteBatch / Sync semantics | Group commit começa no batch | — |
| P1 | WAL sem CRC / framing | Torn write silencioso | Journal framed + CRC; abort replay no bad record |
| P1 | Flush = rewrite da tabela inteira | Write stall | L0 append-only + merge-on-read; MANIFEST lista arquivos |
| P2 | Sem imm memtable / bg flush | Stall no `_gate` | Promote mem→imm; ThreadPool flush |
| P2 | Scan linear no `.ldb` | Seek/Get caros com muitos `ov:` | Index/restart points + Bloom (~10 bits) |
| P3 | Compaction 1 nível | Compaction storm / CF | Size-tiered; **sem** column families (prefix `c:`/`ov:` basta) |
| Defer | Formato Mojang / PInvoke RocksDB | — | Fora: formato ZLDB managed |

Para Zenith, o próximo ganho real de escala é **L0 multi-file** (fim do full-rewrite), não Bloom nem column families.
