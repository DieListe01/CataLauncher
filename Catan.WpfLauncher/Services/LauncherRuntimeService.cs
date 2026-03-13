using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Catan.WpfLauncher.Services;

public sealed class LauncherRuntimeService
{
    public void StartFile(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            throw new FileNotFoundException("Datei wurde nicht gefunden.", exePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    public void StartFull(string catanExePath, string dgVoodooExePath, string radminExePath)
    {
        if (File.Exists(radminExePath))
            StartFile(radminExePath);

        if (File.Exists(dgVoodooExePath))
            StartFile(dgVoodooExePath);

        StartFile(catanExePath);
    }

    public string GetLanIp()
    {
        return GetConnectionInfo(
                   nic =>
                       nic.OperationalStatus == OperationalStatus.Up &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Ppp &&
                       !IsRadminAdapter(nic),
                   address => IsLocalLanAddress(address.Address))?.Address
            ?? "nicht gefunden";
    }

    public string GetLanAdapterName()
    {
        return GetConnectionInfo(
                   nic =>
                       nic.OperationalStatus == OperationalStatus.Up &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                       nic.NetworkInterfaceType != NetworkInterfaceType.Ppp &&
                       !IsRadminAdapter(nic),
                   address => IsLocalLanAddress(address.Address))?.AdapterName
            ?? "nicht gefunden";
    }

    public string GetVpnIp()
    {
        return GetConnectionInfo(nic =>
            nic.OperationalStatus == OperationalStatus.Up &&
            IsRadminAdapter(nic) &&
            nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.Address.ToString().StartsWith("26.")),
            address => address.Address.ToString().StartsWith("26."))?.Address
            ?? "nicht gefunden";
    }

    public string GetVpnAdapterName()
    {
        return GetConnectionInfo(nic =>
            nic.OperationalStatus == OperationalStatus.Up &&
            IsRadminAdapter(nic) &&
            nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.Address.ToString().StartsWith("26.")),
            address => address.Address.ToString().StartsWith("26."))?.AdapterName
            ?? "nicht gefunden";
    }

    public bool IsProcessRunning(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return false;

        string fullExePath = Path.GetFullPath(exePath);
        string processName = Path.GetFileNameWithoutExtension(fullExePath);

        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, fullExePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static NetworkConnectionInfo? GetConnectionInfo(Func<NetworkInterface, bool> nicPredicate, Func<UnicastIPAddressInformation, bool>? addressPredicate = null)
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!nicPredicate(nic))
                continue;

            foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && (addressPredicate == null || addressPredicate(address)))
                {
                    return new NetworkConnectionInfo(address.Address.ToString(), FormatAdapterName(nic));
                }
            }
        }

        return null;
    }

    private static string FormatAdapterName(NetworkInterface nic)
    {
        string name = nic.Name.Trim();
        string description = nic.Description.Trim();

        if (string.IsNullOrWhiteSpace(description) || string.Equals(name, description, StringComparison.OrdinalIgnoreCase))
            return name;

        return name + " (" + description + ")";
    }

    private static bool IsRadminAdapter(NetworkInterface nic)
    {
        return nic.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase) ||
               nic.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        if (bytes[0] == 26)
            return false;

        if (bytes[0] == 169 && bytes[1] == 254)
            return false;

        if (bytes[0] == 10)
            return true;

        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        return bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
    }

    private sealed record NetworkConnectionInfo(string Address, string AdapterName);
}
