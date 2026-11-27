using System.Diagnostics;
using System.Text.Json;
using UnityHybridVPN.Core.Models;

namespace UnityHybridVPN.Core.Services;

public enum TunnelState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Manages local WireGuard/OpenVPN binaries on Windows, profile storage,
/// and tunnel lifecycle with safe cleanup.
/// </summary>
public sealed class TunnelController : IDisposable
{
    private Process? _activeProcess;
    public TunnelState State { get; private set; } = TunnelState.Disconnected;
    public VpnProfile? ActiveProfile { get; private set; }
    public string LastStatusMessage { get; private set; } = "Ready";

    public static string? FindWireGuardExe()
    {
        string[] paths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe")
        };
        return paths.FirstOrDefault(File.Exists);
    }

    public static string? FindOpenVpnExe()
    {
        string[] paths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenVPN", "bin", "openvpn.exe")
        };
        return paths.FirstOrDefault(File.Exists);
    }

    public async Task<(bool Ok, string Message)> ConnectAsync(VpnProfile profile)
    {
        Disconnect();
        var validation = ConfigSecurityValidator.Validate(profile.Protocol, profile.ConfigContent);
        if (!validation.IsValid)
        {
            State = TunnelState.Error;
            LastStatusMessage = validation.Error ?? "Invalid config";
            return (false, LastStatusMessage);
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "UnityHybridVPN");
        Directory.CreateDirectory(tempDir);

        if (profile.Protocol == VpnProtocol.WireGuard)
        {
            string? wg = FindWireGuardExe();
            if (wg == null)
            {
                State = TunnelState.Error;
                LastStatusMessage = "WireGuard for Windows is not installed (wireguard.exe not found).";
                return (false, LastStatusMessage);
            }
            string confPath = Path.Combine(tempDir, $"uhvpn_{profile.Id}.conf");
            await File.WriteAllTextAsync(confPath, profile.ConfigContent);
            _activeProcess = Process.Start(new ProcessStartInfo(wg, $"/installtunnelservice \"{confPath}\"")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        else
        {
            string? ovpn = FindOpenVpnExe();
            if (ovpn == null)
            {
                State = TunnelState.Error;
                LastStatusMessage = "OpenVPN for Windows is not installed (openvpn.exe not found).";
                return (false, LastStatusMessage);
            }
            string ovpnPath = Path.Combine(tempDir, $"uhvpn_{profile.Id}.ovpn");
            await File.WriteAllTextAsync(ovpnPath, profile.ConfigContent);
            _activeProcess = Process.Start(new ProcessStartInfo(ovpn, $"--config \"{ovpnPath}\" --disable-dco --data-ciphers AES-128-CBC:AES-256-GCM:AES-128-GCM:CHACHA20-POLY1305")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
        }

        ActiveProfile = profile;
        State = TunnelState.Connected;
        LastStatusMessage = $"Connected to {profile.Name} ({profile.Country})";
        return (true, LastStatusMessage);
    }

    public void Disconnect()
    {
        try
        {
            if (ActiveProfile?.Protocol == VpnProtocol.WireGuard)
            {
                string? wg = FindWireGuardExe();
                if (wg != null)
                {
                    Process.Start(new ProcessStartInfo(wg, $"/uninstalltunnelservice uhvpn_{ActiveProfile.Id}")
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    })?.WaitForExit(4000);
                }
            }
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
            string tempDir = Path.Combine(Path.GetTempPath(), "UnityHybridVPN");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch { }
        _activeProcess?.Dispose();
        _activeProcess = null;
        ActiveProfile = null;
        State = TunnelState.Disconnected;
        LastStatusMessage = "Disconnected";
    }

    public static List<VpnProfile> LoadProfiles()
    {
        try
        {
            string path = GetStorePath();
            if (!File.Exists(path)) return new List<VpnProfile>();
            return JsonSerializer.Deserialize<List<VpnProfile>>(File.ReadAllText(path)) ?? new List<VpnProfile>();
        }
        catch { return new List<VpnProfile>(); }
    }

    public static void SaveProfiles(List<VpnProfile> profiles)
    {
        string path = GetStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetStorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UnityHybridVPN", "profiles.json");

    public void Dispose() => Disconnect();
}
