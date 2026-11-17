using Zenith.Ecs;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;

namespace Zenith.Gameplay.Entities;

/// <summary>Which species an entity is, by the name a selector's <c>type=</c> and <c>/summon</c> use. Each species registers its ownership test once, at composition.</summary>
sealed class MobDirectory
{
    private readonly List<(string Name, Func<EntityId, bool> Owns)> _species = [];

    public void Register(string name, Func<EntityId, bool> owns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(owns);
        if (_species.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A mob named '{name}' is already in the directory.");
        _species.Add((name.ToLowerInvariant(), owns));
    }

    /// <summary>The species name of <paramref name="id"/>, or null when no registered species owns it.</summary>
    public string? NameOf(EntityId id)
    {
        foreach (var (name, owns) in _species)
            if (owns(id)) return name;
        return null;
    }
}

/// <summary>
/// Runs the commands that act on selected entities (<c>/effect @e[type=zombie] speed</c>, <c>/kill @e[r=10]</c>, <c>/gamemode creative @a</c>):
/// the one place a selector meets the world. It gathers the players and mobs that exist this tick, lets <see cref="SelectorResolver"/> pick,
/// re-checks the caller's authority for each pick (acting on yourself and on others are separate grants), and acts through each target's own
/// owner: a player's effects and game mode through their intents, a mob's effects through <see cref="MobEffectSystem"/>, a death through the
/// species' damage path so loot and consequences are kept. The caller is told how many it reached.
/// </summary>
[GameSystem("entity-command")]
sealed class EntityCommandSystem(
    EntityRuntime stores, PlayerManager players, DamageDispatch damage, MobEffectSystem mobEffects,
    MobDirectory directory, EntityCommandQueue queue, AuthorityGate gate, Random? random = null) : IGameSystem
{
    /// <summary>Damage large enough to end any mob or player whatever their absorption; Void ignores the invulnerability window and the armor.</summary>
    private const float KillDamage = 1_000_000f;

    private readonly Random _random = random ?? new Random();
    private readonly List<SelectorCandidate> _candidates = [];

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        while (queue.TryDequeue(out var command))
            Run(command, clock, online);
    }

    private void Run(EntityCommand command, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var caller = Find(command.Caller.SessionId, command.Caller.Subject);
        if (caller is null) return; // left since the line was typed

        Gather(online);
        var picked = SelectorResolver.Resolve(command.Selector, command.Origin, _candidates, _random);

        int applied = 0, denied = 0;
        foreach (var target in picked)
        {
            var node = NodeFor(command.Action, target, command.Caller);
            if (!gate.Confirms(new Authorization(command.Caller, node))) { denied++; continue; }
            if (Apply(command, target, clock, online)) applied++;
        }

        var action = command.Action;
        var message = picked.Count == 0 ? "No targets matched."
            : applied == 0 && denied > 0 ? "You do not have permission."
            : applied == 0 ? "Nothing to change."
            : $"{Describe(action)}: {applied} target{(applied == 1 ? "" : "s")}.";
        if (applied > 0 && denied > 0) message += $" Not allowed on {denied}.";
        caller.Client.Ui.SendToast(action.Title, message);
    }

    private Player.Player? Find(long sessionId, Guid id) =>
        players.GetByRuntimeId(sessionId) is { IsInGame: true } player && player.Uuid == id ? player : null;

    private void Gather(IReadOnlyList<Player.Player> online)
    {
        _candidates.Clear();
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            _candidates.Add(new SelectorCandidate(
                SelectorCandidate.PlayerType, player.Username, player.PositionX, player.PositionY, player.PositionZ,
                player.RuntimeId, player.Uuid, Level: player.ExperienceLevel, Mode: player.GameMode));
        }

        foreach (var id in Query.With(stores.Positions, stores.Health))
        {
            if (stores.Health.TryGet(id, out var health) && health.State.IsDead) continue;
            if (directory.NameOf(id) is not { } species) continue;
            if (!stores.Positions.TryGet(id, out var position)) continue;
            _candidates.Add(new SelectorCandidate(species, species, position.X, position.Y, position.Z, Entity: id));
        }
    }

    /// <summary>The permission an action needs on this target: your own effects, game mode and death are one grant, anything else's another.</summary>
    private static string NodeFor(EntityAction action, SelectorCandidate target, Principal caller)
    {
        var self = target.IsPlayer && target.PlayerSessionId == caller.SessionId && target.PlayerId == caller.Subject;
        return action.Kind switch
        {
            EntityActionKind.GameMode => self ? PermissionNodes.GameModeSelf : PermissionNodes.GameModeOthers,
            EntityActionKind.Kill => PermissionNodes.Kill,
            _ => self ? PermissionNodes.EffectSelf : PermissionNodes.EffectOthers
        };
    }

    private bool Apply(EntityCommand command, SelectorCandidate target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var action = command.Action;
        var issued = new Authorization(command.Caller, NodeFor(action, target, command.Caller));
        if (target.IsPlayer)
        {
            if (Find(target.PlayerSessionId, target.PlayerId) is not { } player) return false;
            switch (action.Kind)
            {
                case EntityActionKind.GiveEffect:
                    player.Effects.SubmitEffect(EffectIntent.Give(action.Effect, action.Amplifier, action.DurationTicks, issued));
                    return true;
                case EntityActionKind.ClearEffects:
                    if (player.Effects.Active.Count == 0) return false;
                    player.Effects.SubmitEffect(EffectIntent.ClearBy(issued));
                    return true;
                case EntityActionKind.GameMode:
                    player.Intents.SubmitGameMode(action.Mode, issued);
                    return true;
                default:
                    PlayerDamage.ApplyCore(player, players, DamageSource.Void, KillDamage, clock.CurrentTick).Conclude(online);
                    return true;
            }
        }

        if (target.Entity is not { } id) return false;
        return action.Kind switch
        {
            EntityActionKind.GiveEffect => mobEffects.Give(id, action.Effect, action.Amplifier, action.DurationTicks, clock, online),
            EntityActionKind.ClearEffects => mobEffects.Clear(id, online),
            EntityActionKind.Kill => damage.TryApplyDamage(id, DamageSource.Void, KillDamage, online, clock.CurrentTick),
            _ => false // a mob has no game mode
        };
    }

    private static string Describe(EntityAction action) => action.Kind switch
    {
        EntityActionKind.GiveEffect => $"{action.Effect} given",
        EntityActionKind.ClearEffects => "Effects cleared",
        EntityActionKind.Kill => "Killed",
        _ => $"Game mode {action.Mode}"
    };
}
