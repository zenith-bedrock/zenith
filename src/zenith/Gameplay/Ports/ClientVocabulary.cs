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
    ExperienceOrbPickup,
}

/// <summary>The other half of a double chest, as the client needs to be told: where it is, and whether this cell leads the pair.</summary>
readonly record struct ChestLink(int PartnerX, int PartnerZ, bool Lead);
