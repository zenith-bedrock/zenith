namespace Zenith.NetherNet.Axolotl.Info;

public enum GameType
{
    Survival = 0,
    Creative = 1,
    Adventure = 2,
}

/// <summary>Server advertisement data (port of <c>ServerInfo</c>).</summary>
public sealed record ServerInfo(
    string ServerName,
    int Protocol,
    string GameVersion,
    string LevelName,
    GameType GameType = GameType.Survival,
    int PlayerCount = 0,
    int MaxPlayerCount = 0,
    bool EditorWorld = false,
    bool Hardcore = false,
    bool AcceptsOnlineAuth = true,
    bool AcceptsSelfSignedAuth = true,
    string Nonce = "")
{
    public const int DataVersion = 7;
    public const int TransportLayerNetherNet = 2;
    public const int ConnectionTypeNetherNet = 4;
}

public interface IServerInfoProvider
{
    ServerInfo? GetServerInfo();
}

public sealed class MutableServerInfoProvider(ServerInfo? info = null) : IServerInfoProvider
{
    private volatile ServerInfo? _info = info;

    public ServerInfo? GetServerInfo() => _info;

    public void SetServerInfo(ServerInfo? value) => _info = value;
}
