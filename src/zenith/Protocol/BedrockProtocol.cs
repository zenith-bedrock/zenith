using Zenith.Packets;
using Zenith.Session;

namespace Zenith.Protocol;

/// <summary>Façade de composição: só agrega os módulos <c>*Protocol</c>. Sem lógica de negócio.</summary>
sealed class BedrockProtocol
{
    public LoginProtocol Login { get; }
    public ResourcePacksProtocol ResourcePacks { get; }
    public WorldProtocol World { get; }
    public EntityProtocol Entity { get; }
    public ChatProtocol Chat { get; }
    public InventoryProtocol Inventory { get; }
    public SkinProtocol Skin { get; }
    public UiProtocol Ui { get; }
    public CommandProtocol Commands { get; }

    public BedrockProtocol(NetworkSession session)
    {
        Login = new LoginProtocol(session);
        ResourcePacks = new ResourcePacksProtocol(session);
        World = new WorldProtocol(session);
        Entity = new EntityProtocol(session);
        Chat = new ChatProtocol(session);
        Inventory = new InventoryProtocol(session);
        Skin = new SkinProtocol(session);
        Ui = new UiProtocol(session);
        Commands = new CommandProtocol(session);
    }
}
