using Zenith.Session;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Entidade lógica de um jogador conectado. Não sabe nada sobre raknet nem wire format.
/// Estado de gameplay (posição) só é mutado no GameLoop; a rede só escreve intents.
/// <para>
/// O que não é identidade, pose ou vitals vive em componentes próprios: <see cref="Intents"/> (caixas de entrada da rede),
/// <see cref="Dig"/> (quebra de bloco), <see cref="Windows"/> (containers e janelas), <see cref="Effects"/> (efeitos de status) e
/// <see cref="Replicated"/> (o que os peers já sabem).
/// </para>
/// </summary>
class Player
{
    /// <summary>Vanilla player maximum for the current first health slice; not an attribute framework.</summary>
    public const float DefaultMaxHealth = 20f;

    /// <summary>Euclidean interact reach (blocks) from eye to target center — Fase 3 simple authority.</summary>
    public const float MaxBlockReach = 6f;

    private readonly HealthState _health = new(DefaultMaxHealth);
    private bool _deathTransitionFinalized;

    // Connection lifecycle gates are intentionally independent scalars written by the session
    // boundary and observed by the GameLoop. They are not gameplay authority, but visibility
    // matters when cancelling a just-disconnected player's pending input.
    private volatile bool _isInGame;
    private volatile bool _isSpawning;

    public string Username { get; }
    public NetworkSession Session { get; }

    /// <summary>This player's connection as gameplay sees it: ports only, no session, packet or protocol type.</summary>
    public Zenith.Gameplay.Ports.IClientChannel Client => _client ?? Session;

    /// <summary>
    /// Replaces the channel gameplay uses to reach this player's client (default: its session). A headless or scripted player, or a
    /// test that wants to record what gameplay says to a client, attaches its own <see cref="Zenith.Gameplay.Ports.IClientChannel"/>.
    /// </summary>
    public void AttachClient(Zenith.Gameplay.Ports.IClientChannel client) => _client = client;

    private Zenith.Gameplay.Ports.IClientChannel? _client;
    private readonly World.World? _level;
    private readonly ItemPalette? _itemPalette;

    /// <summary>The world this player lives in; gameplay resolves positions, rules and storage against it.</summary>
    public World.World Level => _level ?? throw new InvalidOperationException($"Player '{Username}' was created without a world.");

    /// <summary>The item registry this player's stacks resolve against.</summary>
    public ItemPalette ItemPalette => _itemPalette ?? throw new InvalidOperationException($"Player '{Username}' was created without an item palette.");

    /// <summary>Runtime entity id enviado no StartGame / MoveActorAbsolute.</summary>
    public long RuntimeId { get; }

    /// <summary>UUID de lista/AddPlayer.</summary>
    public Guid Uuid { get; }

    /// <summary>
    /// False when login minted an ephemeral Guid — skip <c>pd:</c> / prefer no reconnect honesty (ADR §60).
    /// </summary>
    public bool IdentityStable { get; }

    /// <summary>Requests the network hands to the GameLoop (movement input, attack, chat, block edits, ...).</summary>
    public PlayerIntents Intents { get; }

    /// <summary>Block-breaking state and the dig intent queue.</summary>
    public PlayerDig Dig { get; }

    /// <summary>The open container, the crafting grids, the window intents and the inventory-request ledger.</summary>
    public PlayerWindows Windows { get; }

    /// <summary>Active status effects and the pending effect command.</summary>
    public PlayerEffects Effects { get; }

    /// <summary>What peers were last told about this player (held item, pose, flags).</summary>
    public PlayerReplicatedState Replicated { get; } = new();

    /// <summary>
    /// True after SetLocalPlayerAsInitialized → InGame. Connection-lifecycle scalar deliberately
    /// written by session transitions (ADR §97), not an intent: it gates visibility and protocol
    /// handling but does not itself mutate world, inventory, combat, or movement authority.
    /// </summary>
    public bool IsInGame
    {
        get => _isInGame;
        set => _isInGame = value;
    }

    /// <summary>
    /// True while SpawnResponse (after PLAYER_SPAWN, before InGame). Allows ChunkStream
    /// to fill the view ring during loading (ADR §70). Like <see cref="IsInGame"/>, this is a
    /// direct connection-lifecycle transition rather than gameplay state owned by a simulation.
    /// </summary>
    public bool IsSpawning
    {
        get => _isSpawning;
        set => _isSpawning = value;
    }

