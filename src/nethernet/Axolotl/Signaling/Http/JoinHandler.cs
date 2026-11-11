using System.Text;
using System.Text.RegularExpressions;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

/// <summary>The outcome of handling one HTTP request: nothing (drop), an immediate response, or a negotiation to wait on.</summary>
public readonly record struct JoinResult(HttpResponse? Response, INegotiation? Negotiation)
{
    public static JoinResult Drop => default;
    public static JoinResult Respond(HttpResponse response) => new(response, null);
    public static JoinResult Negotiate(INegotiation negotiation) => new(null, negotiation);
}

/// <summary>
/// Routes <c>GET /v1/join</c> (server status) and <c>POST /v1/join/{networkId}</c> (SDP offer to answer)
/// (port of <c>JoinHandler</c>).
/// </summary>
public sealed partial class JoinHandler(
    INegotiator negotiator,
    IAddressBlockList blockList,
    IServerInfoProvider? infoProvider,
    ReverseProxy? reverseProxy,
    INetherNetLogger logger)
{
    private const string Path = "/v1/join";
    private const string OfferPathPrefix = Path + "/";
    private const string MaxNetworkId = "18446744073709551615";
    public const string ContentTypeSdp = "application/sdp";
    public const string ContentTypeJson = "application/json";
    private const string ContentTypeText = "text/plain; charset=utf-8";

    [GeneratedRegex(@"^\d{1,20}\z")]
    private static partial Regex NetworkIdPattern();

    /// <summary>Validates the method and path. Returns the NetworkID for an offer, or null for the status endpoint.</summary>
    public string? Route(HttpRequest request)
    {
        var path = request.GetPath();
        if (path == Path || path == OfferPathPrefix)
        {
            if (request.Method != HttpRequest.MethodGet && request.Method != HttpRequest.MethodHead)
                throw new HttpException(HttpStatus.MethodNotAllowed, "Method not allowed: endpoint only accepts GET or HEAD requests");
            return null;
        }

        if (!path.StartsWith(OfferPathPrefix, StringComparison.Ordinal))
            throw new HttpException(HttpStatus.NotFound, "No such endpoint");
        if (request.Method != HttpRequest.MethodPost)
            throw new HttpException(HttpStatus.MethodNotAllowed, "Method not allowed: SDP offers must be submitted via POST");
        if (!request.HasContentType(ContentTypeSdp))
            throw new HttpException(HttpStatus.UnsupportedMediaType, "Offers must be sent as " + ContentTypeSdp);

        var networkId = Uri.UnescapeDataString(path[OfferPathPrefix.Length..]);
        if (networkId == "")
            throw new HttpException(HttpStatus.NotFound, "No NetworkID in the request path");
        if (Encoding.UTF8.GetByteCount(networkId) > HttpSignaling.MaxNetworkIdLength)
            throw new HttpException(HttpStatus.UriTooLong, $"NetworkID is longer than {HttpSignaling.MaxNetworkIdLength} bytes");
        if (!NetworkIdPattern().IsMatch(networkId) || (networkId.Length == 20 && string.CompareOrdinal(networkId, MaxNetworkId) > 0))
            throw new HttpException(HttpStatus.BadRequest, "NetworkID must be an unsigned 64-bit integer");
        return networkId;
    }

    public JoinResult Handle(HttpRequest request, string body, InternetAddress? peerAddress)
    {
        var networkId = Route(request);

        // Behind a trusted reverse proxy the real client is named by a header, not by the TCP peer.
        var clientAddress = peerAddress?.Ip;
        if (clientAddress is not null && reverseProxy is not null && reverseProxy.Trusts(clientAddress))
        {
            clientAddress = reverseProxy.ClientAddress(request);
            if (clientAddress is null && networkId is not null)
            {
                logger.Warning($"Refused an offer from trusted proxy {peerAddress} without a usable {reverseProxy.Header} header");
                throw new HttpException(HttpStatus.BadRequest, $"No client address in the {reverseProxy.Header} header");
            }
        }

        if (clientAddress is not null && blockList.IsBlocked(clientAddress))
        {
            logger.Debug($"{peerAddress?.ToString() ?? "unknown"}: dropped a request from {clientAddress}, which is blocked");
            return JoinResult.Drop;
        }

        if (networkId is null)
            return JoinResult.Respond(Probe());

        try
        {
            return JoinResult.Negotiate(negotiator.BeginNegotiation(body, networkId, peerAddress: clientAddress));
        }
        catch (NegotiationException e)
        {
            logger.Debug("Refused an offer: " + e.Message);
            return JoinResult.Respond(Failure(e.ErrorCode));
        }
    }

    // The client reads the error code from the body of a 500.
    private static HttpResponse Failure(ErrorCode code) =>
        new(HttpStatus.InternalServerError, ((int)code).ToString(), ContentTypeText);

    private HttpResponse Probe()
    {
        var info = infoProvider?.GetServerInfo();
        if (info is not null)
        {
            try
            {
                return new HttpResponse(HttpStatus.Ok, ServerInfoCodec.EncodeJson(info), ContentTypeJson);
            }
            catch (System.Text.Json.JsonException e)
            {
                logger.Debug("Could not build the server status: " + e.Message);
            }
        }
        return new HttpResponse(HttpStatus.Ok);
    }

    /// <summary>The response for a finished negotiation, or null while it is still in progress.</summary>
    public HttpResponse? RespondTo(INegotiation negotiation)
    {
        var answer = negotiation.Answer;
        if (answer is not null)
            return new HttpResponse(HttpStatus.Ok, answer, ContentTypeSdp);

        if (negotiation.IsFailed)
        {
            logger.Debug("Negotiation failed: " + (negotiation.FailureReason ?? "no reason given"));
            return Failure(negotiation.FailureCode);
        }
        return null;
    }
}
