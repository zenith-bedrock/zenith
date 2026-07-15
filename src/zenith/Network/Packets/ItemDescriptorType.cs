namespace Zenith.Network.Packets;

/// <summary>Item stack descriptor type byte (CraftingData encode / ItemStackRequest skip).</summary>
static class ItemDescriptorType
{
    public const byte Invalid = 0;
    public const byte Default = 1;
    public const byte Molang = 2;
    public const byte ItemTag = 3;
    public const byte Deferred = 4;
    public const byte ComplexAlias = 5;
}
