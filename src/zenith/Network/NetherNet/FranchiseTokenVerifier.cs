using System.Security.Cryptography;
using Zenith.NetherNet.Axolotl.Identity;
using Zenith.Session;

namespace Zenith.Network.NetherNet;

/// <summary>
/// Accepts only GameServerTokens signed by the Franchise authorization service. NetherNet verifies tokens
/// synchronously while negotiating, so this keeps the signing keys in memory and refreshes them in the background
/// instead of fetching on the connection path.
/// </summary>
/// <param name="required">
/// True when only Franchise-authenticated peers may connect. False lets other tokens through (self-signed or offline
/// play is allowed) while <see cref="Authenticates"/> still tells the two apart.
/// </param>
/// <param name="onRejected">Called with the reason whenever a token fails verification.</param>
internal sealed class FranchiseTokenVerifier(bool required, Action<string> onRejected) : ITokenVerifier
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonWebToken, object> _verdicts = new();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    private volatile IReadOnlyList<RSAParameters>? _keys;

    /// <summary>True once a key set has been loaded; before that every token is rejected.</summary>
    public bool HasKeys => _keys is not null;

    /// <summary>Loads the key set now and then keeps it fresh until cancelled. Failures are retried, never thrown.</summary>
    public async Task KeepKeysFreshAsync(Action<string> onFailure, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = RefreshInterval;
            try
            {
                _keys = await FranchiseTokenValidator.GetKeysAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or
                                          System.Text.Json.JsonException or CryptographicException)
            {
                onFailure($"Franchise signing keys could not be loaded ({e.GetType().Name}); retrying.");
                delay = _keys is null ? RetryInterval : RefreshInterval;
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>True when the token was issued by the Franchise authorization service (result is remembered per token).</summary>
    public bool Authenticates(JsonWebToken token)
    {
        if (_verdicts.TryGetValue(token, out var known))
            return (bool)known;

        string? failure = null;
        var verdict = FranchiseTokenValidator.Validate(token.ToString(), _keys, reason => failure = reason);
        _verdicts.Add(token, verdict);
        if (!verdict)
            onRejected(failure ?? "token rejected");
        return verdict;
    }

    public void Check(JsonWebToken token)
    {
        if (!Authenticates(token) && required)
            throw new IdentityException("Token was not issued by the Franchise authorization service");
    }
}
