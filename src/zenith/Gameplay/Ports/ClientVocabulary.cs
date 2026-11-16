namespace Zenith.Gameplay.Ports;

/// <summary>How an actor is attached to a vehicle. The wire value is the Protocol layer's business.</summary>
enum ActorLinkKind : byte
{
    Remove,
    Rider,
}

/// <summary>What happened to a timed effect on an actor.</summary>
enum EffectChange : byte
{
    Add,
    Update,
    Remove,
}

/// <summary>A sound the server decides to play at a point. Which wire sound id it is belongs to the Protocol layer.</summary>
enum SoundCue : byte
{
    Explosion,
    EndermanTeleport,
    IronGolemAttack,
}
