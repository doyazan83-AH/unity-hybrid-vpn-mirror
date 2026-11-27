using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using UnityHybridVPN.Core.Models;

namespace UnityHybridVPN.Core.Services;

/// <summary>
/// Fetches and parses public VPNGate academic volunteer servers (CSV + Base64 .ovpn)
/// and runs parallel TCP latency health checks.
/// </summary>
public sealed class VpnGateDirectoryService
{
    private static readonly string[] DefaultUrls =
    {
        "https://raw.githubusercontent.com/doyazan83-AH/unity-hybrid-vpn-mirror/main/vpngate_cache.csv",
        "https://www.vpngate.net/api/iphone/",
        "http://www.vpngate.net/api/iphone/"
    };

    public static string CacheFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UnityHybridVPN", "vpngate_cache.csv");

    public static string MirrorUrlFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UnityHybridVPN", "mirror.txt");

    public static string LoadSavedMirrorUrl()
    {
        try { return File.Exists(MirrorUrlFilePath) ? File.ReadAllText(MirrorUrlFilePath).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    public static void SaveMirrorUrl(string? url)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MirrorUrlFilePath)!);
            File.WriteAllText(MirrorUrlFilePath, url?.Trim() ?? string.Empty);
        }
        catch { }
    }

    public async Task<List<VpnGateNode>> FetchNodesAsync(string? customMirrorUrl = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(customMirrorUrl))
            SaveMirrorUrl(customMirrorUrl);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(customMirrorUrl))
            urls.Add(customMirrorUrl.Trim());
        urls.AddRange(DefaultUrls);

        Exception? lastEx = null;
        foreach (var url in urls)
        {
            try
            {
                string csv = await http.GetStringAsync(url, ct);
                var parsed = ParseCsv(csv);
                if (parsed.Count > 0)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
                        await File.WriteAllTextAsync(CacheFilePath, csv, ct);
                    }
                    catch { }
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        if (File.Exists(CacheFilePath))
        {
            var cached = ParseCsv(await File.ReadAllTextAsync(CacheFilePath, ct));
            if (cached.Count > 0) return cached;
        }

        throw new InvalidOperationException($"All VPNGate sources failed. Last error: {lastEx?.Message}");
    }

    public static List<VpnGateNode> ParseCsv(string csv)
    {
        var nodes = new List<VpnGateNode>();
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith('*') || line.StartsWith('#')) continue;
            var cols = line.Split(',');
            if (cols.Length < 15) continue;

            if (!long.TryParse(cols[2], out long score)) score = 0;
            if (!int.TryParse(cols[3], out int ping)) ping = 999;
            if (!double.TryParse(cols[4], out double speedBps)) speedBps = 0;

            string b64 = cols[14].Trim();
            if (string.IsNullOrEmpty(b64)) continue;

            nodes.Add(new VpnGateNode
            {
                HostName = cols[0].Trim(),
                Ip = cols[1].Trim(),
                Score = score,
                PingMs = ping,
                SpeedMbps = Math.Round(speedBps / 1_000_000.0, 2),
                CountryLong = cols[5].Trim(),
                CountryShort = cols[6].Trim(),
                OpenVpnConfigBase64 = b64
            });
        }

        return nodes.OrderByDescending(n => n.SpeedMbps).Take(60).ToList();
    }

    public static string DecodeOpenVpnConfig(string base64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    public async Task<List<VpnGateNode>> ProbeAndSortFastestAsync(IEnumerable<VpnGateNode> nodes, CancellationToken ct = default)
    {
        var list = nodes.ToList();
        using var gate = new SemaphoreSlim(20);

        var tasks = list.Select(async node =>
        {
            await gate.WaitAsync(ct);
            try
            {
                string host = node.HostName.Contains('.') ? node.HostName : $"{node.HostName}.opengw.net";
                using var tcp = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(2000);

                var sw = Stopwatch.StartNew();
                await tcp.ConnectAsync(host, 443, timeoutCts.Token);
                using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions
                {
                    TargetHost = host
                }, timeoutCts.Token);
                sw.Stop();

                node.PingMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
                node.IsSstpReady = true;
            }
            catch
            {
                node.IsSstpReady = false;
                node.PingMs = 9999;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return SortByFastest(list);
    }

    public static List<VpnGateNode> SortByFastest(IEnumerable<VpnGateNode> nodes) =>
        nodes.OrderByDescending(n => n.IsSstpReady)
             .ThenByDescending(n => n.SpeedMbps / Math.Max(30.0, n.PingMs))
             .ThenBy(n => n.PingMs)
             .ToList();
}
