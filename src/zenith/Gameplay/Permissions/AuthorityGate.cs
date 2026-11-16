namespace Zenith.Gameplay.Permissions;

/// <summary>
/// One permission a principal exercised to queue work: what a deferred operation carries so the owner can ask again right before
/// the effect. A value (no live objects), so it can cross a handoff.
/// </summary>
readonly record struct Authorization(Principal Principal, string Node);

/// <summary>
/// The owner-side recheck for deferred work. Checking when a command is parsed gives the user an answer; this check, made on the
/// game loop just before the mutation, is the one that counts: a grant revoked, or a session that ended, between enqueue and
/// commit stops the effect.
/// </summary>
sealed class AuthorityGate(PermissionAuthority authority, Player.PlayerManager players)
{
    public PermissionAuthority Authority => authority;

    /// <summary>
    /// True while the issuer still holds the node <i>and</i> is still the same online login: a later login that reused the same
    /// identity does not inherit work its predecessor queued.
    /// </summary>
    public bool Confirms(in Authorization issued) =>
        players.GetByRuntimeId(issued.Principal.SessionId) is { } session &&
        session.Uuid == issued.Principal.Subject &&
        authority.Has(issued.Principal, issued.Node);

    /// <summary>Work with no issuer (the server itself acting) always stands; work with one stands only while it is confirmed.</summary>
    public bool Confirms(in Authorization? issued) => issued is not { } value || Confirms(value);
}
