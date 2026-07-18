using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="Player.Player.SubmitGameMode"/> no tick e transmite wire (§52).
/// Handler só enfileira; inventário não é reseedado.
/// </summary>
sealed class GameModeSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public GameModeSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;
            if (!player.TryConsumeGameMode(out var mode)) continue;

            var previous = player.GameMode;
            player.SetGameMode(mode);

            var wire = (int)mode;
            var entity = player.Session.Protocol.Entity;
            entity.SendPlayerGameType(wire);
            entity.SendLocalAbilities(player.RuntimeId, wire);
            entity.SendAdventureSettings();

            if (mode == GameMode.Creative && previous != GameMode.Creative)
                player.Session.Protocol.Inventory.SendCreativeContent();

            player.Session.Protocol.Ui.SendToast(
                "Game mode",
                mode == GameMode.Creative ? "Creative" : "Survival");
        }
    }
}
