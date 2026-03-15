using System.IO;
using System.Text;

namespace CatanLauncher.Services;

public static class LocalTelemetryService
{
    public static void Write(string category, string message, bool enabled)
    {
        if (!enabled)
            return;

        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CatanLauncher");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "launcher.log");
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                          category + " | " + message + Environment.NewLine;
            File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch
        {
            // Logging darf nie die App beeinflussen.
        }
    }
}
