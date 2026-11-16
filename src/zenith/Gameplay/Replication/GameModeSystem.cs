using System.Collections.Generic;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Aplica <see cref="Player.Player.Intents.SubmitGameMode"/> no tick e transmite wire (§52).
/// Handler só enfileira; inventário não é reseedado.
/// </summary>
sealed class GameModeSystem(AuthorityGate gate) : IGameSystem
{
    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;
            if (!player.Intents.TryConsumeGameMode(out var request)) continue;

            // The owner asks again: a grant revoked, or an issuer that left, between the command and this tick stops the change.
            if (!gate.Confirms(request.Issuer)) continue;
            var mode = request.Mode;

            player.SetGameMode(mode);

            var wire = (int)mode;
            var entity = player.Client.Entity;
            entity.SendPlayerGameType(wire);
            entity.SendLocalAbilities(player.RuntimeId, wire);
            entity.SendAdventureSettings();

            // PocketMine syncGameMode → syncCreative on every mode change (not only enter Creative).
            // Join already sent once in ResourcePacks (PM/DF/Serenity all send CreativeContent at spawn).
            // Dragonfly/Serenity omit remint on SetGameMode and rely on abilities; Zenith follows PM.
            player.Client.Inventory.SendCreativeContent();

            PlayerVisibility.RefreshPeerView(player, online);

            player.Level.Persistence.PersistPlayerData(player);

            player.Client.Ui.SendToast(
                "Game mode",
                mode == GameMode.Creative ? "Creative" : "Survival");
        }
    }
}
