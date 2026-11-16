namespace Zenith.World;

/// <summary>The default world storage: write-behind over ZLDB (ADR §11, §168).</summary>
sealed class LevelDbChunkStorage(string directory) : WriteBehindStorage(new LevelDbRecordStore(directory));
