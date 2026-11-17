namespace Zenith.Gameplay.Permissions;

/// <summary>The built-in permission nodes (exact names, no wildcards) and their defaults.</summary>
static class PermissionNodes
{
    public const string Help = "zenith.command.help";
    public const string Sample = "zenith.command.sample";
    public const string GameModeSelf = "zenith.command.gamemode.self";
    public const string GameModeOthers = "zenith.command.gamemode.others";
    public const string EffectSelf = "zenith.command.effect.self";
    public const string EffectOthers = "zenith.command.effect.others";
    public const string GameRuleRead = "zenith.command.gamerule.read";
    public const string GameRuleWrite = "zenith.command.gamerule.write";
    public const string DifficultyRead = "zenith.command.difficulty.read";
    public const string DifficultyWrite = "zenith.command.difficulty.write";
    public const string Summon = "zenith.command.summon";
    public const string Diagnostics = "zenith.command.diagnostics";

    public const string WorldPlace = "zenith.world.place";
    public const string WorldBreak = "zenith.world.break";
    public const string WorldInteract = "zenith.world.interact";
    public const string ContainerOpen = "zenith.container.open";
    public const string ContainerTransfer = "zenith.container.transfer";

    /// <summary>Registers every built-in node. Modules register their own during composition, before the registry freezes.</summary>
    public static void RegisterBuiltIns(PermissionRegistry registry)
    {
        registry.Register(Help, PermissionDefault.Everyone);
        registry.Register(Sample, PermissionDefault.Everyone);
        registry.Register(GameModeSelf, PermissionDefault.Operator);
        registry.Register(GameModeOthers, PermissionDefault.Operator);
        registry.Register(EffectSelf, PermissionDefault.Operator);
        registry.Register(EffectOthers, PermissionDefault.Operator);
        registry.Register(GameRuleRead, PermissionDefault.Everyone);
        registry.Register(GameRuleWrite, PermissionDefault.Operator);
        registry.Register(DifficultyRead, PermissionDefault.Everyone);
        registry.Register(DifficultyWrite, PermissionDefault.Operator);
        registry.Register(Summon, PermissionDefault.Operator);
        registry.Register(Diagnostics, PermissionDefault.Operator);

        registry.Register(WorldPlace, PermissionDefault.Everyone);
        registry.Register(WorldBreak, PermissionDefault.Everyone);
        registry.Register(WorldInteract, PermissionDefault.Everyone);
        registry.Register(ContainerOpen, PermissionDefault.Everyone);
        registry.Register(ContainerTransfer, PermissionDefault.Everyone);
    }
}
