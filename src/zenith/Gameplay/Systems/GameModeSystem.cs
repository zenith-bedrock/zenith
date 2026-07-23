using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Session;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="Player.Player.SubmitGameMode"/> no tick e transmite wire (§52).
/// Handler só enfileira; inventário não é reseedado.
/// </summary>
sealed class GameModeSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly List<global::Zenith.Player.Player> _onlineScratch = new();

    public GameModeSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock)
    {
        _players.FillOnline(_onlineScratch);
        Tick(clock, _onlineScratch);
    }


    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;
            if (!player.TryConsumeGameMode(out var mode)) continue;

            player.SetGameMode(mode);

            var wire = (int)mode;
            var entity = player.Session.Protocol.Entity;
            entity.SendPlayerGameType(wire);
            entity.SendLocalAbilities(player.RuntimeId, wire);
            entity.SendAdventureSettings();

            // PocketMine syncGameMode → syncCreative on every mode change (not only enter Creative).
            // Join already sent once in ResourcePacks (PM/DF/Serenity all send CreativeContent at spawn).
            // Dragonfly/Serenity omit remint on SetGameMode and rely on abilities; Zenith follows PM.
            player.Session.Protocol.Inventory.SendCreativeContent();

            PlayerVisibility.RefreshPeerView(player, online);

            player.Session.Context.World.PersistPlayerData(player);

            player.Session.Protocol.Ui.SendToast(
                "Game mode",
                mode == GameMode.Creative ? "Creative" : "Survival");
        }
    }
}
