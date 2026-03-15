using System.Reflection;

namespace CatanLauncher.Services;

public static class AppVersionService
{
    public static Version GetCurrentVersion()
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

    public static string GetCurrentVersionText()
    {
        Version version = GetCurrentVersion();
        return version.Revision >= 0
            ? version.ToString(4)
            : version.Build >= 0
                ? version.ToString(3)
                : version.ToString(2);
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
}
