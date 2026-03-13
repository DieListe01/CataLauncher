using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Catan.WpfLauncher.Models;

namespace Catan.WpfLauncher.Services;

public sealed class SystemInfoService
{
    private const string DisplayAdapterClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const int SmCmonitors = 80;

    public IReadOnlyList<SystemInfoItem> GetItems(string lanIp, string vpnIp, string lanAdapterName, string vpnAdapterName, bool isAdministrator, string catanExePath, SystemStatusSnapshot? statusSnapshot)
    {
        var memory = new MemoryStatusEx();
        memory.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        GlobalMemoryStatusEx(memory);

        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(systemRoot);
        WindowsInfo windowsInfo = GetWindowsInfo();
        IReadOnlyList<GraphicsAdapterInfo> graphicsAdapters = GetGraphicsAdapters();
        bool hasNvidiaAdapter = statusSnapshot?.HasNvidiaAdapter == true || graphicsAdapters.Any(adapter => adapter.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        string catanWriteAccess = GetCatanWriteAccess(catanExePath);

        var items = new List<SystemInfoItem>
        {
            new() { Label = "Betriebssystem", Value = windowsInfo.DisplayName },
            new() { Label = "Windows-Edition", Value = windowsInfo.Edition },
            new() { Label = "Windows-Build", Value = windowsInfo.Build },
            CreateWindowsUpdateItem(statusSnapshot),
            new() { Label = "Windows-Laufzeit", Value = GetSystemUptime() },
            new() { Label = "Rechnername", Value = Environment.MachineName },
            new() { Label = "Benutzer", Value = Environment.UserName },
            new() { Label = "Als Admin gestartet", Value = isAdministrator ? "ja" : "nein" },
            new() { Label = "Architektur", Value = RuntimeInformation.OSArchitecture + (Environment.Is64BitOperatingSystem ? " (64-Bit)" : " (32-Bit)") },
            new() { Label = "Prozessor", Value = GetProcessorName() },
            new() { Label = "CPU-Kerne", Value = Environment.ProcessorCount.ToString() },
            new() { Label = "Grafikkarte", Value = FormatGraphicsAdapterNames(graphicsAdapters) },
            new() { Label = "Grafiktreiber", Value = FormatGraphicsDriverVersions(graphicsAdapters) },
            new() { Label = "RAM gesamt", Value = FormatBytes(memory.ullTotalPhys) },
            new() { Label = "RAM frei", Value = FormatBytes(memory.ullAvailPhys) },
            new() { Label = ".NET Runtime", Value = RuntimeInformation.FrameworkDescription },
            new() { Label = "Bildschirm", Value = ((int)SystemParameters.PrimaryScreenWidth) + " x " + ((int)SystemParameters.PrimaryScreenHeight) },
            new() { Label = "Monitore", Value = GetMonitorCount() },
            new() { Label = "Skalierung", Value = GetDisplayScaling() },
            CreateLanIpStatusItem(lanIp),
            new() { Label = "LAN-Adapter", Value = DescribeAdapterName(lanAdapterName, "kein aktiver LAN-Adapter") },
            CreateVpnStatusItem(vpnIp),
            new() { Label = "VPN-Adapter", Value = DescribeAdapterName(vpnAdapterName, "kein Radmin-Adapter aktiv") },
            CreateFirewallRulesItem(statusSnapshot),
            new() { Label = "Systemlaufwerk", Value = drive.IsReady ? FormatBytes((ulong)drive.AvailableFreeSpace) + " frei von " + FormatBytes((ulong)drive.TotalSize) : "unbekannt" },
            CreateCatanWriteAccessItem(catanWriteAccess)
        };

        if (hasNvidiaAdapter)
        {
            items.Add(CreateNvidiaDriverItem(statusSnapshot));
        }

        return OrderSystemInfoItems(items);
    }

    private static string GetSystemUptime()
    {
        TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        if (uptime.TotalDays >= 1)
        {
            int days = (int)uptime.TotalDays;
            return days + (days == 1 ? " Tag, " : " Tage, ") + uptime.Hours + " Std.";
        }

        if (uptime.TotalHours >= 1)
            return uptime.Hours + " Std., " + uptime.Minutes + " Min.";

        return Math.Max(1, uptime.Minutes) + " Min.";
    }

    private static string GetProcessorName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string name = Convert.ToString(key?.GetValue("ProcessorNameString")) ?? string.Empty;
            return string.IsNullOrWhiteSpace(name)
                ? "unbekannt"
                : string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch
        {
            return "unbekannt";
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value.ToString(unitIndex == 0 ? "0" : "0.0") + " " + units[unitIndex];
    }

    private static WindowsInfo GetWindowsInfo()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key == null)
                return new WindowsInfo(RuntimeInformation.OSDescription, "unbekannt", "unbekannt");

