using System.Diagnostics;

namespace UnityHybridVPN.Core.Services;

public enum NativeVpnProtocol
{
    L2tp,
    Sstp
}

/// <summary>
/// Connects using Windows 10/11 built-in VPN engine (Add-VpnConnection + rasdial.exe)
/// via L2TP/IPsec or SSTP without requiring WireGuard or OpenVPN downloads.
/// </summary>
public sealed class NativeWindowsVpnService : IDisposable
{
    private const string ConnectionName = "UnityHybridVPN_Native";
    public bool IsConnected { get; private set; }

    public static bool IsSupported()
    {
        string rasdial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rasdial.exe");
        return File.Exists(rasdial);
    }

    public (bool Ok, string Message) Connect(
        string serverAddress,
        NativeVpnProtocol protocol = NativeVpnProtocol.L2tp,
        string username = "vpn",
        string password = "vpn",
        string psk = "vpn")
    {
        if (!IsSupported())
            return (false, "Windows rasdial.exe was not found.");
        if (string.IsNullOrWhiteSpace(serverAddress))
            return (false, "Server IP or hostname is required.");

        Disconnect();

        string safeServer = serverAddress.Trim().Replace("'", "''");
        string safePsk = psk.Replace("'", "''");
        string tunnelType = protocol == NativeVpnProtocol.Sstp ? "Sstp" : "L2tp";
        string l2tpPskArg = protocol == NativeVpnProtocol.L2tp ? $" -L2tpPsk '{safePsk}'" : string.Empty;

        string psCmd =
            $"rasdial '{ConnectionName}' /disconnect | Out-Null; " +
            $"if (Get-VpnConnection -Name '{ConnectionName}' -ErrorAction SilentlyContinue) {{ " +
            $"  Set-VpnConnection -Name '{ConnectionName}' -ServerAddress '{safeServer}' -TunnelType {tunnelType}{l2tpPskArg} " +
            $"  -EncryptionLevel Optional -AuthenticationMethod Pap,Chap,MSChapv2 -Force " +
            $"}} else {{ " +
            $"  Add-VpnConnection -Name '{ConnectionName}' -ServerAddress '{safeServer}' -TunnelType {tunnelType}{l2tpPskArg} " +
            $"  -EncryptionLevel Optional -AuthenticationMethod Pap,Chap,MSChapv2 -Force " +
            $"}}";

        var (psCode, psErr) = RunTool("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"");
        if (psCode != 0)
            return (false, $"Failed to configure Windows VPN: {psErr}");

        var (dialCode, dialOut) = RunTool("rasdial.exe", $"\"{ConnectionName}\" \"{username}\" \"{password}\"");
        if (dialCode != 0)
        {
            Disconnect();
            if (dialOut.Contains("789"))
                return (false, "Error 789: L2TP blocked by ISP/NAT. Try SSTP (443) or OpenVPN.");
            string oneLine = dialOut.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length > 110) oneLine = oneLine[..110] + "...";
            return (false, $"Connection failed: {oneLine}");
        }

        IsConnected = true;
        return (true, $"Connected via Windows {tunnelType} ({serverAddress})");
    }

    public void Disconnect()
    {
        try
        {
            RunTool("rasdial.exe", $"\"{ConnectionName}\" /disconnect");
            RunTool("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"Remove-VpnConnection -Name '{ConnectionName}' -Force -ErrorAction SilentlyContinue\"");
        }
        catch { }
        IsConnected = false;
    }

    private static (int Code, string Output) RunTool(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p == null) return (-1, "Failed to start process");
            p.WaitForExit(15000);
            string outText = (p.StandardOutput.ReadToEnd() + "\n" + p.StandardError.ReadToEnd()).Trim();
            return (p.ExitCode, outText);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public void Dispose() => Disconnect();
}
