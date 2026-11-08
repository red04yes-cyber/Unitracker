using System.Net;
using System.Net.Sockets;

namespace UniToStudyHub.Security;

/// <summary>An accepted base endpoint plus whether it points at the local machine.</summary>
public sealed record OutboundEndpoint(Uri BaseUri, bool IsLoopback);

/// <summary>
/// Validates every user-configured base URL (Moodle, AI provider) before an
/// HTTP request is created:
/// <list type="bullet">
/// <item>HTTPS is mandatory; plain HTTP is accepted only for loopback while a
/// caller explicitly opts in for local development.</item>
/// <item>Credentials, query strings and fragments are rejected so no secret
/// can be smuggled into a URL.</item>
/// <item>Loopback, link-local, private, CGNAT, multicast and unique-local
/// addresses are rejected for remote endpoints (SSRF and metadata-service
/// protection).</item>
/// </list>
/// DNS names are deliberately not resolved here: resolution happens once in
/// the HTTP stack and a redirect is never followed, which keeps this policy
/// deterministic and testable.
/// </summary>
public static class OutboundEndpointPolicy
{
    private static readonly string[] BlockedHostSuffixes =
    {
        ".localhost", ".local", ".internal", ".lan", ".intranet", ".home.arpa"
    };

    private static readonly string[] BlockedHostNames =
    {
        "localhost", "metadata", "metadata.google.internal", "instance-data"
    };

    public static OutboundEndpoint ValidateBaseUrl(string? url, bool allowLoopbackHttp, string parameterName)
    {
        if (TryValidateBaseUrl(url, allowLoopbackHttp, out var endpoint, out var error)) return endpoint!;
        throw new ArgumentException(error, parameterName);
    }

    public static bool TryValidateBaseUrl(
        string? url,
        bool allowLoopbackHttp,
        out OutboundEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Inserisci un indirizzo completo (https://...).";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "L'indirizzo non è valido.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "L'indirizzo non deve contenere credenziali.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "L'indirizzo deve essere la radice del servizio, senza query o frammenti.";
            return false;
        }

        var loopback = IsLoopbackHost(uri);
        if (loopback)
        {
            // A local endpoint is reachable only when the caller opts in for
            // local development; otherwise it is an SSRF target like any other.
            if (!allowLoopbackHttp)
            {
                error = "L'indirizzo punta a un endpoint locale non consentito.";
                return false;
            }
        }
        else if (IsBlockedHost(uri.Host))
        {
            error = "L'indirizzo punta a un host locale o privato non consentito.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            // Accepted.
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && allowLoopbackHttp && loopback)
        {
            // Explicit local-development escape hatch only.
        }
        else
        {
            error = "È richiesto HTTPS. HTTP è ammesso solo per un endpoint locale esplicitamente abilitato.";
            return false;
        }

        var normalized = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/", UriKind.Absolute);
        endpoint = new OutboundEndpoint(normalized, loopback);
        return true;
    }

    public static bool IsBlockedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var candidate = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (candidate.Length == 0) return true;
        if (BlockedHostNames.Contains(candidate, StringComparer.Ordinal)) return true;
        if (BlockedHostSuffixes.Any(suffix => candidate.EndsWith(suffix, StringComparison.Ordinal))) return true;
        // Decimal/hex encoded IPv4 literals ("2130706433", "0x7f000001").
        if (candidate.All(char.IsDigit)) return true;
        if (candidate.StartsWith("0x", StringComparison.Ordinal) &&
            candidate[2..].All(Uri.IsHexDigit)) return true;

        var unwrapped = candidate.StartsWith('[') && candidate.EndsWith(']')
            ? candidate[1..^1]
            : candidate;
        if (IPAddress.TryParse(unwrapped, out var address)) return IsPrivateOrLoopbackAddress(address);
        return false;
    }

    public static bool IsPrivateOrLoopbackAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6) return IsPrivateOrLoopbackAddress(address.MapToIPv4());
            var v6 = address.GetAddressBytes();
            if (address.Equals(IPAddress.IPv6Any)) return true;
            if (address.Equals(IPAddress.IPv6Loopback)) return true;
            if (address.IsIPv6LinkLocal) return true;
            if (address.IsIPv6SiteLocal) return true;
            if ((v6[0] & 0xFE) == 0xFC) return true;   // fc00::/7 unique local
            if (v6[0] == 0xFF) return true;            // ff00::/8 multicast
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 => true,                                       // 0.0.0.0/8
            10 => true,                                      // 10.0.0.0/8
            100 => bytes[1] is >= 64 and <= 127,             // 100.64.0.0/10 CGNAT
            127 => true,                                     // loopback
            169 => bytes[1] == 254,                          // link-local
            172 => bytes[1] is >= 16 and <= 31,              // 172.16.0.0/12
            192 => (bytes[1] == 168) || (bytes[1] == 0 && bytes[2] == 0), // 192.168/16, 192.0.0/24
            198 => bytes[1] is 18 or 19,                     // benchmarking
            >= 224 => true,                                  // multicast + reserved
            _ => false
        };
    }

    /// <summary>Scheme, host and port only: safe to show in a message or a log.</summary>
    public static string Describe(Uri? uri) =>
        uri is null ? string.Empty : uri.GetLeftPart(UriPartial.Path);

    private static bool IsLoopbackHost(Uri uri)
    {
        if (uri.IsLoopback) return true;
        var host = uri.Host.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
