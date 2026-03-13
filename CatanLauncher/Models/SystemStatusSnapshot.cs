namespace CatanLauncher.Models;

public sealed class SystemStatusSnapshot
{
    public bool FirewallRulesExist { get; set; }
    public bool WindowsUpdateCheckSucceeded { get; set; }
    public int WindowsUpdatePendingCount { get; set; }
    public string WindowsUpdateLastCheckedText { get; set; } = string.Empty;
    public string DgVoodooInstalledVersion { get; set; } = "unbekannt";
    public string DgVoodooLatestVersion { get; set; } = "online nicht pruefbar";
    public bool DgVoodooUpdateAvailable { get; set; }
    public string RadminInstalledVersion { get; set; } = "unbekannt";
    public string RadminLatestVersion { get; set; } = "online nicht pruefbar";
    public bool RadminUpdateAvailable { get; set; }
    public bool HasNvidiaAdapter { get; set; }
    public string NvidiaGpuName { get; set; } = string.Empty;
    public string NvidiaInstalledDriverVersion { get; set; } = "kein NVIDIA-Adapter";
    public string NvidiaLatestDriverVersion { get; set; } = "online nicht pruefbar";
    public string NvidiaLatestDriverName { get; set; } = string.Empty;
    public string NvidiaLatestDriverReleaseDate { get; set; } = string.Empty;
    public string NvidiaDriverPageUrl { get; set; } = string.Empty;
    public bool NvidiaUpdateAvailable { get; set; }
}
