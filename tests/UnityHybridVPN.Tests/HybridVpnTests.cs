using System.Text;
using UnityHybridVPN.Core.Models;
using UnityHybridVPN.Core.Services;

namespace UnityHybridVPN.Tests;

public static class Program
{
    public static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("WireGuardConfig_ValidatesCleanConfig", ValidWireGuard),
            ("WireGuardConfig_BlocksUnsafePostUpDirective", BlocksUnsafeWireGuard),
            ("OpenVpnConfig_BlocksUnsafeScriptSecurity", BlocksUnsafeOpenVpn),
            ("VpnGateCsv_ParsesNodesAndDecodesBase64", ParsesVpnGateCsv),
            ("SelfHostScript_GeneratesValidWireGuardScript", GeneratesSelfHostScript),
            ("SshTunnel_FindsBuiltInWindowsSshAndBuildsArgs", SshTunnelBuildsArgs),
            ("NativeWindowsVpn_IsSupportedOnWindows", () => Assert(NativeWindowsVpnService.IsSupported(), "rasdial.exe must exist")),
            ("VpnGateSort_RanksReadyAndFastestNodesFirst", SortRanksReadyAndFastestFirst),
            ("VpnGateSort_LowestPingRanksLowestLatencyFirst", SortLowestPingRanksLowestFirst)
        };

        int failed = 0;
        foreach (var (name, run) in tests)
        {
            try { run(); Console.WriteLine($"[PASS] {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"[FAIL] {name}: {ex.Message}"); }
        }
        Console.WriteLine($"\nSummary: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }

    private static void ValidWireGuard()
    {
        string cfg = "[Interface]\nPrivateKey = abc123=\nAddress = 10.66.0.2/24\n[Peer]\nPublicKey = def456=\nEndpoint = 1.2.3.4:51820\n";
        var res = ConfigSecurityValidator.Validate(VpnProtocol.WireGuard, cfg);
        Assert(res.IsValid && res.Endpoint == "1.2.3.4:51820", "Valid WG config should pass");
    }

    private static void BlocksUnsafeWireGuard()
    {
        string cfg = "[Interface]\nPrivateKey = abc=\nPostUp = calc.exe\n[Peer]\nEndpoint = 1.2.3.4:51820\n";
        var res = ConfigSecurityValidator.Validate(VpnProtocol.WireGuard, cfg);
        Assert(!res.IsValid, "Unsafe PostUp must be blocked");
    }

    private static void BlocksUnsafeOpenVpn()
    {
        string cfg = "client\nremote 1.2.3.4 1194\nscript-security 2\nup evil.bat\n";
        var res = ConfigSecurityValidator.Validate(VpnProtocol.OpenVPN, cfg);
        Assert(!res.IsValid, "Unsafe OpenVPN script-security/up must be blocked");
    }

    private static void ParsesVpnGateCsv()
    {
        string ovpn = "client\nremote 203.0.113.5 1194\n";
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ovpn));
        string csv = $"*vpn_servers\n#HostName,IP,Score,Ping,Speed,CountryLong,CountryShort,NumVpnSessions,Uptime,TotalUsers,TotalTraffic,LogType,Operator,Message,OpenVPN_ConfigData_Base64\nvpn1,203.0.113.5,100000,25,50000000,Japan,JP,10,1000,50,1000,2wk,vol,,{b64}\n";
        var nodes = VpnGateDirectoryService.ParseCsv(csv);
        Assert(nodes.Count == 1 && nodes[0].CountryShort == "JP" && nodes[0].SpeedMbps == 50, "CSV parse failed");
        Assert(VpnGateDirectoryService.DecodeOpenVpnConfig(nodes[0].OpenVpnConfigBase64).Contains("203.0.113.5"), "Base64 decode failed");
    }

    private static void GeneratesSelfHostScript()
    {
        string script = SelfHostScriptGenerator.GenerateUbuntuWireGuardScript("198.51.100.7");
        Assert(script.Contains("198.51.100.7:51820") && script.Contains("wg genkey"), "Self-host script invalid");
    }

    private static void SshTunnelBuildsArgs()
    {
        Assert(SshTunnelService.FindSshExe() != null, "Built-in Windows OpenSSH must be found");
        string args = SshTunnelService.BuildArguments("203.0.113.10", 22, "ubuntu", 10809);
        Assert(args.Contains("-D 127.0.0.1:10809") && args.Contains("ubuntu@203.0.113.10"), "SSH args invalid");
    }

    private static void SortRanksReadyAndFastestFirst()
    {
        var list = new List<VpnGateNode>
        {
            new() { Ip = "1.1.1.1", IsSstpReady = false, SpeedMbps = 900, PingMs = 9999 },
            new() { Ip = "2.2.2.2", IsSstpReady = true, SpeedMbps = 100, PingMs = 200 },
            new() { Ip = "3.3.3.3", IsSstpReady = true, SpeedMbps = 250, PingMs = 60 }
        };
        var sorted = VpnGateDirectoryService.SortByFastest(list);
        Assert(sorted[0].Ip == "3.3.3.3" && sorted[1].Ip == "2.2.2.2" && sorted[2].Ip == "1.1.1.1", "Sort order incorrect");
    }

    private static void SortLowestPingRanksLowestFirst()
    {
        var list = new List<VpnGateNode>
        {
            new() { Ip = "1.1.1.1", IsSstpReady = true, SpeedMbps = 500, PingMs = 240 },
            new() { Ip = "2.2.2.2", IsSstpReady = true, SpeedMbps = 80, PingMs = 65 }
        };
        var sorted = VpnGateDirectoryService.SortByLowestPing(list);
        Assert(sorted[0].Ip == "2.2.2.2", "Lowest ping must rank first");
    }
}
