using Zenith.Session;

namespace Zenith.Protocol;

sealed class BedrockProtocol
{
    public LoginProtocol Login { get; }
    public ResourcePacksProtocol ResourcePacks { get; }
    public WorldProtocol World { get; }
    public EntityProtocol Entity { get; }
    public ChatProtocol Chat { get; }
    public InventoryProtocol Inventory { get; }
    public SkinProtocol Skin { get; }

    public BedrockProtocol(NetworkSession session)
    {
        Login = new LoginProtocol(session);
        ResourcePacks = new ResourcePacksProtocol(session);
        World = new WorldProtocol(session);
        Entity = new EntityProtocol(session);
        Chat = new ChatProtocol(session);
        Inventory = new InventoryProtocol(session);
        Skin = new SkinProtocol(session);
    }
}
