namespace Catan.WpfLauncher.Models;

public sealed class LauncherConfig
{
    public string ConfigFilePath { get; set; } = string.Empty;
    public string CatanExePath { get; set; } = string.Empty;
    public string DgVoodooExePath { get; set; } = string.Empty;
    public string RadminExePath { get; set; } = string.Empty;
    public bool MusicEnabled { get; set; } = true;
    public int MusicVolume { get; set; } = 65;
}
