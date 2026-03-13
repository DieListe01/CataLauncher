using System.IO;
using CatanLauncher.Models;

namespace CatanLauncher.Services;

public sealed class LauncherConfigService
{
    public LauncherConfig Load()
    {
        string configPath = ResolveConfigPath();
        var config = new LauncherConfig { ConfigFilePath = configPath };

        if (!File.Exists(configPath))
            return config;

        string currentSection = string.Empty;
        foreach (string rawLine in File.ReadAllLines(configPath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            ApplyValue(config, currentSection, key, value);
        }

        config.CatanExePath = NormalizeExePath(config.CatanExePath, "Catan.exe");
        config.DgVoodooExePath = NormalizeExePath(config.DgVoodooExePath, "dgVoodooCpl.exe");
        config.RadminExePath = NormalizeRadminPath(config.RadminExePath);
        config.MusicVolume = Math.Clamp(config.MusicVolume, 0, 100);
        return config;
    }

    public void Save(LauncherConfig config)
    {
        string configPath = string.IsNullOrWhiteSpace(config.ConfigFilePath) ? ResolveConfigPath() : config.ConfigFilePath;
        string? directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string catanDir = GetDirectory(config.CatanExePath);
        string dgDir = GetDirectory(config.DgVoodooExePath);
        string radminDir = GetDirectory(config.RadminExePath);

        var lines = new[]
        {
            "[Catan]",
            "Pfad = " + catanDir,
            "ExePfad = " + (config.CatanExePath ?? string.Empty),
            string.Empty,
            "[dgVoodoo2]",
            "Pfad = " + dgDir,
            "ExePfad = " + (config.DgVoodooExePath ?? string.Empty),
            string.Empty,
            "[RadminVPN]",
            "ExePfad = " + (config.RadminExePath ?? string.Empty),
            "Pfad = " + radminDir,
            string.Empty,
            "[Launcher]",
            "MusikAktiv = " + config.MusicEnabled.ToString().ToLowerInvariant(),
            "MusikLautstaerke = " + Math.Clamp(config.MusicVolume, 0, 100)
        };

        File.WriteAllLines(configPath, lines);
    }

    private static void ApplyValue(LauncherConfig config, string section, string key, string value)
    {
        switch (section)
        {
            case "Catan":
                if (key.Equals("ExePfad", StringComparison.OrdinalIgnoreCase) || key.Equals("Pfad", StringComparison.OrdinalIgnoreCase))
                    config.CatanExePath = PreferBetterPath(config.CatanExePath, value);
                break;
            case "dgVoodoo2":
                if (key.Equals("ExePfad", StringComparison.OrdinalIgnoreCase) || key.Equals("Pfad", StringComparison.OrdinalIgnoreCase))
                    config.DgVoodooExePath = PreferBetterPath(config.DgVoodooExePath, value);
                break;
            case "RadminVPN":
                if (key.Equals("ExePfad", StringComparison.OrdinalIgnoreCase) || key.Equals("Pfad", StringComparison.OrdinalIgnoreCase))
                    config.RadminExePath = PreferBetterPath(config.RadminExePath, value);
                break;
            case "Launcher":
                if (key.Equals("MusikAktiv", StringComparison.OrdinalIgnoreCase))
                    config.MusicEnabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0";
                else if (key.Equals("MusikLautstaerke", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int volume))
                    config.MusicVolume = volume;
                break;
        }
    }

    private static string PreferBetterPath(string existingValue, string candidate)
    {
        if (string.IsNullOrWhiteSpace(existingValue))
            return candidate;

        if (!existingValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return candidate;

        return existingValue;
    }

    private static string NormalizeExePath(string value, string fileName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return value;

        return Path.Combine(value, fileName);
    }

    private static string NormalizeRadminPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return value;

        string path1 = Path.Combine(value, "RvRvpnGui.exe");
        if (File.Exists(path1))
            return path1;

        return Path.Combine(value, "Radmin VPN.exe");
    }

    private static string GetDirectory(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return string.Empty;

        return Path.GetDirectoryName(exePath) ?? string.Empty;
    }

    private static string ResolveConfigPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDirectory, "config.ini"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "config.ini"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
