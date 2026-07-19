namespace Zenith.Session;

/// <summary>
/// Login identity / ClientData fields needed on join wire (ADR §59).
/// Lives on <see cref="NetworkSession"/> — not Player (no domain→Packets).
/// </summary>
readonly record struct ClientProfile(
    string Xuid,
    string DeviceId,
    int BuildPlatform,
    string PlatformChatId)
{
    public static ClientProfile Empty { get; } = new("", "", -1, "");
}
