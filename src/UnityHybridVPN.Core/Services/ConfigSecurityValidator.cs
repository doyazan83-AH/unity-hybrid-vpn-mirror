using UnityHybridVPN.Core.Models;

namespace UnityHybridVPN.Core.Services;

public sealed record ConfigValidationResult(bool IsValid, string? Error, string Endpoint);

/// <summary>
/// Validates WireGuard (.conf) and OpenVPN (.ovpn) files, blocking dangerous
/// arbitrary shell-execution directives (Security by Design).
/// </summary>
public static class ConfigSecurityValidator
{
    private static readonly string[] BlockedOpenVpnDirectives =
    {
        "script-security", "up ", "down ", "route-up", "ipchange", "client-connect", "learn-address", "plugin "
    };

    private static readonly string[] BlockedWireGuardDirectives =
    {
        "postup", "predown", "preup", "postdown"
    };

    public static ConfigValidationResult Validate(VpnProtocol protocol, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ConfigValidationResult(false, "Configuration content is empty.", "");

        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string endpoint = string.Empty;

        if (protocol == VpnProtocol.WireGuard)
        {
            bool hasInterface = false, hasPeer = false, hasPrivateKey = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith('#') || line.StartsWith(';')) continue;
                string lower = line.ToLowerInvariant();

                if (BlockedWireGuardDirectives.Any(d => lower.StartsWith(d)))
                    return new ConfigValidationResult(false, $"Blocked unsafe directive: {line}", "");

                if (lower == "[interface]") hasInterface = true;
                if (lower == "[peer]") hasPeer = true;
                if (lower.StartsWith("privatekey")) hasPrivateKey = true;
                if (lower.StartsWith("endpoint"))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2) endpoint = parts[1].Trim();
                }
            }

            if (!hasInterface || !hasPeer || !hasPrivateKey)
                return new ConfigValidationResult(false, "Invalid WireGuard config: missing [Interface], [Peer], or PrivateKey.", "");

            return new ConfigValidationResult(true, null, endpoint);
        }
        else
        {
            bool hasClient = false, hasRemote = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith('#') || line.StartsWith(';')) continue;
                string lower = line.ToLowerInvariant();

                if (BlockedOpenVpnDirectives.Any(d => lower.StartsWith(d) || lower.StartsWith("--" + d)))
                    return new ConfigValidationResult(false, $"Blocked unsafe OpenVPN directive: {line}", "");

                if (lower == "client") hasClient = true;
                if (lower.StartsWith("remote "))
                {
                    hasRemote = true;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) endpoint = parts[1];
                }
            }

            if (!hasClient || !hasRemote)
                return new ConfigValidationResult(false, "Invalid OpenVPN config: missing 'client' or 'remote' directive.", "");

            return new ConfigValidationResult(true, null, endpoint);
        }
    }
}
