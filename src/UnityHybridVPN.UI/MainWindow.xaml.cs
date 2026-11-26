using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using UnityHybridVPN.Core.Models;
using UnityHybridVPN.Core.Services;

namespace UnityHybridVPN.UI;

public partial class MainWindow : Window
{
    private readonly TunnelController _controller = new();
    private readonly NativeWindowsVpnService _nativeVpn = new();
    private readonly SshTunnelService _sshTunnel = new();
    private readonly VpnGateDirectoryService _vpnGate = new();
    private readonly List<VpnProfile> _profiles;
    private readonly ObservableCollection<VpnProfile> _profileRows = new();
    private readonly ObservableCollection<VpnGateNode> _vpnGateRows = new();
    private List<VpnGateNode> _allNodes = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isArabic = true;
    private bool _forceExit;

    public MainWindow()
    {
        InitializeComponent();
        _profiles = TunnelController.LoadProfiles();
        GridProfiles.ItemsSource = _profileRows;
        GridVpnGate.ItemsSource = _vpnGateRows;

        RefreshProfilesUi();
        CheckEngines();
        SetupTrayIcon();
        TxtMirrorUrl.Text = VpnGateDirectoryService.LoadSavedMirrorUrl();
        TxtSelfHostScript.Text = SelfHostScriptGenerator.GenerateUbuntuWireGuardScript(TxtVpsIp.Text);
    }

    private void SetupTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("فتح / Open", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("إغلاق نهائي / Exit", null, (_, _) => Dispatcher.Invoke(() => { _forceExit = true; Close(); }));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "UnityHybridVPN",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
    }

