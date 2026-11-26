using System.IO;
using System.Windows;
using UnityHybridVPN.Core.Services;

namespace UnityHybridVPN.UI;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityHybridVPN");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {args.Exception}\n\n");
            }
            catch { }
            System.Windows.MessageBox.Show($"Unexpected error:\n{args.Exception.Message}", "UnityHybridVPN Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { KillSwitchService.Disable(); } catch { }
        try { SshTunnelService.DisableSystemProxy(); } catch { }
        base.OnExit(e);
    }
}
