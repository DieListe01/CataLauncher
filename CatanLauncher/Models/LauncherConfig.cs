namespace CatanLauncher.Models;

public sealed class LauncherConfig
{
    public string ConfigFilePath { get; set; } = string.Empty;
    public string CatanExePath { get; set; } = string.Empty;
    public string DgVoodooExePath { get; set; } = string.Empty;
    public string RadminExePath { get; set; } = string.Empty;
    public bool MusicEnabled { get; set; } = true;
    public int MusicVolume { get; set; } = 65;
    public bool UpdateChecksEnabled { get; set; } = true;
    public string GitHubOwner { get; set; } = "DieListe01";
    public string GitHubRepository { get; set; } = "CataLauncher";
    public string GitHubAssetName { get; set; } = string.Empty;
}
