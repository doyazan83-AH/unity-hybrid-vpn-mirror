namespace UnityHybridVPN.Core.Models;

public enum VpnProtocol
{
    WireGuard,
    OpenVPN
}

public enum TrustLevel
{
    PrivateSelfHosted,
    CommunityUntrusted
}

public sealed class VpnProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = "Custom";
    public VpnProtocol Protocol { get; set; }
    public TrustLevel Trust { get; set; } = TrustLevel.PrivateSelfHosted;
    public string Endpoint { get; set; } = string.Empty;
    public string ConfigContent { get; set; } = string.Empty;
    public int PingMs { get; set; } = -1;
}

public sealed class VpnGateNode
{
    public string HostName { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public long Score { get; init; }
    public int PingMs { get; set; }
    public double SpeedMbps { get; init; }
    public string CountryLong { get; init; } = string.Empty;
    public string CountryShort { get; init; } = string.Empty;
    public string OpenVpnConfigBase64 { get; init; } = string.Empty;
}
