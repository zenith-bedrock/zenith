namespace Zenith.LevelDB;

/// <summary>Opções de abertura do banco.</summary>
public sealed class Options
{
    public bool CreateIfMissing { get; set; } = true;
    public bool ErrorIfExists { get; set; }

    /// <summary>Tamanho aproximado do memtable antes do flush para tabela imutável.</summary>
    public int WriteBufferSize { get; set; } = 4 * 1024 * 1024;
}

/// <summary>Opções de leitura (v1 ignora verify_checksums).</summary>
public sealed class ReadOptions
{
    public static ReadOptions Default { get; } = new();
}

/// <summary>Opções de escrita no journal (WAL).</summary>
public sealed class WriteOptions
{
    public static WriteOptions Default { get; } = new();

    /// <summary>
    /// Se true, após append no journal chama <c>FileStream.Flush(true)</c> (fsync)
    /// antes de retornar. Default false = flush OS buffer apenas.
    /// </summary>
    public bool Sync { get; set; }
}
