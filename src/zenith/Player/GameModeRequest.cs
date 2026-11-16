using Zenith.Gameplay.Permissions;

namespace Zenith.Player;

/// <summary>A queued mode change and who asked for it (null: the server itself), so the owner can reconfirm the authority before applying it.</summary>
readonly record struct GameModeRequest(GameMode Mode, Authorization? Issuer);
