using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using CatanLauncher.Models;

namespace CatanLauncher.Services;

public sealed class SystemStatusService
{
    private const string NvidiaDisplayAdapterClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string NvidiaLookupApiBaseUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx";
    private const string NvidiaDriverSearchUrl = "https://www.nvidia.com/Download/processFind.aspx";
    private readonly Dictionary<int, IReadOnlyList<NvidiaLookupValue>> nvidiaProductsBySeriesId = new();
    private readonly Dictionary<string, NvidiaProductLookup> nvidiaProductLookupCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<NvidiaLookupValue>? nvidiaSeriesCache;

    public async Task<SystemStatusSnapshot> GetStatusAsync(LauncherConfig config)
    {
        var snapshot = new SystemStatusSnapshot
        {
            FirewallRulesExist = CheckFirewallRulesExist(),
            DgVoodooInstalledVersion = GetInstalledVersion(config.DgVoodooExePath, "nicht installiert"),
            RadminInstalledVersion = GetInstalledVersion(config.RadminExePath, string.IsNullOrWhiteSpace(config.RadminExePath) ? "nicht installiert" : "unbekannt")
        };

        NvidiaInstalledAdapter? nvidiaAdapter = GetInstalledNvidiaAdapter();
        if (nvidiaAdapter != null)
        {
            snapshot.HasNvidiaAdapter = true;
            snapshot.NvidiaGpuName = nvidiaAdapter.Name;
            snapshot.NvidiaInstalledDriverVersion = NormalizeNvidiaDriverVersion(nvidiaAdapter.DriverVersion);
        }

        Task<string> dgVoodooTask = GetLatestDgVoodooReleaseVersionAsync();
        Task<string> radminTask = GetLatestRadminVersionAsync();
        Task<WindowsUpdateCheckInfo> windowsUpdateTask = GetWindowsUpdateCheckInfoAsync();
        Task<NvidiaDriverReleaseInfo?> nvidiaTask = nvidiaAdapter == null
            ? Task.FromResult<NvidiaDriverReleaseInfo?>(null)
            : GetLatestNvidiaDriverAsync(nvidiaAdapter.Name);

        await Task.WhenAll(dgVoodooTask, radminTask, windowsUpdateTask, nvidiaTask);

        WindowsUpdateCheckInfo windowsUpdateInfo = windowsUpdateTask.Result;
        snapshot.WindowsUpdateCheckSucceeded = windowsUpdateInfo.CheckSucceeded;
        snapshot.WindowsUpdatePendingCount = windowsUpdateInfo.PendingCount;
        snapshot.WindowsUpdateLastCheckedText = windowsUpdateInfo.LastCheckedText;

        snapshot.DgVoodooLatestVersion = dgVoodooTask.Result;
        snapshot.DgVoodooUpdateAvailable = IsUpdateAvailable(snapshot.DgVoodooInstalledVersion, snapshot.DgVoodooLatestVersion);

        snapshot.RadminLatestVersion = radminTask.Result;
        snapshot.RadminUpdateAvailable = IsUpdateAvailable(snapshot.RadminInstalledVersion, snapshot.RadminLatestVersion);

        NvidiaDriverReleaseInfo? nvidiaRelease = nvidiaTask.Result;
        if (nvidiaRelease != null)
        {
            snapshot.NvidiaLatestDriverVersion = nvidiaRelease.Version;
            snapshot.NvidiaLatestDriverName = nvidiaRelease.DriverName;
            snapshot.NvidiaLatestDriverReleaseDate = nvidiaRelease.ReleaseDate;
            snapshot.NvidiaDriverPageUrl = nvidiaRelease.DriverPageUrl;
            snapshot.NvidiaUpdateAvailable = IsUpdateAvailable(snapshot.NvidiaInstalledDriverVersion, snapshot.NvidiaLatestDriverVersion);
        }

        return snapshot;
    }

