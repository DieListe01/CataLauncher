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

            Version currentVersion = AppVersionService.GetCurrentVersion();
            if (!TryParseVersion(latestTag, out Version? latestVersion) || latestVersion <= currentVersion)
                return;

            string message =
                "Neue Version verfuegbar: " + latestVersion + Environment.NewLine +
                "Installierte Version: " + currentVersion + Environment.NewLine + Environment.NewLine +
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

                string? downloadUrl = TryGetAssetDownloadUrl(root, config.GitHubAssetName);
                if (!string.IsNullOrWhiteSpace(downloadUrl))
                {
                    var progress = new Progress<DownloadProgressInfo>(info =>
                    {
                        UpdateProgressUi(progressBar, progressText, info);
                    });

                    await DownloadAndStartInstallerAsync(http, downloadUrl, progress);
                    Application.Current.Shutdown();
                    return;
                }

                string htmlUrl = root.TryGetProperty("html_url", out JsonElement htmlElement)
                    ? htmlElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(htmlUrl))
                    OpenUrl(htmlUrl);
            }
            finally
            {
                progressWindow?.Close();
            }
        }
        catch
        {
            // Update-Check darf den Launcher-Start niemals blockieren.
        }
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

    private static async Task DownloadAndStartInstallerAsync(HttpClient http, string downloadUrl, IProgress<DownloadProgressInfo>? progress)
    {
        string fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "CatanLauncher-Update.exe";

        string targetPath = Path.Combine(Path.GetTempPath(), fileName);

        using HttpResponseMessage response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;

        await using (Stream source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(targetPath))
        {
            byte[] buffer = new byte[1024 * 32];
            long bytesReceived = 0;

            progress?.Report(new DownloadProgressInfo(bytesReceived, totalBytes));

            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read <= 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, read));
                bytesReceived += read;
                progress?.Report(new DownloadProgressInfo(bytesReceived, totalBytes));
            }
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
            Width = 360,
            Height = 130,
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

        if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
        {
            double percent = (double)progress.BytesReceived / progress.TotalBytes.Value * 100.0;
            progressBar.IsIndeterminate = false;
            progressBar.Value = Math.Clamp(percent, 0, 100);
            progressText.Text = "Update wird heruntergeladen: " +
                                progressBar.Value.ToString("0") + " % (" +
                                FormatBytes(progress.BytesReceived) + " / " +
                                FormatBytes(progress.TotalBytes.Value) + ")";
            return;
        }

        progressBar.IsIndeterminate = true;
        progressText.Text = "Update wird heruntergeladen: " + FormatBytes(progress.BytesReceived);
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

    private readonly record struct DownloadProgressInfo(long BytesReceived, long? TotalBytes);
}
