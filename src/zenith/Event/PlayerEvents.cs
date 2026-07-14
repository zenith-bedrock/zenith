namespace Zenith.Event;

/// <summary>Publicado depois que o login é aceito e o Player já está registrado no PlayerManager.</summary>
class PlayerLoginEvent
{
    public Player.Player Player { get; }

    public PlayerLoginEvent(Player.Player player) => Player = player;
}

/// <summary>
/// Publicado quando a sessão de transporte de um player conectado é encerrada (disconnect,
/// kick ou timeout). Não é disparado se a conexão cair antes do login terminar - nesse caso
/// nunca chegou a existir um Player pra remover.
/// </summary>
class PlayerQuitEvent
{
    public Player.Player Player { get; }

    public PlayerQuitEvent(Player.Player player) => Player = player;
}
