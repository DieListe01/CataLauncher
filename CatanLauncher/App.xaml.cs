using System.Windows;
using CatanLauncher.Services;

namespace CatanLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var adminMaintenanceService = new AdminMaintenanceService();
        if (adminMaintenanceService.IsFirewallHelperMode(e.Args))
        {
            int exitCode = adminMaintenanceService.ExecuteScheduledFirewallRequest() ? 0 : 1;
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
