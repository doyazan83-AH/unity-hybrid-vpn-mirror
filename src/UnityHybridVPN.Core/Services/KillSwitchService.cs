using System.Diagnostics;

namespace UnityHybridVPN.Core.Services;

/// <summary>
/// Manages Windows Defender Firewall Kill Switch rules to block non-tunnel outbound traffic
/// when enabled, and guarantees cleanup on disconnect or application exit.
/// </summary>
public static class KillSwitchService
{
    private const string RuleName = "UnityHybridVPN_KillSwitch_Block";

    public static bool IsActive { get; private set; }

    public static (bool Ok, string Message) Enable(string? allowedServerIp = null)
    {
        Disable();
        string args = $"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block profile=any enable=yes";
        var (code, err) = RunNetsh(args);
        if (code != 0)
            return (false, $"Kill Switch requires Administrator privileges ({err}).");

        if (!string.IsNullOrWhiteSpace(allowedServerIp))
        {
            RunNetsh($"advfirewall firewall add rule name=\"{RuleName}_AllowServer\" dir=out action=allow remoteip={allowedServerIp} profile=any enable=yes");
        }

        IsActive = true;
        return (true, "Kill Switch Active");
    }

    public static void Disable()
    {
        RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}_AllowServer\"");
        IsActive = false;
    }

    private static (int Code, string Error) RunNetsh(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            if (p == null) return (-1, "Failed to start netsh");
            p.WaitForExit(3000);
            string err = p.StandardError.ReadToEnd().Trim();
            return (p.ExitCode, err);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