    private static string GetInstalledVersion(string exePath, string missingText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return missingText;

            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            return NormalizeVersionString(versionInfo.FileVersion);
        }
        catch
        {
            return "unbekannt";
        }
    }

    private static bool CheckFirewallRulesExist()
    {
        try
        {
            string tcpOutput = RunCommand("netsh.exe", "advfirewall firewall show rule name=\"Catan DirectPlay TCP\"");
            string udpOutput = RunCommand("netsh.exe", "advfirewall firewall show rule name=\"Catan DirectPlay UDP\"");

            bool tcpExists = tcpOutput.IndexOf("Catan DirectPlay TCP", StringComparison.OrdinalIgnoreCase) >= 0;
            bool udpExists = udpOutput.IndexOf("Catan DirectPlay UDP", StringComparison.OrdinalIgnoreCase) >= 0;
            return tcpExists && udpExists;
        }
        catch
        {
            return false;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
            return string.Empty;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output ?? string.Empty;
    }

    private static async Task<string> GetLatestRadminVersionAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");
            string html = await client.GetStringAsync("https://www.radmin-vpn.com/de/");
            Match match = Regex.Match(html, @"Radmin_VPN_(\d+(?:\.\d+)+)\.exe", RegexOptions.IgnoreCase);
            if (match.Success)
                return NormalizeVersionString(match.Groups[1].Value);
        }
        catch
        {
        }

        return "online nicht pruefbar";
    }

    private static async Task<WindowsUpdateCheckInfo> GetWindowsUpdateCheckInfoAsync()
    {
        string lastCheckedText = GetWindowsUpdateLastCheckedText();

        try
        {
            Task<WindowsUpdateQueryResult> queryTask = RunStaAsync(QueryPendingWindowsUpdates);
            Task completedTask = await Task.WhenAny(queryTask, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completedTask != queryTask)
                return new WindowsUpdateCheckInfo(false, 0, lastCheckedText);

            WindowsUpdateQueryResult result = await queryTask;
            return new WindowsUpdateCheckInfo(true, result.PendingCount, lastCheckedText);
        }
        catch
        {
            return new WindowsUpdateCheckInfo(false, 0, lastCheckedText);
        }
    }

    private static WindowsUpdateQueryResult QueryPendingWindowsUpdates()
    {
        object? session = null;
        object? searcher = null;
        object? searchResult = null;
        object? updates = null;

        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType == null)
                return new WindowsUpdateQueryResult(0);

            session = Activator.CreateInstance(sessionType);
            if (session == null)
                return new WindowsUpdateQueryResult(0);

            searcher = InvokeComMethod(session, "CreateUpdateSearcher");
            searchResult = InvokeComMethod(searcher, "Search", "IsInstalled=0 and IsHidden=0 and BrowseOnly=0");
            updates = GetComProperty(searchResult, "Updates");

            int count = Convert.ToInt32(GetComProperty(updates, "Count"), CultureInfo.InvariantCulture);
            int pendingCount = 0;

            for (int i = 0; i < count; i++)
            {
                object? update = null;

                try
                {
                    update = InvokeComMethod(updates, "Item", i);
                    string title = Convert.ToString(GetComProperty(update, "Title")) ?? string.Empty;
                    if (!ShouldIgnoreWindowsUpdate(title))
                        pendingCount++;
                }
                finally
                {
                    ReleaseComObject(update);
                }
            }

            return new WindowsUpdateQueryResult(pendingCount);
        }
        finally
        {
            ReleaseComObject(updates);
            ReleaseComObject(searchResult);
            ReleaseComObject(searcher);
            ReleaseComObject(session);
        }
    }

    private static bool ShouldIgnoreWindowsUpdate(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return title.Contains("Microsoft Defender Antivirus", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Security Intelligence", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Definition Update", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowsUpdateLastCheckedText()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect");
            string raw = Convert.ToString(key?.GetValue("LastSuccessTime")) ?? string.Empty;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime lastChecked))
                return string.Empty;

            if (lastChecked.Date == DateTime.Today)
                return "zuletzt geprueft: heute, " + lastChecked.ToString("HH:mm", CultureInfo.InvariantCulture);

            if (lastChecked.Date == DateTime.Today.AddDays(-1))
                return "zuletzt geprueft: gestern, " + lastChecked.ToString("HH:mm", CultureInfo.InvariantCulture);

            return "zuletzt geprueft: " + lastChecked.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static object GetComProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, null) ?? string.Empty;
    }

    private static object InvokeComMethod(object target, string methodName, params object[] args)
    {
        return target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, args) ?? string.Empty;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private static async Task<string> GetLatestDgVoodooReleaseVersionAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");
            string json = await client.GetStringAsync("https://api.github.com/repos/dege-diosg/dgVoodoo2/releases/latest");
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("tag_name", out JsonElement tagName))
                return NormalizeVersionString(tagName.GetString());

            if (root.TryGetProperty("name", out JsonElement name))
                return NormalizeVersionString(name.GetString());
        }
        catch
        {
        }

        return "online nicht pruefbar";
    }

    private async Task<NvidiaDriverReleaseInfo?> GetLatestNvidiaDriverAsync(string installedGpuName)
    {
        try
        {
            NvidiaProductLookup? product = await ResolveNvidiaProductAsync(installedGpuName);
            if (product == null)
                return null;

            int osId = GetNvidiaWindowsOsId();
            using var client = CreateHttpClient();
            string url = NvidiaDriverSearchUrl + "?psid=" + product.SeriesId + "&pfid=" + product.ProductId + "&osid=" + osId + "&lid=1&dtcid=1&ctk=0&qnfslb=00&lang=en-us";
            string html = await client.GetStringAsync(url);
            return ParseLatestNvidiaDriver(html);
        }
        catch
        {
            return null;
        }
    }

    private async Task<NvidiaProductLookup?> ResolveNvidiaProductAsync(string installedGpuName)
    {
        string normalizedProductName = NormalizeNvidiaProductName(installedGpuName);
        if (string.IsNullOrWhiteSpace(normalizedProductName))
            return null;

        if (nvidiaProductLookupCache.TryGetValue(normalizedProductName, out NvidiaProductLookup? cached))
            return cached;

        IReadOnlyList<NvidiaLookupValue> series = await GetNvidiaSeriesAsync();
        foreach (string candidateSeriesName in GetCandidateNvidiaSeriesNames(normalizedProductName))
        {
            NvidiaLookupValue? seriesMatch = series.FirstOrDefault(value => value.Name.Equals(candidateSeriesName, StringComparison.OrdinalIgnoreCase));
            if (seriesMatch == null)
                continue;

            NvidiaProductLookup? match = await FindNvidiaProductInSeriesAsync(seriesMatch, normalizedProductName);
            if (match != null)
                return CacheNvidiaProductLookup(normalizedProductName, match);
        }

        foreach (NvidiaLookupValue seriesEntry in series)
        {
            NvidiaProductLookup? match = await FindNvidiaProductInSeriesAsync(seriesEntry, normalizedProductName);
            if (match != null)
                return CacheNvidiaProductLookup(normalizedProductName, match);
        }

        return null;
    }

    private async Task<IReadOnlyList<NvidiaLookupValue>> GetNvidiaSeriesAsync()
    {
        if (nvidiaSeriesCache != null)
            return nvidiaSeriesCache;

        using var client = CreateHttpClient();
        string xml = await client.GetStringAsync(NvidiaLookupApiBaseUrl + "?TypeID=2&ParentID=1");
        nvidiaSeriesCache = ParseNvidiaLookupValues(xml);
        return nvidiaSeriesCache;
    }

    private async Task<IReadOnlyList<NvidiaLookupValue>> GetNvidiaProductsAsync(int seriesId)
    {
        if (nvidiaProductsBySeriesId.TryGetValue(seriesId, out IReadOnlyList<NvidiaLookupValue>? cached))
            return cached;

        using var client = CreateHttpClient();
        string xml = await client.GetStringAsync(NvidiaLookupApiBaseUrl + "?TypeID=3&ParentID=" + seriesId);
        IReadOnlyList<NvidiaLookupValue> products = ParseNvidiaLookupValues(xml);
        nvidiaProductsBySeriesId[seriesId] = products;
        return products;
    }

    private async Task<NvidiaProductLookup?> FindNvidiaProductInSeriesAsync(NvidiaLookupValue series, string normalizedProductName)
    {
        IReadOnlyList<NvidiaLookupValue> products = await GetNvidiaProductsAsync(series.Value);
        NvidiaLookupValue? product = products.FirstOrDefault(value => value.Name.Equals(normalizedProductName, StringComparison.OrdinalIgnoreCase));
        return product == null ? null : new NvidiaProductLookup(series.Value, product.Value, product.Name);
    }

    private static IReadOnlyList<NvidiaLookupValue> ParseNvidiaLookupValues(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants("LookupValue")
            .Select(element => new NvidiaLookupValue(
                (element.Element("Name")?.Value ?? string.Empty).Trim(),
                int.TryParse(element.Element("Value")?.Value, out int value) ? value : -1))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Value >= 0)
            .ToList();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");
        return client;
    }

    private static NvidiaInstalledAdapter? GetInstalledNvidiaAdapter()
    {
        try
        {
            using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(NvidiaDisplayAdapterClassKeyPath);
            if (classKey == null)
                return null;

            return classKey.GetSubKeyNames()
                .Where(name => name.Length == 4 && name.All(char.IsDigit))
                .Select(name => classKey.OpenSubKey(name))
                .OfType<RegistryKey>()
                .Select(key =>
                {
                    using (key)
                    {
                        string adapterName = CleanWhitespace(Convert.ToString(key.GetValue("DriverDesc")) ?? Convert.ToString(key.GetValue("HardwareInformation.AdapterString")) ?? string.Empty);
                        string driverVersion = CleanWhitespace(Convert.ToString(key.GetValue("DriverVersion")) ?? string.Empty);
                        return new NvidiaInstalledAdapter(adapterName, driverVersion);
                    }
                })
                .FirstOrDefault(adapter => adapter.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeNvidiaProductName(string gpuName)
    {
        string normalized = CleanWhitespace(gpuName);
        normalized = normalized.Replace("NVIDIA ", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized;
    }

    private static IReadOnlyList<string> GetCandidateNvidiaSeriesNames(string productName)
    {
        var candidates = new List<string>();
        bool notebook = IsNotebookNvidiaGpu(productName);

        if (TryGetNvidiaSeriesName(productName, notebook, out string? primaryCandidate) && primaryCandidate != null)
            candidates.Add(primaryCandidate);

        if (notebook && TryGetNvidiaSeriesName(productName, false, out string? desktopFallback) && desktopFallback != null && !candidates.Contains(desktopFallback, StringComparer.OrdinalIgnoreCase))
            candidates.Add(desktopFallback);

        return candidates;
    }

    private static bool TryGetNvidiaSeriesName(string productName, bool notebook, out string? seriesName)
    {
        seriesName = null;

        Match mxMatch = Regex.Match(productName, @"GeForce\s+MX(?<family>\d)", RegexOptions.IgnoreCase);
        if (mxMatch.Success)
        {
            seriesName = "GeForce MX" + mxMatch.Groups["family"].Value + "00 Series (Notebooks)";
            return true;
        }

        Match rtxMatch = Regex.Match(productName, @"GeForce\s+RTX\s+(?<family>\d{2})\d{2}", RegexOptions.IgnoreCase);
        if (rtxMatch.Success)
        {
            seriesName = "GeForce RTX " + rtxMatch.Groups["family"].Value + " Series" + (notebook ? " (Notebooks)" : string.Empty);
            return true;
        }

        Match gtx16Match = Regex.Match(productName, @"GeForce\s+GTX\s+16\d{2}", RegexOptions.IgnoreCase);
        if (gtx16Match.Success)
        {
            seriesName = "GeForce 16 Series" + (notebook ? " (Notebooks)" : string.Empty);
            return true;
        }

        Match legacyMatch = Regex.Match(productName, @"GeForce\s+(?:GTX|GT)\s+(?<model>\d{3,4}M?)", RegexOptions.IgnoreCase);
        if (!legacyMatch.Success)
            return false;

        string modelText = legacyMatch.Groups["model"].Value.ToUpperInvariant();
        bool modelNotebook = notebook || modelText.EndsWith("M", StringComparison.OrdinalIgnoreCase);
        string digitsOnly = new string(modelText.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digitsOnly))
            return false;

        if (!TryGetLegacySeriesLabel(digitsOnly, modelNotebook, out seriesName))
            return false;

        return true;
    }

    private static bool TryGetLegacySeriesLabel(string digitsOnly, bool notebook, out string? seriesName)
    {
        seriesName = null;
        if (!int.TryParse(digitsOnly, out int modelNumber))
            return false;

        if (digitsOnly.Length == 4 && modelNumber >= 1000)
        {
            int series = modelNumber / 100;
            seriesName = "GeForce " + series + " Series" + (notebook ? " (Notebooks)" : string.Empty);
            return true;
        }

        int legacySeries = modelNumber / 100;
        if (legacySeries < 1)
            return false;

        if (legacySeries >= 8)
        {
            seriesName = "GeForce " + legacySeries + (notebook ? "M Series (Notebooks)" : " Series");
            return true;
        }

        seriesName = legacySeries <= 5
            ? "GeForce " + legacySeries + " FX Series"
            : "GeForce " + legacySeries + " Series";
        return true;
    }

    private static bool IsNotebookNvidiaGpu(string productName)
    {
        return productName.Contains("Notebook", StringComparison.OrdinalIgnoreCase) ||
               productName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
               productName.Contains("Max-Q", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(productName, @"\b\d{3,4}M\b", RegexOptions.IgnoreCase);
    }

    private static int GetNvidiaWindowsOsId()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string productName = Convert.ToString(key?.GetValue("ProductName")) ?? string.Empty;
            string buildNumber = Convert.ToString(key?.GetValue("CurrentBuildNumber")) ?? string.Empty;

            if (productName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
                return 135;

            if (int.TryParse(buildNumber, out int build) && build >= 22000)
                return 135;
        }
        catch
        {
        }

        return 57;
    }

    private static NvidiaDriverReleaseInfo? ParseLatestNvidiaDriver(string html)
    {
        MatchCollection matches = Regex.Matches(
            html,
            @"<td class=""gridItem driverName"">\s*<b><a href='(?<url>[^']+)'>\s*(?<name>[^<]+?)\s*</a>.*?</td>\s*<td class=""gridItem"">\s*(?<version>\d+(?:\.\d+)+)\s*</td>\s*<td class=""gridItem""[^>]*>\s*(?<date>[^<]+?)\s*</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Match? selected = matches
            .Cast<Match>()
            .FirstOrDefault(match => match.Groups["name"].Value.Contains("Game Ready", StringComparison.OrdinalIgnoreCase))
            ?? matches.Cast<Match>().FirstOrDefault();

        if (selected == null)
            return null;

        string url = WebUtility.HtmlDecode(selected.Groups["url"].Value.Trim());
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;

        return new NvidiaDriverReleaseInfo(
            WebUtility.HtmlDecode(selected.Groups["name"].Value.Trim()),
            NormalizeVersionString(selected.Groups["version"].Value),
            WebUtility.HtmlDecode(selected.Groups["date"].Value.Trim()),
            url);
    }

    private static string NormalizeNvidiaDriverVersion(string driverVersion)
    {
        if (string.IsNullOrWhiteSpace(driverVersion))
            return "unbekannt";

        string[] parts = driverVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
            return NormalizeVersionString(driverVersion);

        if (!int.TryParse(parts[2], out int branch) || !int.TryParse(parts[3], out int tail))
            return NormalizeVersionString(driverVersion);

        int publicPrefix = branch - 10;
        if (publicPrefix < 0)
            return NormalizeVersionString(driverVersion);

        string combined = publicPrefix.ToString() + tail.ToString("0000");
        if (combined.Length <= 2)
            return NormalizeVersionString(driverVersion);

        return combined[..^2] + "." + combined[^2..];
    }

    private static string CleanWhitespace(string input)
    {
        return string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private NvidiaProductLookup CacheNvidiaProductLookup(string productName, NvidiaProductLookup lookup)
    {
        nvidiaProductLookupCache[productName] = lookup;
        return lookup;
    }

    private static bool IsUpdateAvailable(string installedVersion, string latestVersion)
    {
        if (!TryParseVersion(installedVersion, out Version? installed) || !TryParseVersion(latestVersion, out Version? latest))
            return false;

        return installed < latest;
    }

    private static bool TryParseVersion(string input, out Version? version)
    {
        version = null;
        string normalized = NormalizeVersionString(input);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        int dotCount = normalized.Count(c => c == '.');
        while (dotCount < 1)
        {
            normalized += ".0";
            dotCount++;
        }

        return Version.TryParse(normalized, out version);
    }

    private static string NormalizeVersionString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string normalized = input.Trim();
        normalized = normalized.Replace("dgVoodoo", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        normalized = normalized.TrimStart('v', 'V');

        var builder = new StringBuilder();
        bool started = false;
        foreach (char c in normalized)
        {
            if (char.IsDigit(c))
            {
                builder.Append(c);
                started = true;
            }
            else if (c == '.' && started)
            {
                builder.Append(c);
            }
            else if (started)
            {
                break;
            }
        }

        return builder.ToString().Trim('.');
    }

    private sealed record NvidiaLookupValue(string Name, int Value);

    private sealed record NvidiaProductLookup(int SeriesId, int ProductId, string ProductName);

    private sealed record NvidiaDriverReleaseInfo(string DriverName, string Version, string ReleaseDate, string DriverPageUrl);

    private sealed record NvidiaInstalledAdapter(string Name, string DriverVersion);

    private sealed record WindowsUpdateCheckInfo(bool CheckSucceeded, int PendingCount, string LastCheckedText);

    private sealed record WindowsUpdateQueryResult(int PendingCount);
}
