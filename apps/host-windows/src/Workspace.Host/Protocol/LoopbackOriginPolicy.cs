using System.Net;

namespace Workspace.Host.Protocol;

public static class LoopbackOriginPolicy
{
    public static bool IsTrusted(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && string.Equals(uri.Host, "workspace.local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address)
            && IPAddress.IsLoopback(address);
    }
}
