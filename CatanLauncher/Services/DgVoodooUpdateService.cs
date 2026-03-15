using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace CatanLauncher.Services;

public sealed class DgVoodooUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/dege-diosg/dgVoodoo2/releases/latest";
    private const string FallbackPageUrl = "https://dege.freeweb.hu/dgVoodoo2/dgVoodoo2/#";
    public const string InstalledVersionMarkerFileName = ".catanlauncher-dgvoodoo-version.txt";

    public async Task<DgVoodooUpdateResult> InstallLatestAsync(string dgVoodooExePath, IProgress<string>? status = null)
    {
        if (string.IsNullOrWhiteSpace(dgVoodooExePath))
            throw new InvalidOperationException("Kein dgVoodoo-Pfad konfiguriert.");

        if (!File.Exists(dgVoodooExePath))
            throw new FileNotFoundException("Die konfigurierte dgVoodooCpl.exe wurde nicht gefunden.", dgVoodooExePath);

        string targetDirectory = Path.GetDirectoryName(dgVoodooExePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            throw new DirectoryNotFoundException("Der Zielordner fuer dgVoodoo2 existiert nicht.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");

        status?.Report("Lese aktuelle dgVoodoo-Release-Info...");
        string releaseJson = await http.GetStringAsync(LatestReleaseApiUrl);
        using JsonDocument document = JsonDocument.Parse(releaseJson);
        JsonElement root = document.RootElement;

        string releaseVersion = root.TryGetProperty("tag_name", out JsonElement tagElement)
            ? (tagElement.GetString() ?? "neueste")
            : "neueste";

        string? zipUrl = FindZipAssetUrl(root);
        if (string.IsNullOrWhiteSpace(zipUrl))
            throw new InvalidOperationException("Kein passendes dgVoodoo2-ZIP im Latest Release gefunden. Bitte manuell laden: " + FallbackPageUrl);

        string tempRoot = Path.Combine(Path.GetTempPath(), "CatanLauncher", "dgVoodooUpdate", Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(tempRoot, "dgVoodoo_latest.zip");
        string extractPath = Path.Combine(tempRoot, "extracted");
        string backupPath = targetDirectory.TrimEnd('\\') + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        try
        {
            status?.Report("Lade dgVoodoo-ZIP herunter...");
            await using (Stream zipStream = await http.GetStreamAsync(zipUrl))
            await using (var zipFile = File.Create(zipPath))
            {
                await zipStream.CopyToAsync(zipFile);
            }

            status?.Report("Erstelle Backup...");
            CopyDirectoryRecursive(targetDirectory, backupPath);

            status?.Report("Entpacke Update...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            status?.Report("Kopiere neue Dateien...");
            string sourceDirectory = ResolveExtractedRoot(extractPath);
            CopyDirectoryRecursive(sourceDirectory, targetDirectory);

            status?.Report("Speichere lokale Versionsmarkierung...");
            WriteInstalledVersionMarker(targetDirectory, releaseVersion);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Temp-Reste sind unkritisch.
            }
        }

        return new DgVoodooUpdateResult(releaseVersion.TrimStart('v', 'V'), backupPath);
    }

    public void Rollback(string dgVoodooExePath, string backupPath)
    {
        if (string.IsNullOrWhiteSpace(dgVoodooExePath))
            throw new InvalidOperationException("Kein dgVoodoo-Pfad konfiguriert.");

        string targetDirectory = Path.GetDirectoryName(dgVoodooExePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("Zielordner fuer dgVoodoo2 konnte nicht ermittelt werden.");

        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
            throw new DirectoryNotFoundException("Backup-Ordner wurde nicht gefunden.");

        CopyDirectoryRecursive(backupPath, targetDirectory);
    }

    private static void WriteInstalledVersionMarker(string targetDirectory, string version)
    {
        try
        {
            string normalized = version.TrimStart('v', 'V').Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string markerPath = Path.Combine(targetDirectory, InstalledVersionMarkerFileName);
            File.WriteAllText(markerPath, normalized);
        }
        catch
        {
            // Marker ist optional und darf den Update-Prozess nicht brechen.
        }
    }

    private static string? FindZipAssetUrl(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out JsonElement assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            return null;

        string? fallbackZipUrl = null;

        foreach (JsonElement asset in assetsElement.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameElement)
                ? (nameElement.GetString() ?? string.Empty)
                : string.Empty;

            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(url))
                continue;

            bool looksStable = name.StartsWith("dgVoodoo2_", StringComparison.OrdinalIgnoreCase) &&
                               !name.Contains("_dbg", StringComparison.OrdinalIgnoreCase) &&
                               !name.Contains("_dev", StringComparison.OrdinalIgnoreCase);

            if (looksStable)
                return url;

            fallbackZipUrl ??= url;
        }

        return fallbackZipUrl;
    }

    private static string ResolveExtractedRoot(string extractPath)
    {
        string directExe = Path.Combine(extractPath, "dgVoodooCpl.exe");
        if (File.Exists(directExe))
            return extractPath;

        string? nested = Directory
            .GetFiles(extractPath, "dgVoodooCpl.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        if (!string.IsNullOrWhiteSpace(nested))
            return nested;

        throw new InvalidOperationException("Die entpackte dgVoodoo2-Struktur ist unerwartet (dgVoodooCpl.exe nicht gefunden).");
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string sourceSubDirectory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, sourceSubDirectory);
            string targetSubDirectory = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(targetSubDirectory);
        }

        foreach (string sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            string targetFilePath = Path.Combine(targetDirectory, relative);
            string? targetFileDirectory = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
                Directory.CreateDirectory(targetFileDirectory);

            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
        }
    }
}

public sealed record DgVoodooUpdateResult(string InstalledVersion, string BackupPath);
