using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UnityHybridVPN.Core.Services;

/// <summary>
/// Built-in zero-install encrypted tunnel using Windows 10/11 native OpenSSH (ssh -D)
/// paired with safe WinINet SOCKS5 system proxy activation and cleanup.
/// </summary>
public sealed class SshTunnelService : IDisposable
{
    private Process? _sshProcess;
    public bool IsConnected => _sshProcess is { HasExited: false };
    public int LocalSocksPort { get; private set; } = 10809;

    public static string? FindSshExe()
    {
        string sysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        return File.Exists(sysPath) ? sysPath : null;
    }

    public static string BuildArguments(string host, int port, string username, int localSocksPort, string? keyPath = null)
    {
        string safeHost = host.Trim();
        string safeUser = username.Trim();
        string keyArg = !string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath)
            ? $" -i \"{keyPath.Trim()}\""
            : string.Empty;

        return $"-N -D 127.0.0.1:{localSocksPort} -p {port}{keyArg} -o ServerAliveInterval=30 -o ExitOnForwardFailure=yes {safeUser}@{safeHost}";
    }

    public (bool Ok, string Message) Connect(string host, int port, string username, string? keyPath = null, int localSocksPort = 10809)
    {
        Disconnect();
        string? sshExe = FindSshExe();
        if (sshExe == null)
            return (false, "Built-in OpenSSH (ssh.exe) was not found on this Windows system.");

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            return (false, "Host IP and Username are required.");

        LocalSocksPort = localSocksPort;
        string args = BuildArguments(host, port, username, localSocksPort, keyPath);

        _sshProcess = Process.Start(new ProcessStartInfo(sshExe, args)
        {
            UseShellExecute = true
        });

        EnableSocksProxy(localSocksPort);
        return (true, $"SSH Tunnel Active (127.0.0.1:{localSocksPort} -> {host})");
    }

    public void Disconnect()
    {
        try
        {
            if (_sshProcess is { HasExited: false })
                _sshProcess.Kill(entireProcessTree: true);
        }
        catch { }
        _sshProcess?.Dispose();
        _sshProcess = null;
        DisableSystemProxy();
    }

    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public static void EnableSocksProxy(int port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
        if (key == null) return;
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"socks=127.0.0.1:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", "localhost;127.*;10.*;192.168.*;<local>", RegistryValueKind.String);
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    public static void DisableSystemProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key == null) return;
            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
        catch { }
    }

    public void Dispose() => Disconnect();
}
