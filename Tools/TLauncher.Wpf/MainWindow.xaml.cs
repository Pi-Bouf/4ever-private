using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TLauncher.Wpf;

public partial class MainWindow : Window
{
    private LauncherSettings _settings;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        // The launcher normally sits in the game folder next to config.ini + TClient.exe.
        string dir = File.Exists(Path.Combine(AppContext.BaseDirectory, "config.ini"))
            ? AppContext.BaseDirectory
            : Directory.GetCurrentDirectory();
        _settings = LauncherConfig.Resolve(gameDir: dir);
        Status($"Patch server {_settings.Server}:{_settings.Port}");

        // Embedded news browser — WebView2 (Chromium). A black default background paints blank / loading /
        // error states black (no white), and --hide-scrollbars removes the scrollbars.
        NewsBrowser.DefaultBackgroundColor = System.Drawing.Color.Black;
        NewsBrowser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TLauncher.Wpf", "WebView2"),
            AdditionalBrowserArguments = "--hide-scrollbars",
        };
        Loaded += async (_, _) =>
        {
            try
            {
                await NewsBrowser.EnsureCoreWebView2Async();
                // If the news URL is unreachable, Chromium shows its own (light) error page — replace it
                // with a black page so the panel stays black.
                NewsBrowser.CoreWebView2.NavigationCompleted += (_, ev) =>
                {
                    if (!ev.IsSuccess)
                        try { NewsBrowser.CoreWebView2.NavigateToString("<html><body style='margin:0;background:#000'></body></html>"); } catch { }
                };
                NewsBrowser.CoreWebView2.Navigate(_settings.NewsUrl);
            }
            catch { /* no WebView2 runtime / offline → the control stays black */ }
        };
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    private void Home_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(_settings.HomeUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log($"Homepage failed: {ex.Message}"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings.ConfigPath) { Owner = this };
        win.ShowDialog();
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        SetBars(0, 0);

        try
        {
            Status($"Connecting to {_settings.Server}:{_settings.Port} …");
            var m = await PatchClient.RequestAsync(_settings.Server, _settings.Port, _settings.Version, CancellationToken.None);
            Status($"Login {m.LoginIp}:{m.LoginPort} · {m.Files.Count} file(s)");

            if (m.Files.Count > 0)
            {
                var log = new Progress<string>(Log);
                var step = new Progress<Patcher.DownloadStep>(OnStep);
                await Patcher.DownloadAsync(m, _settings.GameDir, log, step, CancellationToken.None);
            }
            SetBars(100, 100);

            string exePath = Path.IsPathRooted(_settings.Exe) ? _settings.Exe : Path.Combine(_settings.GameDir, _settings.Exe);
            Status("Launching client …");
            var r = Patcher.Launch(exePath, _settings.GameDir, m.LoginIp, m.LoginPort);
            Log(r.Message);

            if (r.Status is LaunchStatus.Started or LaunchStatus.StartedElevated)
            {
                Status("Client launched.");
                await Task.Delay(600);
                Close();
                return;
            }
            Status($"Launch: {r.Status}.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            Status("Failed.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnStep(Patcher.DownloadStep s)
    {
        double filePct = s.BytesTotal is > 0 ? (double)s.BytesReceived / s.BytesTotal.Value * 100 : 0;
        double totalPct = (s.Count == 0) ? 100 : ((s.Done - 1) + filePct / 100.0) / s.Count * 100;
        SetBars(filePct, totalPct);
        Status($"{s.Name}  ({s.Done}/{s.Count})");
    }

    private void SetBars(double current, double total)
    {
        CurrentBar.Value = Math.Clamp(current, 0, 100);
        TotalBar.Value = Math.Clamp(total, 0, 100);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PlayBtn.IsEnabled = !busy;
        SettingsBtn.IsEnabled = !busy;
    }

    private void Status(string s) => StatusText.Text = s;

    // Status/progress messages surface on the single status line above the bars.
    private void Log(string line) => StatusText.Text = line;
}
