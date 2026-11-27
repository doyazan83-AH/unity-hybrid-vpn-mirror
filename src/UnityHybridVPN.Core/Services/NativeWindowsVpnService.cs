using System.Diagnostics;

namespace UnityHybridVPN.Core.Services;

public enum NativeVpnProtocol
{
    L2tp,
    Sstp
}

/// <summary>
/// Connects using Windows 10/11 built-in VPN engine (Add-VpnConnection + rasdial.exe)
/// via SSTP or L2TP/IPsec without requiring WireGuard or OpenVPN downloads.
/// Guards against Error 756 and stale RAS profiles.
/// </summary>
public sealed class NativeWindowsVpnService : IDisposable
{
    private const string Prefix = "UnityHybridVPN_";
    private string _currentConn = "UnityHybridVPN_Conn";
    public bool IsConnected { get; private set; }

    public static bool IsSupported()
    {
        string rasdial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rasdial.exe");
        return File.Exists(rasdial);
    }

    public (bool Ok, string Message) Connect(
        string serverAddress,
        NativeVpnProtocol protocol = NativeVpnProtocol.Sstp,
        string username = "vpn",
        string password = "vpn",
        string psk = "vpn")
    {
        if (!IsSupported())
            return (false, "Windows rasdial.exe was not found.");
        if (string.IsNullOrWhiteSpace(serverAddress))
            return (false, "Server IP or hostname is required.");

        Disconnect();

        // Fresh profile name avoids Windows RasMan Error 756 ("already being dialed") locks
        _currentConn = Prefix + DateTime.UtcNow.ToString("HHmmssff");

        string safeServer = serverAddress.Trim().Replace("'", "''");
        string safePsk = psk.Replace("'", "''");
        string tunnelType = protocol == NativeVpnProtocol.Sstp ? "Sstp" : "L2tp";
        string l2tpPskArg = protocol == NativeVpnProtocol.L2tp ? $" -L2tpPsk '{safePsk}'" : string.Empty;

        string psCmd =
            $"Add-VpnConnection -Name '{_currentConn}' -ServerAddress '{safeServer}' -TunnelType {tunnelType}{l2tpPskArg} " +
            $"-EncryptionLevel Optional -AuthenticationMethod Pap,Chap,MSChapv2 -Force";

        var (psCode, psErr) = RunTool("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"", 10000);
        if (psCode != 0)
            return (false, $"VPN setup failed: {psErr}");

        var (dialCode, dialOut) = RunTool("rasdial.exe", $"\"{_currentConn}\" \"{username}\" \"{password}\"", 15000);
        if (dialCode != 0)
        {
            Disconnect();
            if (dialOut.Contains("789"))
                return (false, "Error 789: L2TP blocked by ISP/NAT. Use SSTP (443).");
            if (dialOut.Contains("756"))
                return (false, "Error 756: Windows RAS busy. Try again in 2 seconds.");
            string oneLine = dialOut.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length > 100) oneLine = oneLine[..100] + "...";
            return (false, $"Failed: {oneLine}");
        }

        IsConnected = true;
        return (true, $"Connected ({tunnelType}) -> {serverAddress}");
    }

    public void Disconnect()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("rasdial"))
            {
                try { p.Kill(); } catch { }
            }
            RunTool("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-VpnConnection | Where-Object {{ $_.Name -like '{Prefix}*' }} | ForEach-Object {{ rasdial $_.Name /disconnect | Out-Null; Remove-VpnConnection -Name $_.Name -Force -ErrorAction SilentlyContinue }}\"",
                6000);
        }
        catch { }
        IsConnected = false;
    }

    private static (int Code, string Output) RunTool(string exe, string args, int timeoutMs)
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
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-2, "Connection attempt timed out.");
            }
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