    private void CheckEngines()
    {
        bool hasWg = TunnelController.FindWireGuardExe() != null;
        bool hasOvpn = TunnelController.FindOpenVpnExe() != null;
        bool hasSsh = SshTunnelService.FindSshExe() != null;
        TxtEngines.Text = $"SSH: {(hasSsh ? "Ready ✓" : "Missing")} | WG: {(hasWg ? "✓" : "Missing")} | OVPN: {(hasOvpn ? "✓" : "Missing")}";
        BtnInstallEngines.Visibility = (!hasWg || !hasOvpn) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnBrowseSshKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "SSH Private Key (*.key;*.pem;*)|*.*" };
        if (dlg.ShowDialog() == true) TxtSshKey.Text = dlg.FileName;
    }

    private void BtnConnectSsh_Click(object sender, RoutedEventArgs e)
    {
        if (_sshTunnel.IsConnected)
        {
            _sshTunnel.Disconnect();
            BtnConnectSsh.Content = _isArabic ? "تشغيل نفق SSH" : "Start SSH Tunnel";
            TxtSshStatus.Text = "Disconnected.";
            TxtConnState.Text = _isArabic ? "غير متصل (Ready)" : "Disconnected (Ready)";
            TxtConnState.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            BtnDisconnect.IsEnabled = false;
            return;
        }

        int port = int.TryParse(TxtSshPort.Text, out int p) ? p : 22;
        var (ok, msg) = _sshTunnel.Connect(TxtSshHost.Text, port, TxtSshUser.Text, TxtSshKey.Text);
        TxtSshStatus.Text = msg;
        TxtConnState.Text = msg;
        TxtConnState.Foreground = new SolidColorBrush(ok ? System.Windows.Media.Color.FromRgb(16, 185, 129) : System.Windows.Media.Color.FromRgb(239, 68, 68));
        if (ok)
        {
            BtnConnectSsh.Content = _isArabic ? "إيقاف نفق SSH" : "Stop SSH Tunnel";
            BtnDisconnect.IsEnabled = true;
        }
    }

    private void BtnInstallEngines_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TunnelController.FindWireGuardExe() == null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.wireguard.com/install/") { UseShellExecute = true });
            if (TunnelController.FindOpenVpnExe() == null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://openvpn.net/community-downloads/") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnImportWg_Click(object sender, RoutedEventArgs e) => ImportConfig(VpnProtocol.WireGuard, "WireGuard Config (*.conf)|*.conf");
    private void BtnImportOvpn_Click(object sender, RoutedEventArgs e) => ImportConfig(VpnProtocol.OpenVPN, "OpenVPN Config (*.ovpn)|*.ovpn");

    private void ImportConfig(VpnProtocol protocol, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog() != true) return;

        string content = File.ReadAllText(dlg.FileName);
        var check = ConfigSecurityValidator.Validate(protocol, content);
        if (!check.IsValid)
        {
            System.Windows.MessageBox.Show(check.Error, "Security Validation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profiles.Add(new VpnProfile
        {
            Name = Path.GetFileNameWithoutExtension(dlg.FileName),
            Protocol = protocol,
            Country = "Private",
            Trust = TrustLevel.PrivateSelfHosted,
            Endpoint = check.Endpoint,
            ConfigContent = content
        });
        TunnelController.SaveProfiles(_profiles);
        RefreshProfilesUi();
    }

    private async void BtnConnectProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == id);
            if (profile == null) return;
            await ConnectProfileAsync(profile);
        }
    }

    private async Task ConnectProfileAsync(VpnProfile profile)
    {
        var (ok, msg) = await _controller.ConnectAsync(profile);
        TxtConnState.Text = msg;
        TxtConnState.Foreground = new SolidColorBrush(ok
            ? System.Windows.Media.Color.FromRgb(16, 185, 129)
            : System.Windows.Media.Color.FromRgb(239, 68, 68));
        BtnDisconnect.IsEnabled = ok;
    }

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _controller.Disconnect();
        _nativeVpn.Disconnect();
        _sshTunnel.Disconnect();
        KillSwitchService.Disable();
        if (ChkKillSwitch.IsChecked == true) ChkKillSwitch.IsChecked = false;
        BtnConnectSsh.Content = _isArabic ? "تشغيل نفق SSH" : "Start SSH Tunnel";
        TxtConnState.Text = _isArabic ? "غير متصل (Ready)" : "Disconnected (Ready)";
        TxtConnState.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
        BtnDisconnect.IsEnabled = false;
    }

    private void ChkKillSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkKillSwitch.IsChecked == true)
        {
            var (ok, msg) = KillSwitchService.Enable(_controller.ActiveProfile?.Endpoint);
            if (!ok)
            {
                System.Windows.MessageBox.Show(msg, "Kill Switch", MessageBoxButton.OK, MessageBoxImage.Information);
                ChkKillSwitch.IsChecked = false;
            }
        }
        else
        {
            KillSwitchService.Disable();
        }
    }

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
        {
            _profiles.RemoveAll(p => p.Id == id);
            TunnelController.SaveProfiles(_profiles);
            RefreshProfilesUi();
        }
    }

    private async void BtnFetchNodes_Click(object sender, RoutedEventArgs e)
    {
        BtnFetchNodes.IsEnabled = false;
        BtnFetchNodes.Content = _isArabic ? "جاري الجلب والفحص..." : "Fetching & Probing...";
        try
        {
            _allNodes = await _vpnGate.FetchNodesAsync(TxtMirrorUrl.Text);
            await _vpnGate.ProbePingsAsync(_allNodes);
            FilterVpnGateNodes();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"VPNGate fetch error: {ex.Message}");
        }
        finally
        {
            BtnFetchNodes.IsEnabled = true;
            BtnFetchNodes.Content = _isArabic ? "تحديث الخوادم المجانية الآن" : "Refresh Free Servers";
        }
    }

    private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "VPNGate CSV (*.csv)|*.csv|All Files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        _allNodes = VpnGateDirectoryService.ParseCsv(File.ReadAllText(dlg.FileName));
        FilterVpnGateNodes();
    }

    private void TxtCountryFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => FilterVpnGateNodes();

    private void FilterVpnGateNodes()
    {
        string q = TxtCountryFilter.Text.Trim();
        _vpnGateRows.Clear();
        foreach (var n in _allNodes.Where(n => string.IsNullOrEmpty(q) || n.CountryLong.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            _vpnGateRows.Add(n);
        }
    }

    private async void BtnUseVpnGateNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string ip })
        {
            var node = _allNodes.FirstOrDefault(n => n.Ip == ip);
            if (node == null) return;

            // If OpenVPN is not installed, connect directly using Windows built-in L2TP (zero downloads)
            if (TunnelController.FindOpenVpnExe() == null)
            {
                TxtConnState.Text = $"Connecting Windows L2TP -> {node.Ip}...";
                var (ok, msg) = await Task.Run(() => _nativeVpn.Connect(node.Ip, NativeVpnProtocol.L2tp));
                TxtConnState.Text = msg;
                TxtConnState.Foreground = new SolidColorBrush(ok ? System.Windows.Media.Color.FromRgb(16, 185, 129) : System.Windows.Media.Color.FromRgb(239, 68, 68));
                BtnDisconnect.IsEnabled = ok;
                return;
            }

            string ovpn = VpnGateDirectoryService.DecodeOpenVpnConfig(node.OpenVpnConfigBase64);
            var profile = new VpnProfile
            {
                Name = $"VPNGate-{node.CountryShort}-{node.Ip}",
                Country = node.CountryLong,
                Protocol = VpnProtocol.OpenVPN,
                Trust = TrustLevel.CommunityUntrusted,
                Endpoint = node.Ip,
                ConfigContent = ovpn,
                PingMs = node.PingMs
            };
            _profiles.Add(profile);
            TunnelController.SaveProfiles(_profiles);
            RefreshProfilesUi();
            await ConnectProfileAsync(profile);
        }
    }

    private async void BtnNativeConnect_Click(object sender, RoutedEventArgs e)
    {
        string ip = TxtNativeIp.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip)) return;
        var tag = (CmbNativeProto.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        var proto = tag == "Sstp" ? NativeVpnProtocol.Sstp : NativeVpnProtocol.L2tp;
        TxtConnState.Text = $"Connecting Windows {proto} -> {ip}...";
        var (ok, msg) = await Task.Run(() => _nativeVpn.Connect(ip, proto));
        TxtConnState.Text = msg;
        TxtConnState.Foreground = new SolidColorBrush(ok ? System.Windows.Media.Color.FromRgb(16, 185, 129) : System.Windows.Media.Color.FromRgb(239, 68, 68));
        BtnDisconnect.IsEnabled = ok;
    }

    private void BtnGenScript_Click(object sender, RoutedEventArgs e)
    {
        TxtSelfHostScript.Text = SelfHostScriptGenerator.GenerateUbuntuWireGuardScript(TxtVpsIp.Text);
    }

    private void BtnCopyScript_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(TxtSelfHostScript.Text);
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        _isArabic = !_isArabic;
        FlowDirection = _isArabic ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        BtnLang.Content = _isArabic ? "English (LTR)" : "العربية (RTL)";
        TxtTitle.Text = _isArabic ? "UnityHybridVPN — عميل الشبكة الخاصة الهجين" : "UnityHybridVPN — Hybrid VPN Client";
        TabProfiles.Header = _isArabic ? "ملفاتي الخاصة (WireGuard / OpenVPN)" : "My Profiles (WireGuard / OpenVPN)";
        TabVpnGate.Header = _isArabic ? "خوادم مجانية متعددة الدول (VPNGate)" : "Free Multi-Country (VPNGate)";
        TabSelfHost.Header = _isArabic ? "إنشاء سيرفرك الشخصي (Oracle Free / VPS)" : "Self-Host Helper (Oracle / VPS)";
        BtnDisconnect.Content = _isArabic ? "قطع الاتصال" : "Disconnect";
    }

    private void RefreshProfilesUi()
    {
        _profileRows.Clear();
        foreach (var p in _profiles) _profileRows.Add(p);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceExit)
        {
            var res = System.Windows.MessageBox.Show(
                this,
                _isArabic
                    ? "تصغير إلى شريط المهام أم إغلاق نهائي؟\nنعم = تصغير | لا = إغلاق نهائي | إلغاء = البقاء"
                    : "Minimize to Tray or Exit?\nYes = Tray | No = Exit | Cancel = Stay",
                "UnityHybridVPN",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes) { e.Cancel = true; Hide(); return; }
            if (res == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            _forceExit = true;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.Dispose();
        _nativeVpn.Dispose();
        _sshTunnel.Dispose();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
