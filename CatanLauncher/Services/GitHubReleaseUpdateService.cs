using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CatanLauncher.Models;

namespace CatanLauncher.Services;

public sealed class GitHubReleaseUpdateService
{
    public async Task CheckForUpdateAsync(Window owner, LauncherConfig config, bool isManualCheck = false)
    {
        if (!config.UpdateChecksEnabled)
        {
            if (isManualCheck)
                MessageBox.Show(owner, "Update-Pruefung ist in den Einstellungen deaktiviert.", "Launcher-Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.GitHubOwner) || string.IsNullOrWhiteSpace(config.GitHubRepository))
            return;

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");

            ReleaseInfo? release = await GetReleaseAsync(http, config);
            if (release == null || !TryParseVersion(release.TagName, out Version? latestVersion))
            {
                if (isManualCheck)
                    MessageBox.Show(owner, "Online-Version konnte nicht ermittelt werden.", "Launcher-Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                LocalTelemetryService.Write("update", "could-not-resolve-latest-release", config.LocalTelemetryEnabled);
                return;
            }

            Version currentVersion = AppVersionService.GetCurrentVersion();
            if (latestVersion <= currentVersion)
            {
                if (isManualCheck)
                    MessageBox.Show(owner, "Du bist bereits auf der neuesten Version (" + currentVersion + ").", "Launcher-Update", MessageBoxButton.OK, MessageBoxImage.Information);
                LocalTelemetryService.Write("update", "up-to-date " + currentVersion, config.LocalTelemetryEnabled);
                return;
            }

            string message =
                "Neue Version verfuegbar: " + latestVersion + Environment.NewLine +
                "Installierte Version: " + currentVersion + Environment.NewLine +
                "Kanal: " + NormalizeChannel(config.UpdateChannel) + Environment.NewLine + Environment.NewLine +
                BuildReleaseNotesPreview(release.Body) + Environment.NewLine + Environment.NewLine +
                "Jetzt Update herunterladen?";

            MessageBoxResult result = MessageBox.Show(owner, message, "Launcher-Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
                return;

            Window? progressWindow = null;
            ProgressBar? progressBar = null;
            TextBlock? progressText = null;
            try
            {
                progressWindow = CreateProgressWindow(owner, out progressBar, out progressText);
                progressWindow.Show();

                string? downloadUrl = ResolveDownloadUrl(release, config.GitHubAssetName);
                if (!string.IsNullOrWhiteSpace(downloadUrl))
                {
                    IProgress<DownloadProgressInfo> progress = new Progress<DownloadProgressInfo>(info => UpdateProgressUi(progressBar, progressText, info));
                    progress.Report(DownloadProgressInfo.Status("Pruefe Release-Asset..."));

                    await DownloadAndStartInstallerAsync(http, downloadUrl, progress);
                    LocalTelemetryService.Write("update", "download-started " + latestVersion, config.LocalTelemetryEnabled);
                    Application.Current.Shutdown();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
                    OpenUrl(release.HtmlUrl);
            }
            finally
            {
                progressWindow?.Close();
            }
        }
        catch (Exception ex)
        {
            LocalTelemetryService.Write("update-error", ex.Message, config.LocalTelemetryEnabled);
            if (isManualCheck)
                MessageBox.Show(owner, "Update konnte nicht geladen werden: " + ex.Message, "Launcher-Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task<ReleaseInfo?> GetReleaseAsync(HttpClient http, LauncherConfig config)
    {
        string owner = config.GitHubOwner.Trim();
        string repo = config.GitHubRepository.Trim();
        string channel = NormalizeChannel(config.UpdateChannel);

        if (channel == "stable")
        {
            string latestUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            string json = await http.GetStringAsync(latestUrl);
            return ParseReleaseInfo(JsonDocument.Parse(json).RootElement);
        }

        string releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
        string releasesJson = await http.GetStringAsync(releasesUrl);
        using JsonDocument releasesDocument = JsonDocument.Parse(releasesJson);

        foreach (JsonElement release in releasesDocument.RootElement.EnumerateArray())
        {
            bool isDraft = release.TryGetProperty("draft", out JsonElement draftElement) && draftElement.GetBoolean();
            if (isDraft)
                continue;

            // Beta-Kanal: erste nicht-draft Release (inkl. prerelease) nehmen.
            return ParseReleaseInfo(release);
        }

        return null;
    }

    private static ReleaseInfo ParseReleaseInfo(JsonElement release)
    {
        string tagName = release.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        string htmlUrl = release.TryGetProperty("html_url", out JsonElement htmlElement) ? htmlElement.GetString() ?? string.Empty : string.Empty;
        string body = release.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;

        var assets = new List<ReleaseAsset>();
        if (release.TryGetProperty("assets", out JsonElement assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                string url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    assets.Add(new ReleaseAsset(name, url));
            }
        }

        return new ReleaseInfo(tagName, htmlUrl, body, assets);
    }

    private static string NormalizeChannel(string? channel)
    {
        return string.Equals(channel?.Trim(), "beta", StringComparison.OrdinalIgnoreCase) ? "beta" : "stable";
    }

    private static string BuildReleaseNotesPreview(string releaseBody)
    {
        if (string.IsNullOrWhiteSpace(releaseBody))
            return "Was ist neu: keine Release-Notizen vorhanden.";

        string[] lines = releaseBody
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(4)
            .ToArray();

        if (lines.Length == 0)
            return "Was ist neu: keine Release-Notizen vorhanden.";

        return "Was ist neu:" + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(line => "- " + line));
    }

    private static string? ResolveDownloadUrl(ReleaseInfo release, string configuredAssetName)
    {
        string desiredName = (configuredAssetName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(desiredName))
        {
            ReleaseAsset? exact = release.Assets.FirstOrDefault(asset => asset.Name.Equals(desiredName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Url;
        }

        ReleaseAsset? setup = release.Assets
            .FirstOrDefault(asset => asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                                     asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (setup != null)
            return setup.Url;

        ReleaseAsset? exe = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (exe != null)
            return exe.Url;

        ReleaseAsset? msi = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
        return msi?.Url;
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

    private static async Task DownloadAndStartInstallerAsync(HttpClient http, string downloadUrl, IProgress<DownloadProgressInfo>? progress)
    {
        string fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "CatanLauncher-Update.exe";

        string targetPath = Path.Combine(Path.GetTempPath(), fileName);

        progress?.Report(DownloadProgressInfo.Status("Verbinde mit Download-Server..."));
        using HttpResponseMessage response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;
        progress?.Report(DownloadProgressInfo.Status("Download gestartet..."));

        await using (Stream source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(targetPath))
        {
            byte[] buffer = new byte[1024 * 32];
            long bytesReceived = 0;
            progress?.Report(DownloadProgressInfo.Bytes(bytesReceived, totalBytes));

            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read <= 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, read));
                bytesReceived += read;
                progress?.Report(DownloadProgressInfo.Bytes(bytesReceived, totalBytes));
            }
        }

        progress?.Report(DownloadProgressInfo.Status("Download abgeschlossen. Starte Installer..."));
        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true
        });
    }

    private static Window CreateProgressWindow(Window owner, out ProgressBar progressBar, out TextBlock messageText)
    {
        progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Minimum = 0,
            Maximum = 100,
            Height = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };

        messageText = new TextBlock
        {
            Text = "Update wird heruntergeladen. Bitte kurz warten...",
            TextWrapping = TextWrapping.Wrap
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(16)
        };
        panel.Children.Add(progressBar);
        panel.Children.Add(messageText);

        return new Window
        {
            Title = "Launcher-Update",
            Content = panel,
            Width = 380,
            Height = 140,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = owner
        };
    }

    private static void UpdateProgressUi(ProgressBar? progressBar, TextBlock? progressText, DownloadProgressInfo progress)
    {
        if (progressBar == null || progressText == null)
            return;

        if (!progress.HasByteProgress)
        {
            progressBar.IsIndeterminate = true;
            progressText.Text = progress.StatusText;
            return;
        }

        if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
        {
            double percent = (double)progress.BytesReceived / progress.TotalBytes.Value * 100.0;
            progressBar.IsIndeterminate = false;
            progressBar.Value = Math.Clamp(percent, 0, 100);
            progressText.Text = progress.StatusText + ": " +
                                progressBar.Value.ToString("0") + " % (" +
                                FormatBytes(progress.BytesReceived) + " / " +
                                FormatBytes(progress.TotalBytes.Value) + ")";
            return;
        }

        progressBar.IsIndeterminate = true;
        progressText.Text = progress.StatusText + ": " + FormatBytes(progress.BytesReceived);
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
            return (bytes / gb).ToString("0.00") + " GB";
        if (bytes >= mb)
            return (bytes / mb).ToString("0.00") + " MB";
        if (bytes >= kb)
            return (bytes / kb).ToString("0.0") + " KB";
        return bytes + " B";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private sealed record ReleaseInfo(string TagName, string HtmlUrl, string Body, IReadOnlyList<ReleaseAsset> Assets);
    private sealed record ReleaseAsset(string Name, string Url);

    private readonly record struct DownloadProgressInfo(string StatusText, long BytesReceived, long? TotalBytes, bool HasByteProgress)
    {
        public static DownloadProgressInfo Status(string statusText) => new(statusText, 0, null, false);
        public static DownloadProgressInfo Bytes(long bytesReceived, long? totalBytes) => new("Update wird heruntergeladen", bytesReceived, totalBytes, true);
    }
}
