using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Catan.WpfLauncher.Models;

namespace Catan.WpfLauncher;

public partial class SettingsWindow : Window
{
    private static readonly string[] RadminExeNames = { "RvRvpnGui.exe", "Radmin VPN.exe" };

    public LauncherConfig Config { get; }

    public SettingsWindow(LauncherConfig config)
    {
        InitializeComponent();
        Config = new LauncherConfig
        {
            ConfigFilePath = config.ConfigFilePath,
            CatanExePath = config.CatanExePath,
            DgVoodooExePath = config.DgVoodooExePath,
            RadminExePath = config.RadminExePath,
            MusicEnabled = config.MusicEnabled,
            MusicVolume = config.MusicVolume
        };

        CatanPathTextBox.Text = Config.CatanExePath;
        DgVoodooPathTextBox.Text = Config.DgVoodooExePath;
        RadminPathTextBox.Text = Config.RadminExePath;
        MusicEnabledCheckBox.IsChecked = Config.MusicEnabled;
        MusicVolumeSlider.Value = Config.MusicVolume;
        UpdateVolumeText();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        DragMove();
    }

    private void BrowseCatanButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForExe(CatanPathTextBox, "Catan.exe auswaehlen", "Catan.exe");
    }

    private void BrowseDgVoodooButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForExe(DgVoodooPathTextBox, "dgVoodooCpl.exe auswaehlen", "dgVoodooCpl.exe");
    }

    private void BrowseRadminButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForExe(RadminPathTextBox, "Radmin VPN auswaehlen", RadminExeNames);
    }

    private void MusicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateVolumeText();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string catanExe = CatanPathTextBox.Text.Trim();
        string dgExe = DgVoodooPathTextBox.Text.Trim();
        string radminExe = RadminPathTextBox.Text.Trim();

        if (!ValidateRequiredExe(catanExe, "Catan.exe", "Bitte eine gueltige Catan.exe auswaehlen."))
            return;

        if (!ValidateRequiredExe(dgExe, "dgVoodooCpl.exe", "Bitte eine gueltige dgVoodooCpl.exe auswaehlen."))
            return;

        if (!string.IsNullOrWhiteSpace(radminExe) && !ValidateOptionalRadmin(radminExe))
            return;

        Config.CatanExePath = catanExe;
        Config.DgVoodooExePath = dgExe;
        Config.RadminExePath = radminExe;
        Config.MusicEnabled = MusicEnabledCheckBox.IsChecked == true;
        Config.MusicVolume = (int)Math.Round(MusicVolumeSlider.Value);

        DialogResult = true;
        Close();
    }

    private void BrowseForExe(System.Windows.Controls.TextBox target, string title, params string[] expectedNames)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "EXE-Dateien (*.exe)|*.exe|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            try
            {
                if (File.Exists(target.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
            }
            catch
            {
            }
        }

        if (dialog.ShowDialog(this) == true)
            target.Text = dialog.FileName;
    }

    private bool ValidateRequiredExe(string path, string expectedFileName, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            LauncherMessageDialog.ShowWarning(this, "Fehler", errorMessage);
            return false;
        }

        if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            LauncherMessageDialog.ShowWarning(this, "Fehler", "Bitte wirklich die Datei " + expectedFileName + " auswaehlen.");
            return false;
        }

        return true;
    }

    private bool ValidateOptionalRadmin(string path)
    {
        if (!File.Exists(path))
        {
            LauncherMessageDialog.ShowWarning(this, "Fehler", "Die angegebene Radmin-EXE wurde nicht gefunden.");
            return false;
        }

        string fileName = Path.GetFileName(path);
        if (!RadminExeNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            LauncherMessageDialog.ShowWarning(this, "Fehler", "Bitte wirklich RvRvpnGui.exe oder Radmin VPN.exe auswaehlen.");
            return false;
        }

        return true;
    }

    private void UpdateVolumeText()
    {
        if (MusicVolumeText != null)
            MusicVolumeText.Text = ((int)Math.Round(MusicVolumeSlider.Value)) + " %";
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase || source is TextBox || source is Slider || source is CheckBox)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
