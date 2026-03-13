using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace CatanLauncher.Services;

public sealed class AdminMaintenanceService
{
    private const string FirewallHelperTaskName = "CatanLauncher Firewall Helper";
    private const string FirewallHelperArgument = "--firewall-helper";

    private static readonly string HelperDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatanLauncher");

    private static readonly string RequestFilePath = Path.Combine(HelperDirectory, "firewall-helper.request");
    private static readonly string ResultFilePath = Path.Combine(HelperDirectory, "firewall-helper.result");

    public async Task<bool> SetFirewallRulesAsync()
    {
        return await RunFirewallActionAsync("set");
    }

    public async Task<bool> ResetFirewallRulesAsync()
    {
        return await RunFirewallActionAsync("reset");
    }

    public bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool IsFirewallHelperMode(string[] args)
    {
        return args.Any(arg => string.Equals(arg, FirewallHelperArgument, StringComparison.OrdinalIgnoreCase));
    }

    public bool ExecuteScheduledFirewallRequest()
    {
        try
        {
            Directory.CreateDirectory(HelperDirectory);
            if (!File.Exists(RequestFilePath))
            {
                WriteResult("error: request missing");
                return false;
            }

            string action = File.ReadAllText(RequestFilePath, Encoding.UTF8).Trim().ToLowerInvariant();
            bool success = action switch
            {
                "set" => ExecuteCommand(BuildSetCommand()),
                "reset" => ExecuteCommand(BuildResetCommand()),
                _ => false
            };

            WriteResult(success ? "success" : "error: command failed");
            return success;
        }
        catch (Exception ex)
        {
            WriteResult("error: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> RunFirewallActionAsync(string action)
    {
        if (IsAdministrator())
            return await Task.Run(() => ExecuteCommand(action == "set" ? BuildSetCommand() : BuildResetCommand()));

        if (!await EnsureScheduledHelperAsync())
            return false;

        Directory.CreateDirectory(HelperDirectory);
        File.WriteAllText(RequestFilePath, action, Encoding.UTF8);
        if (File.Exists(ResultFilePath))
            File.Delete(ResultFilePath);

        bool started = await RunProcessAsync(
            "schtasks.exe",
            "/run /tn \"" + FirewallHelperTaskName + "\"",
            elevate: false,
            useShellExecute: false,
            createNoWindow: true);

        if (!started)
            return false;

        return await WaitForResultAsync();
    }

    public async Task<bool> EnsureHelperInstalledAsync()
    {
        if (await TaskExistsAsync())
            return true;

        return await CreateScheduledHelperAsync();
    }

    private async Task<bool> EnsureScheduledHelperAsync()
    {
        if (await TaskExistsAsync())
            return true;

        return await CreateScheduledHelperAsync();
    }

    private async Task<bool> TaskExistsAsync()
    {
        return await RunProcessAsync(
            "schtasks.exe",
            "/query /tn \"" + FirewallHelperTaskName + "\"",
            elevate: false,
            useShellExecute: false,
            createNoWindow: true);
    }

    private async Task<bool> CreateScheduledHelperAsync()
    {
        string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return false;

        string taskCommand =
            "schtasks /create /tn \"" + FirewallHelperTaskName + "\" " +
            "/tr \"\\\"" + exePath + "\\\" " + FirewallHelperArgument + "\" " +
            "/sc onlogon /rl highest /f";

        return await RunProcessAsync(
            "cmd.exe",
            "/c " + taskCommand,
            elevate: !IsAdministrator(),
            useShellExecute: true,
            createNoWindow: true);
    }

    private async Task<bool> WaitForResultAsync()
    {
        for (int i = 0; i < 150; i++)
        {
            if (File.Exists(ResultFilePath))
            {
                string result = File.ReadAllText(ResultFilePath, Encoding.UTF8).Trim();
                return string.Equals(result, "success", StringComparison.OrdinalIgnoreCase);
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static string BuildSetCommand()
    {
        return "netsh advfirewall firewall delete rule name=\"Catan DirectPlay TCP\" >nul 2>&1 & " +
               "netsh advfirewall firewall delete rule name=\"Catan DirectPlay UDP\" >nul 2>&1 & " +
               "netsh advfirewall firewall add rule name=\"Catan DirectPlay TCP\" dir=in action=allow protocol=TCP localport=47624 & " +
               "netsh advfirewall firewall add rule name=\"Catan DirectPlay UDP\" dir=in action=allow protocol=UDP localport=2300-2400";
    }

    private static string BuildResetCommand()
    {
        return "netsh advfirewall firewall delete rule name=\"Catan DirectPlay TCP\" >nul 2>&1 & " +
               "netsh advfirewall firewall delete rule name=\"Catan DirectPlay UDP\"";
    }

    private static bool ExecuteCommand(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process? process = Process.Start(psi);
        if (process == null)
            return false;

        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private async Task<bool> RunProcessAsync(string fileName, string arguments, bool elevate, bool useShellExecute, bool createNoWindow)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = useShellExecute,
            CreateNoWindow = createNoWindow
        };

        if (elevate)
            psi.Verb = "runas";

        using Process? process = Process.Start(psi);
        if (process == null)
            return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static void WriteResult(string result)
    {
        Directory.CreateDirectory(HelperDirectory);
        File.WriteAllText(ResultFilePath, result, Encoding.UTF8);
    }
}
