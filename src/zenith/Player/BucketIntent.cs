using Zenith.World;

namespace Zenith.Player;

/// <summary>A bucket used on a block: the clicked cell and face, and the hotbar stack the client believes it holds.</summary>
readonly record struct BucketIntent(int X, int Y, int Z, byte Face, int HotbarSlot, StackId ExpectedStack);
