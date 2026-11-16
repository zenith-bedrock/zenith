namespace Zenith.World;

/// <summary>
/// Saving and restoring one player's state against <see cref="IChunkStorage"/>: inventory, armor, offhand, and pose with vitals.
/// Writes are write-behind (see <see cref="WriteBehind"/>); loads are synchronous and belong to login. A player whose identity is
/// not stable (ADR §60) is never written.
/// </summary>
sealed class PlayerPersistence
{
    private readonly IChunkStorage _storage;
    private readonly WriteBehind _writes;

    public PlayerPersistence(IChunkStorage storage, WriteBehind writes)
    {
        _storage = storage;
        _writes = writes;
    }

    public void PersistInventory(Player.Player player)
    {
        if (!player.IdentityStable) return;
        _writes.Observe(_storage.PutInventoryAsync(player.Uuid, PlayerInventorySave.Pack(player)), "PutInventory");
    }

    /// <summary>Raw Put for tests / loaders — prefer <see cref="PersistInventory(Player.Player)"/> on live sessions.</summary>
    public void PersistInventory(Guid uuid, Player.PlayerInventory inventory) =>
        _writes.Observe(_storage.PutInventoryAsync(uuid, inventory.PackMainBlob()), "PutInventory");

    public bool TryLoadInventory(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetInventoryAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && PlayerInventorySave.TryLoad(blob, inventory);
    }

    public bool TryLoadInventory(Player.Player player)
    {
        var blob = _storage.GetInventoryAsync(player.Uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && PlayerInventorySave.TryLoad(blob, player.Inventory, player.Windows);
    }

    public void PersistArmor(Player.Player player)
    {
        if (!player.IdentityStable) return;
        _writes.Observe(_storage.PutArmorAsync(player.Uuid, player.Inventory.PackArmorBlob()), "PutArmor");
    }

    public bool TryLoadArmor(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetArmorAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && inventory.TryLoadArmorFromBlob(blob);
    }

    public void PersistOffhand(Player.Player player)
    {
        if (!player.IdentityStable) return;
        _writes.Observe(_storage.PutOffhandAsync(player.Uuid, player.Inventory.PackOffhandBlob()), "PutOffhand");
    }

    public bool TryLoadOffhand(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetOffhandAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && inventory.TryLoadOffhandFromBlob(blob);
    }

    /// <summary>Quit / gamemode Persist — skips unstable identity (ADR §60).</summary>
    public void PersistPlayerData(Player.Player player)
    {
        if (!player.IdentityStable) return;
        // A persisted health of 0 would fail TryUnpack's own corruption guard on the next load
        // (health must be positive — a hydrated player must never load already-dead). Disconnecting
        // mid-death-screen (before CompleteRespawn ran) is the one real path that could otherwise
        // hit this: persist as if already respawned rather than losing the whole blob (pose/XP too)
        // to a validation rejection.
        var health = player.IsDead ? player.MaxHealth : player.Health;
        var blob = PlayerDataBlob.Pack(
            player.PositionX,
            player.PositionY,
            player.PositionZ,
            player.Yaw,
            player.Pitch,
            player.GameMode,
            player.ExperienceLevel,
            player.ExperiencePoints,
            health,
            player.Hunger,
            player.Saturation,
            player.Exhaustion);
        _writes.Observe(_storage.PutPlayerDataAsync(player.Uuid, blob), "PutPlayerData");
    }

    /// <summary>
    /// Login hydrate. False = miss / corrupt / OOB → caller keeps terrain spawn + config GameMode.
    /// </summary>
    public bool TryLoadPlayerData(
        Guid uuid, out float x, out float y, out float z, out float yaw, out float pitch, out Player.GameMode mode,
        out int experienceLevel, out int experiencePoints,
        out float health, out float hunger, out float saturation, out float exhaustion)
    {
        x = y = z = yaw = pitch = 0;
        mode = Player.GameMode.Survival;
        experienceLevel = 0;
        experiencePoints = 0;
        health = 20f;
        hunger = 20f;
        saturation = 5f;
        exhaustion = 0f;
        var blob = _storage.GetPlayerDataAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && PlayerDataBlob.TryUnpack(
            blob, out x, out y, out z, out yaw, out pitch, out mode, out experienceLevel, out experiencePoints,
            out health, out hunger, out saturation, out exhaustion);
    }
}
