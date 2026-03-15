using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using CatanLauncher.Models;

namespace CatanLauncher.Services;

public sealed class GitHubReleaseUpdateService
{
    public async Task CheckForUpdateAsync(Window owner, LauncherConfig config)
    {
        if (!config.UpdateChecksEnabled)
            return;

        if (string.IsNullOrWhiteSpace(config.GitHubOwner) || string.IsNullOrWhiteSpace(config.GitHubRepository))
            return;

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");

            string url = $"https://api.github.com/repos/{config.GitHubOwner}/{config.GitHubRepository}/releases/latest";
            string json = await http.GetStringAsync(url);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;

            Version currentVersion = GetCurrentVersion();
            if (!TryParseVersion(latestTag, out Version? latestVersion) || latestVersion <= currentVersion)
                return;

            string message =
                "Neue Version verfuegbar: " + latestVersion + Environment.NewLine +
                "Installierte Version: " + currentVersion + Environment.NewLine + Environment.NewLine +
                "Jetzt Update herunterladen?";

            MessageBoxResult result = MessageBox.Show(owner, message, "Launcher-Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
                return;

            string? downloadUrl = TryGetAssetDownloadUrl(root, config.GitHubAssetName);
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                await DownloadAndStartInstallerAsync(http, downloadUrl);
                Application.Current.Shutdown();
                return;
            }

            string htmlUrl = root.TryGetProperty("html_url", out JsonElement htmlElement)
                ? htmlElement.GetString() ?? string.Empty
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(htmlUrl))
                OpenUrl(htmlUrl);
        }
        catch
        {
            // Update-Check darf den Launcher-Start niemals blockieren.
        }
    }

    private static Version GetCurrentVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (TryParseVersion(informationalVersion ?? string.Empty, out Version? parsedInformationalVersion))
            return parsedInformationalVersion ?? new Version(0, 0, 0, 0);

        Version? assemblyVersion = assembly.GetName().Version;
        return assemblyVersion ?? new Version(0, 0, 0, 0);
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().TrimStart('v', 'V');

        int separatorIndex = normalized.IndexOf('-');
        if (separatorIndex >= 0)
            normalized = normalized[..separatorIndex];

        int metadataSeparatorIndex = normalized.IndexOf('+');
        if (metadataSeparatorIndex >= 0)
            normalized = normalized[..metadataSeparatorIndex];

        return Version.TryParse(normalized, out version);
    }

    private static string? TryGetAssetDownloadUrl(JsonElement releaseRoot, string configuredAssetName)
    {
        if (!releaseRoot.TryGetProperty("assets", out JsonElement assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            return null;

        string desiredName = (configuredAssetName ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(desiredName))
        {
            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!name.Equals(desiredName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return asset.TryGetProperty("browser_download_url", out JsonElement exactUrl)
                    ? exactUrl.GetString()
                    : null;
            }
        }

        foreach (JsonElement asset in assetsElement.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return asset.TryGetProperty("browser_download_url", out JsonElement url)
                ? url.GetString()
                : null;
        }

        return null;
    }

    private static async Task DownloadAndStartInstallerAsync(HttpClient http, string downloadUrl)
    {
        string fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "CatanLauncher-Update.exe";

        string targetPath = Path.Combine(Path.GetTempPath(), fileName);

        await using (Stream source = await http.GetStreamAsync(downloadUrl))
        await using (var target = File.Create(targetPath))
        {
            await source.CopyToAsync(target);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true
        });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
