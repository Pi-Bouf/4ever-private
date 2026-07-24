using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace TLauncher.Wpf;

/// <summary>
/// Options window — a functional editor for the shared <c>config.ini</c> <c>[Settings]</c> block (the same
/// keys the client + C++ GameSetting/PlaySetting use). Reads/writes via the Win32 private-profile API, so
/// the format matches exactly how the game loads it. (Skinned like the launcher palette; the C++ tabbed
/// bitmap skin is not reproduced 1:1 — the values are.)
/// </summary>
public partial class SettingsWindow : Window
{
    private const string Section = "Settings";
    private readonly string _path;

    private static readonly (int W, int H)[] Resolutions =
    {
        (1024, 768), (1280, 720), (1280, 1024), (1366, 768), (1600, 900), (1920, 1080),
    };

    public SettingsWindow(string configPath)
    {
        InitializeComponent();
        _path = configPath;

        foreach (var d in new[] { "Low", "Medium", "High" })
        {
            TexCombo.Items.Add(d); MapCombo.Items.Add(d); ObjCombo.Items.Add(d);
        }
        MasterSlider.ValueChanged += (_, _) => MasterVal.Text = ((int)MasterSlider.Value).ToString();
        BgmSlider.ValueChanged += (_, _) => BgmVal.Text = ((int)BgmSlider.Value).ToString();
        SfxSlider.ValueChanged += (_, _) => SfxVal.Text = ((int)SfxSlider.Value).ToString();

        Load();
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    private void Load()
    {
        int sx = GetInt("ScreenX", 1024), sy = GetInt("ScreenY", 768);
        foreach (var (w, h) in Resolutions) ResoCombo.Items.Add($"{w} x {h}");
        string cur = $"{sx} x {sy}";
        if (!ResoCombo.Items.Contains(cur)) ResoCombo.Items.Insert(0, cur);
        ResoCombo.SelectedItem = cur;

        WindowedChk.IsChecked = GetBool("WindowedMode", false);
        ShaderChk.IsChecked = GetBool("UseShader", true);

        TexCombo.SelectedIndex = Math.Clamp(GetInt("TextureDetail", 1), 0, 2);
        MapCombo.SelectedIndex = Math.Clamp(GetInt("MapDETAIL", 1), 0, 2);
        ObjCombo.SelectedIndex = Math.Clamp(GetInt("ObjDETAIL", 1), 0, 2);
        MapShadowChk.IsChecked = GetInt("MapSHADOW", 1) != 0;
        ObjShadowChk.IsChecked = GetInt("ObjSHADOW", 1) != 0;

        MasterChk.IsChecked = GetInt("MASTER", 1) != 0;
        BgmChk.IsChecked = GetInt("BGM", 1) != 0;
        SfxChk.IsChecked = GetInt("SOUND", 1) != 0;
        MasterSlider.Value = Math.Clamp(GetInt("MainVolume", 100), 0, 100);
        BgmSlider.Value = Math.Clamp(GetInt("BGMVolume", 100), 0, 100);
        SfxSlider.Value = Math.Clamp(GetInt("SFXVolume", 100), 0, 100);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ResoCombo.SelectedItem is string reso)
        {
            var parts = reso.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                Set("ScreenX", w.ToString());
                Set("ScreenY", h.ToString());
            }
        }
        Set("WindowedMode", (WindowedChk.IsChecked == true) ? "TRUE" : "FALSE");
        Set("UseShader", (ShaderChk.IsChecked == true) ? "TRUE" : "FALSE");

        Set("TextureDetail", TexCombo.SelectedIndex.ToString());
        Set("MapDETAIL", MapCombo.SelectedIndex.ToString());
        Set("ObjDETAIL", ObjCombo.SelectedIndex.ToString());
        Set("MapSHADOW", (MapShadowChk.IsChecked == true) ? "1" : "0");
        Set("ObjSHADOW", (ObjShadowChk.IsChecked == true) ? "1" : "0");

        Set("MASTER", (MasterChk.IsChecked == true) ? "1" : "0");
        Set("BGM", (BgmChk.IsChecked == true) ? "1" : "0");
        Set("SOUND", (SfxChk.IsChecked == true) ? "1" : "0");
        Set("MainVolume", ((int)MasterSlider.Value).ToString());
        Set("BGMVolume", ((int)BgmSlider.Value).ToString());
        Set("SFXVolume", ((int)SfxSlider.Value).ToString());

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ---- config.ini access (Win32 private-profile, matching the client) ----

    private string GetStr(string key, string def)
    {
        var sb = new StringBuilder(512);
        GetPrivateProfileString(Section, key, def, sb, (uint)sb.Capacity, _path);
        return sb.ToString();
    }

    private int GetInt(string key, int def) => int.TryParse(GetStr(key, def.ToString()), out var v) ? v : def;

    private bool GetBool(string key, bool def)
    {
        string s = GetStr(key, def ? "TRUE" : "FALSE").Trim();
        return s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1";
    }

    private void Set(string key, string value) => WritePrivateProfileString(Section, key, value, _path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetPrivateProfileStringA")]
    private static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder ret, uint size, string file);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "WritePrivateProfileStringA")]
    private static extern bool WritePrivateProfileString(string section, string key, string value, string file);
}
