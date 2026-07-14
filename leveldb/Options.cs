namespace Zenith.LevelDB;

/// <summary>Opções de abertura do banco.</summary>
public sealed class Options
{
    public bool CreateIfMissing { get; set; } = true;
    public bool ErrorIfExists { get; set; }

    /// <summary>Tamanho aproximado do memtable antes do flush para tabela imutável.</summary>
    public int WriteBufferSize { get; set; } = 4 * 1024 * 1024;
}

/// <summary>Opções de leitura (reservado; v1 ignora verify).</summary>
public sealed class ReadOptions
{
    public static ReadOptions Default { get; } = new();
}

/// <summary>Opções de escrita (reservado; v1 sempre faz append no journal).</summary>
public sealed class WriteOptions
{
    public static WriteOptions Default { get; } = new();
    public bool Sync { get; set; }
}
