namespace Zenith.Gameplay.Permissions;

/// <summary>How far the server can vouch for who a login claims to be.</summary>
enum IdentityTrust
{
    /// <summary>No stable identity (a random id per login). Never receives durable privilege.</summary>
    Ephemeral,

    /// <summary>A stable id the client chose itself (self-signed or offline login). Stable, but anyone can claim it.</summary>
    SelfAsserted,

    /// <summary>A FULL login whose issuer signature and client key proof the server verified.</summary>
    Authenticated
}

/// <summary>
/// Who is acting, as authorization sees it: an immutable value that can cross a handoff. <see cref="SessionId"/> names this login
/// (the player's runtime id), so work queued by one session cannot be replayed by a later one that reused the same identity.
/// </summary>
readonly record struct Principal(Guid Subject, IdentityTrust Trust, long SessionId);