    /// <summary>
    /// Hotbar 0–8; selected slot bounds-checked no handler. Intentional rule-6 exception:
    /// handlers write this directly (not via a pending-intent queue) — atomic scalar,
    /// <see cref="Gameplay.Systems.EquipmentSystem"/> diffs + fans out on tick (ADR §80).
    /// </summary>
    public int SelectedHotbarSlot { get; set; }

    public PlayerInventory Inventory { get; }

    /// <summary>Join-time from config (§31); runtime via GameModeSystem only (§52). Never handlers.</summary>
    public GameMode GameMode { get; private set; }

    /// <summary>Colunas enviadas / em voo e raio de view (streaming).</summary>
    public PlayerChunkTracker Chunks { get; } = new();

    /// <summary>Held hotbar stack identity (ADR §55).</summary>
    public StackId HeldStackId => Inventory.GetStackId(SelectedHotbarSlot);

    /// <summary>
    /// Phase XX — the EntityId of the vehicle this player is riding, or null. Owned by the
    /// vehicle's own system (currently only <see cref="Gameplay.Systems.MinecartSystem"/>), which
    /// is the only writer; <see cref="Gameplay.Systems.MovementSystem"/> only reads it to skip
    /// applying client-reported position while mounted (see MovementSystem's Tick). Not a mailbox:
    /// this is server-decided relationship state, not a one-shot network intent, and not
    /// persisted — a disconnect/reconnect starts unmounted (see phase findings).
    /// </summary>
    internal long? RidingEntityId { get; set; }

    /// <summary>
    /// Classic RGBA mirror of Session.Skin when dimensions match (PlayerList fallback /
    /// mid-game classic update). Full wire skin lives on <c>NetworkSession.Skin</c> (ADR §49).
    /// </summary>
    public byte[]? SkinRgba { get; set; }
    public uint SkinWidth { get; set; }
    public uint SkinHeight { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; } = Blocks.FlatSpawnY;
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    /// <summary>Read-only projection of the GameLoop-owned health state.</summary>
    public float Health => _health.Current;

    /// <summary>Maximum represented by this player's composed health state.</summary>
    public float MaxHealth => _health.Maximum;

    /// <summary>
    /// Domain vitals (ADR §40). Hunger remains a frozen HUD value; health has an explicit
    /// authoritative mutation path through <see cref="ApplyDamage"/>.
    /// </summary>
    public float Hunger { get; set; } = 20f;

    /// <summary>
    /// Vanilla-parity saturation buffer (Phase XXV) — depletes before <see cref="Hunger"/> on each
    /// exhaustion-threshold crossing, same as vanilla's "well-fed" cushion. Written only by
    /// <see cref="Gameplay.Systems.HungerSystem"/>.
    /// </summary>
    public float Saturation { get; set; } = 5f;

    /// <summary>
    /// Vanilla-parity depletion accumulator (Phase XI.1). GameLoop-owned scalar written only by
    /// <see cref="Gameplay.Systems.HungerSystem"/> (thresholded transitions) and by whichever system
    /// caused the exhaustion in the first place (mining, taking damage — see
    /// <see cref="Gameplay.Systems.HungerSystem"/>'s own exhaustion-source constants).
    /// </summary>
    public float Exhaustion { get; set; }

    /// <summary>Player level (Phase XI.4) — <c>minecraft:player.level</c>. Mutated only via <see cref="AddExperience"/>/<see cref="SetExperience"/>.</summary>
    public int ExperienceLevel { get; private set; }

    /// <summary>Points earned toward <see cref="ExperienceLevel"/>'s next level — never exceeds <see cref="PlayerExperience.PointsToNextLevel"/>.</summary>
    public int ExperiencePoints { get; private set; }

    /// <summary>GameLoop-owned gain; applies every level-up the addition crosses (Phase XI.4).</summary>
    internal void AddExperience(int amount) =>
        (ExperienceLevel, ExperiencePoints) = PlayerExperience.AddPoints(ExperienceLevel, ExperiencePoints, amount);

    /// <summary>Login hydrate only — bypasses level-up math since the saved pair is already valid.</summary>
    public void SetExperience(int level, int points)
    {
        ExperienceLevel = level;
        ExperiencePoints = points;
    }

