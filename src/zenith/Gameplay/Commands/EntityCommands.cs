using System.Collections.Concurrent;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Survival;
using Zenith.Player;

namespace Zenith.Gameplay.Commands;

enum EntityActionKind { GiveEffect, ClearEffects, Kill, GameMode }

/// <summary>What a command does to every entity its selector picks. Values only, so it can cross from the session thread to the loop.</summary>
readonly record struct EntityAction(
    EntityActionKind Kind, EffectType Effect = default, int Amplifier = 0, int DurationTicks = 0, GameMode Mode = default)
{
    public string Title => Kind switch
    {
        EntityActionKind.GiveEffect or EntityActionKind.ClearEffects => "Effect",
        EntityActionKind.Kill => "Kill",
        _ => "Game mode"
    };
}

/// <summary>One command waiting for the loop: who ran it, from where, which entities it names, and what to do to them.</summary>
sealed record EntityCommand(Principal Caller, SelectorOrigin Origin, TargetSelector Selector, EntityAction Action);

/// <summary>
/// The handoff from the session thread (where commands run) to the game loop (which owns the entities). A selector is resolved on the loop,
/// against the entities that exist then, never against a snapshot taken when the line was typed.
/// </summary>
sealed class EntityCommandQueue
{
    private readonly ConcurrentQueue<EntityCommand> _pending = new();

    public void Enqueue(EntityCommand command) => _pending.Enqueue(command);

    public bool TryDequeue(out EntityCommand command) => _pending.TryDequeue(out command!);
}