            string productName = Convert.ToString(key.GetValue("ProductName")) ?? "Windows";
            string editionId = Convert.ToString(key.GetValue("EditionID")) ?? string.Empty;
            string displayVersion = Convert.ToString(key.GetValue("DisplayVersion")) ?? Convert.ToString(key.GetValue("ReleaseId")) ?? string.Empty;
            string buildNumber = Convert.ToString(key.GetValue("CurrentBuildNumber")) ?? string.Empty;
            string ubr = Convert.ToString(key.GetValue("UBR")) ?? string.Empty;

            if (int.TryParse(buildNumber, out int build) && build >= 22000 && productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);

            string buildText = string.IsNullOrWhiteSpace(ubr) ? buildNumber : buildNumber + "." + ubr;
            string versionText = string.IsNullOrWhiteSpace(displayVersion) ? string.Empty : " " + displayVersion;
            string finalBuild = string.IsNullOrWhiteSpace(buildText) ? string.Empty : " (Build " + buildText + ")";

            string buildLabel = string.IsNullOrWhiteSpace(buildText)
                ? "unbekannt"
                : string.IsNullOrWhiteSpace(displayVersion)
                    ? "Build " + buildText
                    : displayVersion + " / Build " + buildText;

            return new WindowsInfo(productName + versionText + finalBuild, FormatWindowsEdition(editionId), buildLabel);
        }
        catch
        {
            return new WindowsInfo(RuntimeInformation.OSDescription, "unbekannt", "unbekannt");
        }
    }

    private static string FormatWindowsEdition(string editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
            return "unbekannt";

        return editionId switch
        {
            "Core" => "Home",
            "CoreSingleLanguage" => "Home Single Language",
            "Professional" => "Pro",
            _ => editionId
        };
    }

    private static IReadOnlyList<GraphicsAdapterInfo> GetGraphicsAdapters()
    {
        try
        {
            using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(DisplayAdapterClassKeyPath);
            if (classKey == null)
                return Array.Empty<GraphicsAdapterInfo>();

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
                        return new GraphicsAdapterInfo(adapterName, string.IsNullOrWhiteSpace(driverVersion) ? "unbekannt" : driverVersion);
                    }
                })
                .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Name))
                .GroupBy(adapter => adapter.Name + "|" + adapter.DriverVersion, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch
        {
            return Array.Empty<GraphicsAdapterInfo>();
        }
    }

    private static string FormatGraphicsAdapterNames(IReadOnlyList<GraphicsAdapterInfo> adapters)
    {
        return adapters.Count == 0
            ? "unbekannt"
            : string.Join(" | ", adapters.Select(adapter => adapter.Name));
    }

    private static string FormatGraphicsDriverVersions(IReadOnlyList<GraphicsAdapterInfo> adapters)
    {
        return adapters.Count == 0
            ? "unbekannt"
            : string.Join(" | ", adapters.Select(adapter => adapter.Name + ": " + adapter.DriverVersion));
    }

    private static string GetMonitorCount()
    {
        try
        {
            return GetSystemMetrics(SmCmonitors).ToString();
        }
        catch
        {
            return "unbekannt";
        }
    }

    private static string GetDisplayScaling()
    {
        try
        {
            double scaling = GetDpiForSystem() / 96d * 100d;
            return Math.Round(scaling).ToString("0") + " %";
        }
        catch
        {
            return "unbekannt";
        }
    }

    private static string DescribeLanIp(string lanIp)
    {
        return NormalizeNetworkValue(lanIp, "kein aktiver LAN-Adapter");
    }

    private static string DescribeVpnIp(string vpnIp)
    {
        string normalized = NormalizeNetworkValue(vpnIp, "kein Radmin-Adapter aktiv");
        return normalized == "wird geladen" ? normalized : (normalized.Contains('.') ? "verbunden: " + normalized : normalized);
    }

    private static string DescribeAdapterName(string adapterName, string missingText)
    {
        return NormalizeNetworkValue(adapterName, missingText);
    }

    private static string DescribeFirewallRules(SystemStatusSnapshot? statusSnapshot)
    {
        if (statusSnapshot == null)
            return "wird geladen";

        return statusSnapshot.FirewallRulesExist ? "vorhanden" : "fehlen";
    }

    private static SystemInfoItem CreateLanIpStatusItem(string lanIp)
    {
        string value = DescribeLanIp(lanIp);

        if (value == "wird geladen")
        {
            return new SystemInfoItem
            {
                Label = "LAN-IP",
                Value = value,
                BadgeText = "Pruefung",
                BadgeKind = "neutral"
            };
        }

        bool available = value.Contains('.');
        return new SystemInfoItem
        {
            Label = "LAN-IP",
            Value = value,
            BadgeText = available ? "Verfuegbar" : "Offline",
            BadgeKind = available ? "ok" : "warning"
        };
    }

    private static SystemInfoItem CreateVpnStatusItem(string vpnIp)
    {
        string value = DescribeVpnIp(vpnIp);

        if (value == "wird geladen")
        {
            return new SystemInfoItem
            {
                Label = "Radmin-VPN",
                Value = value,
                BadgeText = "Pruefung",
                BadgeKind = "neutral"
            };
        }

        bool connected = value.StartsWith("verbunden:", StringComparison.OrdinalIgnoreCase);
        return new SystemInfoItem
        {
            Label = "Radmin-VPN",
            Value = value,
            BadgeText = connected ? "Verbunden" : "Offline",
            BadgeKind = connected ? "ok" : "warning"
        };
    }

    private static SystemInfoItem CreateFirewallRulesItem(SystemStatusSnapshot? statusSnapshot)
    {
        string value = DescribeFirewallRules(statusSnapshot);

        if (statusSnapshot == null)
        {
            return new SystemInfoItem
            {
                Label = "Firewall-Regeln",
                Value = value,
                BadgeText = "Pruefung",
                BadgeKind = "neutral"
            };
        }

        return new SystemInfoItem
        {
            Label = "Firewall-Regeln",
            Value = value,
            BadgeText = statusSnapshot.FirewallRulesExist ? "Aktiv" : "Fehlt",
            BadgeKind = statusSnapshot.FirewallRulesExist ? "ok" : "warning"
        };
    }

    private static SystemInfoItem CreateWindowsUpdateItem(SystemStatusSnapshot? statusSnapshot)
    {
        var item = new SystemInfoItem
        {
            Label = "Windows Update",
            ActionId = "windows-update-settings",
            ActionText = "Oeffnen",
            ActionToolTip = "Oeffnet die Windows-Update-Einstellungen."
        };

        if (statusSnapshot == null)
        {
            item.Value = "wird geprueft";
            item.BadgeText = "Pruefung";
            item.BadgeKind = "neutral";
            return item;
        }

        string suffix = string.IsNullOrWhiteSpace(statusSnapshot.WindowsUpdateLastCheckedText)
            ? string.Empty
            : Environment.NewLine + statusSnapshot.WindowsUpdateLastCheckedText;

        if (!statusSnapshot.WindowsUpdateCheckSucceeded)
        {
            item.Value = "Status nicht pruefbar" + suffix;
            item.BadgeText = "Unklar";
            item.BadgeKind = "neutral";
            return item;
        }

        if (statusSnapshot.WindowsUpdatePendingCount > 0)
        {
            item.Value = statusSnapshot.WindowsUpdatePendingCount == 1
                ? "1 Update verfuegbar" + suffix
                : statusSnapshot.WindowsUpdatePendingCount + " Updates verfuegbar" + suffix;
            item.BadgeText = "Update";
            item.BadgeKind = "warning";
            item.ActionText = "Updates";
            item.ActionToolTip = "Oeffnet Windows Update mit den verfuegbaren Updates.";
            return item;
        }

        item.Value = "keine Updates verfuegbar" + suffix;
        item.BadgeText = "Aktuell";
        item.BadgeKind = "ok";
        return item;
    }

    private static string FormatNvidiaDriverSummary(SystemStatusSnapshot? statusSnapshot)
    {
        if (statusSnapshot == null)
            return "wird geladen";

        if (!statusSnapshot.HasNvidiaAdapter)
            return "kein NVIDIA-Adapter";

        string productName = statusSnapshot.NvidiaGpuName.Replace("NVIDIA ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        string latestVersion = string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverVersion) ? "online nicht pruefbar" : statusSnapshot.NvidiaLatestDriverVersion;
        return productName + Environment.NewLine + "installiert: " + statusSnapshot.NvidiaInstalledDriverVersion + " | online: " + latestVersion;
    }

    private static string FormatNvidiaDriverDetails(SystemStatusSnapshot? statusSnapshot)
    {
        if (statusSnapshot == null)
            return "wird geladen";

        if (!statusSnapshot.HasNvidiaAdapter)
            return "kein NVIDIA-Adapter";

        if (string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverVersion) || statusSnapshot.NvidiaLatestDriverVersion == "online nicht pruefbar")
            return string.Empty;

        string driverName = string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverName) ? "Treiber" : statusSnapshot.NvidiaLatestDriverName;
        string releaseDate = string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverReleaseDate) ? string.Empty : " vom " + statusSnapshot.NvidiaLatestDriverReleaseDate;
        return driverName + releaseDate;
    }

    private static SystemInfoItem CreateNvidiaDriverItem(SystemStatusSnapshot? statusSnapshot)
    {
        if (statusSnapshot == null)
        {
            return new SystemInfoItem
            {
                Label = "NVIDIA-Treiber",
                Value = "wird geladen",
                BadgeText = "Pruefung",
                BadgeKind = "neutral"
            };
        }

        if (!statusSnapshot.HasNvidiaAdapter)
        {
            return new SystemInfoItem
            {
                Label = "NVIDIA-Treiber",
                Value = "kein NVIDIA-Adapter",
                BadgeText = string.Empty
            };
        }

        string value = FormatNvidiaDriverSummary(statusSnapshot);
        string details = FormatNvidiaDriverDetails(statusSnapshot);
        if (!string.IsNullOrWhiteSpace(details))
            value += Environment.NewLine + details;

        if (string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverVersion) || statusSnapshot.NvidiaLatestDriverVersion == "online nicht pruefbar")
        {
            return new SystemInfoItem
            {
                Label = "NVIDIA-Treiber",
                Value = value,
                BadgeText = "Unklar",
                BadgeKind = "neutral"
            };
        }

        return CreateNvidiaStatusActionItem(new SystemInfoItem
        {
            Label = "NVIDIA-Treiber",
            Value = value,
            BadgeText = statusSnapshot.NvidiaUpdateAvailable ? "Update" : "Aktuell",
            BadgeKind = statusSnapshot.NvidiaUpdateAvailable ? "warning" : "ok"
        }, statusSnapshot);
    }

    private static SystemInfoItem CreateNvidiaStatusActionItem(SystemInfoItem item, SystemStatusSnapshot statusSnapshot)
    {
        if (string.IsNullOrWhiteSpace(statusSnapshot.NvidiaDriverPageUrl))
            return item;

        item.ActionId = "nvidia-driver-page";
        item.ActionText = statusSnapshot.NvidiaUpdateAvailable ? "Update holen" : "Treiberseite";
        item.ActionToolTip = string.IsNullOrWhiteSpace(statusSnapshot.NvidiaLatestDriverReleaseDate)
            ? "Oeffnet die passende NVIDIA-Treiberseite."
            : "Oeffnet die passende NVIDIA-Treiberseite vom " + statusSnapshot.NvidiaLatestDriverReleaseDate + ".";
        return item;
    }

    private static SystemInfoItem CreateCatanWriteAccessItem(string writeAccess)
    {
        return writeAccess switch
        {
            "schreibbar" => new SystemInfoItem
            {
                Label = "Catan-Schreibrechte",
                Value = writeAccess,
                BadgeText = "OK",
                BadgeKind = "ok"
            },
            "keine Schreibrechte" or "Ordner fehlt" => new SystemInfoItem
            {
                Label = "Catan-Schreibrechte",
                Value = writeAccess,
                BadgeText = "Problem",
                BadgeKind = "warning"
            },
            "nicht konfiguriert" or "nicht pruefbar" or "unbekannt" => new SystemInfoItem
            {
                Label = "Catan-Schreibrechte",
                Value = writeAccess,
                BadgeText = "Unklar",
                BadgeKind = "neutral"
            },
            _ => new SystemInfoItem
            {
                Label = "Catan-Schreibrechte",
                Value = writeAccess,
                BadgeText = string.Empty
            }
        };
    }

    private static IReadOnlyList<SystemInfoItem> OrderSystemInfoItems(IReadOnlyList<SystemInfoItem> items)
    {
        return items
            .Select((item, index) => new { item, index })
            .OrderBy(entry => GetSystemInfoPriority(entry.item))
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();
    }

    private static int GetSystemInfoPriority(SystemInfoItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.BadgeText))
        {
            return item.BadgeKind switch
            {
                "warning" => 0,
                "neutral" => 1,
                "ok" => 2,
                _ => 3
            };
        }

        return item.Label switch
        {
            "LAN-Adapter" => 3,
            "VPN-Adapter" => 3,
            "NVIDIA-Treiber" => 3,
            _ => 4
        };
    }

    private static string NormalizeNetworkValue(string value, string missingText)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-" || value.EndsWith("...", StringComparison.Ordinal))
            return "wird geladen";

        return value.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase) ? missingText : value;
    }

    private static string GetCatanWriteAccess(string catanExePath)
    {
        if (string.IsNullOrWhiteSpace(catanExePath))
            return "nicht konfiguriert";

        string directory = catanExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(catanExePath) ?? string.Empty
            : catanExePath;

        if (string.IsNullOrWhiteSpace(directory))
            return "unbekannt";

        if (!Directory.Exists(directory))
            return "Ordner fehlt";

        try
        {
            string probePath = Path.Combine(directory, ".catanlauncher-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            return "schreibbar";
        }
        catch (UnauthorizedAccessException)
        {
            return "keine Schreibrechte";
        }
        catch (DirectoryNotFoundException)
        {
            return "Ordner fehlt";
        }
        catch
        {
            return "nicht pruefbar";
        }
    }

    private static string CleanWhitespace(string input)
    {
        return string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private sealed record WindowsInfo(string DisplayName, string Edition, string Build);

    private sealed record GraphicsAdapterInfo(string Name, string DriverVersion);
}