    /// <summary>Highest feet Y reached since last on-ground (ADR §96 fall damage). Reset on landing.</summary>
    public float FallPeakY { get; set; } = Blocks.FlatSpawnY;

    /// <summary>True while death screen is up — AuthInput/edits ignored until respawn tick (§40).</summary>
    public bool IsDead => _health.IsDead;

    /// <summary>Cause string last sent via DeathInfo (tests / Debug).</summary>
    public string DeathCause => _health.FatalSource?.DeathInfoCause ?? "";

    /// <summary>AuthInput pose modes applied on tick (§53).</summary>
    public bool IsSneaking { get; set; }
    public bool IsSprinting { get; set; }

    /// <summary>AuthInput VerticalCollision — peer Absolute ON_GROUND (default true until first input).</summary>
    public bool IsOnGround { get; set; } = true;

    public Player(
        string username,
        NetworkSession session,
        long runtimeId,
        Guid uuid,
        GameMode gameMode = GameMode.Survival,
        bool identityStable = true,
        World.World? level = null,
        ItemPalette? itemPalette = null)
    {
        Username = username;
        Session = session;
        _level = level;
        _itemPalette = itemPalette;
        RuntimeId = runtimeId;
        Uuid = uuid;
        IdentityStable = identityStable;
        GameMode = gameMode;
        Inventory = new PlayerInventory(seedStarterHotbar: gameMode == GameMode.Survival);
        Intents = new PlayerIntents(() => IsDead, () => GameMode);
        Dig = new PlayerDig(() => IsDead);
        Windows = new PlayerWindows(() => IsDead);
        Effects = new PlayerEffects(() => IsDead);
    }

    /// <summary>GameLoop only. Does not reseed inventory (§52).</summary>
    public void SetGameMode(GameMode mode) => GameMode = mode;

    /// <summary>
    /// Applies an authoritative damage request on the GameLoop. Network handlers and async work
    /// must submit intent/results to that owner rather than mutate health directly.
    /// </summary>
    internal DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick) => _health.Apply(source, amount, currentTick);

    /// <summary>
    /// Finalizes one already-accepted fatal health transition. This is deliberately separate
    /// from <see cref="ApplyDamage"/> so the gameplay owner can first prepare all death-side
    /// effects, including an all-or-nothing survival loot deposit.
    /// </summary>
    internal bool TryFinalizeDeath()
    {
        if (!IsDead || _deathTransitionFinalized) return false;
        _deathTransitionFinalized = true;
        IsSneaking = false;
        IsSprinting = false;
        Dig.AbortBreak();
        _ = Windows.TryClearOpenContainer(out _);
        _ = Intents.TryConsumeRespawn();
        return true;
    }

    /// <summary>
    /// Phase XXIII-B — vanilla-parity Golem aggro trigger: iron golems are passive until a player
    /// attacks a villager near them (or attacks the golem itself). Zenith has no village/reputation
    /// system, so this narrow signal (who, when, where) is what <c>GolemSystem</c> checks instead of
    /// a full reputation model. Set by <c>VillagerSystem.TryApplyDamage</c>, read by
    /// <c>GolemSystem</c> — a direct field rather than a cross-system event bus, since there is
    /// exactly one producer and one consumer.
    /// </summary>
    internal (ulong Tick, float X, float Z)? LastVillagerAttack { get; set; }

    /// <summary>Authoritative well-fed regeneration entry point for <see cref="Gameplay.Systems.HungerSystem"/>.</summary>
    internal void Heal(float amount) => _health.Heal(amount);

    /// <summary>Login hydrate of persisted vitals (Phase XXV, <see cref="World.PlayerDataBlob"/> v3) — not a gameplay transition.</summary>
    internal void HydrateVitals(float health, float hunger, float saturation, float exhaustion)
    {
        _health.Hydrate(health);
        Hunger = hunger;
        Saturation = saturation;
        Exhaustion = exhaustion;
    }

    /// <summary>Clears death after GameLoop applied spawn pose + vitals.</summary>
    public void CompleteRespawn()
    {
        _health.RestoreFull();
        _deathTransitionFinalized = false;
        Hunger = 20f;
        Saturation = 5f;
        Exhaustion = 0f;
        IsSneaking = false;
        IsSprinting = false;
        Replicated.ForgetPoseFlags();
        FallPeakY = PositionY;
        _ = Intents.TryConsumeRespawn();
    }
}
