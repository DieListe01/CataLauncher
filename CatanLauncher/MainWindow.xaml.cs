using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CatanLauncher.Models;
using CatanLauncher.Services;

namespace CatanLauncher;

public partial class MainWindow : Window
{
    private enum MainTabView
    {
        Launcher,
        Installation,
        SystemInformation
    }

    private readonly LauncherConfigService configService = new();
    private readonly LauncherRuntimeService runtimeService = new();
    private readonly SystemStatusService statusService = new();
    private readonly SystemInfoService systemInfoService = new();
    private readonly AdminMaintenanceService adminMaintenanceService = new();
    private readonly GitHubReleaseUpdateService updateService = new();
    private readonly DgVoodooUpdateService dgVoodooUpdateService = new();
    private LauncherConfig currentConfig = new();
    private SystemStatusSnapshot? currentStatus;
    private bool isLoading;
    private bool firewallRulesExist;
    private string lanAdapterName = "-";
    private string vpnAdapterName = "-";
    private bool musicDeviceOpen;
    private string? launcherMusicPath;
    private bool missingMusicLogged;
    private bool startupBoundsApplied;

    private const string LauncherMusicAlias = "launcherMusic";

    public MainWindow()
    {
        InitializeComponent();
        LauncherVersionText.Text = "v" + AppVersionService.GetCurrentVersionText();
        SetActiveTab(MainTabView.Launcher);
        AdminModeBadge.Visibility = adminMaintenanceService.IsAdministrator() ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => StopLauncherMusic(true);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, System.Text.StringBuilder? returnValue, int returnLength, IntPtr winHandle);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyStartupBounds();
        await RefreshUiStateAsync();
    }

    private async void CheckLauncherVersionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = string.IsNullOrWhiteSpace(currentConfig.ConfigFilePath) ? configService.Load() : currentConfig;
            await updateService.CheckForUpdateAsync(this, config);
            WriteLog("Launcher-Version geprueft.");
        }
        catch (Exception ex)
        {
            ShowError("Version konnte nicht geprueft werden", ex.Message);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ApplyStartupBounds()
    {
        if (startupBoundsApplied)
            return;

        Rect workArea = SystemParameters.WorkArea;
        const double margin = 8;

        Width = Math.Max(MinWidth, workArea.Width - (margin * 2));
        Height = Math.Max(MinHeight, workArea.Height - (margin * 2));
        Left = workArea.Left + margin;
        Top = workArea.Top + margin;
        startupBoundsApplied = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void LauncherTabButton_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(MainTabView.Launcher);
    }

    private void InstallationTabButton_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(MainTabView.Installation);
    }

    private void SystemInformationTabButton_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(MainTabView.SystemInformation);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUiStateAsync();
        WriteLog("Status aktualisiert.");
    }

    private async void FirewallToggleAction_Click(object sender, RoutedEventArgs e)
    {
        if (!firewallRulesExist && !adminMaintenanceService.IsAdministrator())
        {
            WriteLog("Einmalige Adminfreigabe fuer Firewall-Helper erforderlich...");
            bool helperInstalled = await adminMaintenanceService.EnsureHelperInstalledAsync();
            if (!helperInstalled)
            {
                WriteLog("Firewall-Helper konnte nicht eingerichtet werden.");
                return;
            }

            WriteLog("Firewall-Helper erfolgreich eingerichtet.");
        }

        if (firewallRulesExist)
        {
            await RunMaintenanceActionAsync(
                "Portfreigaben werden entfernt...",
                "Portfreigaben entfernt.",
                "Portfreigaben konnten nicht entfernt werden.",
                () => adminMaintenanceService.ResetFirewallRulesAsync());
            return;
        }

        await RunMaintenanceActionAsync(
            "Ports werden freigegeben...",
            "Ports freigegeben.",
            "Ports konnten nicht freigegeben werden.",
            () => adminMaintenanceService.SetFirewallRulesAsync());
    }

    private async void StartFullButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureExists(currentConfig.CatanExePath, "Catan.exe");
            runtimeService.StartFull(currentConfig.CatanExePath, currentConfig.DgVoodooExePath, currentConfig.RadminExePath);
            WriteLog("WPF-Prototyp startet Radmin, dgVoodoo2 und Catan.");
            await Task.Delay(500);
            UpdateProcessButtons();
            UpdateInstallationCards();
        }
        catch (Exception ex)
        {
            ShowError("Komplettstart fehlgeschlagen", ex.Message);
        }
    }

    private async void StartCatanOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureExists(currentConfig.CatanExePath, "Catan.exe");
            runtimeService.StartFile(currentConfig.CatanExePath);
            WriteLog("Catan wurde gestartet.");
            await Task.Delay(500);
            UpdateInstallationCards();
        }
        catch (Exception ex)
        {
            ShowError("Catan konnte nicht gestartet werden", ex.Message);
        }
    }

    private async void OpenRadminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureExists(currentConfig.RadminExePath, "Radmin VPN");
            runtimeService.StartFile(currentConfig.RadminExePath);
            WriteLog("Radmin VPN GUI geoeffnet.");
            await Task.Delay(500);
            await RefreshUiStateAsync();
        }
        catch (Exception ex)
        {
            ShowError("Radmin konnte nicht geoeffnet werden", ex.Message);
        }
    }

    private async void OpenDgVoodooButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureExists(currentConfig.DgVoodooExePath, "dgVoodooCpl.exe");
            runtimeService.StartFile(currentConfig.DgVoodooExePath);
            WriteLog("dgVoodoo2 GUI geoeffnet.");
            await Task.Delay(500);
            await RefreshUiStateAsync();
        }
        catch (Exception ex)
        {
            ShowError("dgVoodoo2 konnte nicht geoeffnet werden", ex.Message);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(currentConfig)
        {
            Owner = this
        };

        bool? result = settingsWindow.ShowDialog();
        if (result != true)
            return;

        currentConfig = settingsWindow.Config;
        configService.Save(currentConfig);
        await RefreshUiStateAsync();
        WriteLog("Einstellungen gespeichert.");
    }

    private void MusicToggleButton_Click(object sender, RoutedEventArgs e)
    {
        currentConfig.MusicEnabled = !currentConfig.MusicEnabled;
        configService.Save(currentConfig);
        UpdateMusicUi();
        ApplyMusicPlayback();
        WriteLog(currentConfig.MusicEnabled ? "Musik in der config.ini aktiviert." : "Musik in der config.ini deaktiviert.");
    }

    private void MusicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isLoading)
            return;

        currentConfig.MusicVolume = (int)Math.Round(e.NewValue);
        configService.Save(currentConfig);
        UpdateMusicUi();
        ApplyMusicPlayback();
    }

    private async Task RefreshUiStateAsync()
    {
        isLoading = true;
        currentStatus = null;
        BeginLoadingIndicators();

        SetConfigProgress("Lade Konfiguration...");
        await WaitForUiAsync();
        currentConfig = await Task.Run(() => configService.Load());

        SetFieldValue(CatanPathText, CatanPathSpinner, DisplayPath(currentConfig.CatanExePath));
        SetFieldValue(DgVoodooPathText, DgVoodooPathSpinner, DisplayPath(currentConfig.DgVoodooExePath));
        SetFieldValue(RadminPathText, RadminPathSpinner, DisplayPath(currentConfig.RadminExePath));
        UpdateInstallationCards();

        SetNetworkProgress("Ermittle LAN-IP...");
        await WaitForUiAsync();
        var lanInfo = await Task.Run(() => (Ip: runtimeService.GetLanIp(), AdapterName: runtimeService.GetLanAdapterName()));
        lanAdapterName = lanInfo.AdapterName;
        SetFieldValue(LanIpText, LanSpinner, lanInfo.Ip);

        SetNetworkProgress("Ermittle VPN-IP...");
        await WaitForUiAsync();
        var vpnInfo = await Task.Run(() => (Ip: runtimeService.GetVpnIp(), AdapterName: runtimeService.GetVpnAdapterName()));
        vpnAdapterName = vpnInfo.AdapterName;
        SetFieldValue(VpnIpText, VpnSpinner, vpnInfo.Ip);
        SetNetworkProgress(string.Empty);
        UpdateSystemInfoItems();

        MusicSlider.Value = currentConfig.MusicVolume;
        UpdateMusicUi();
        EnsureMusicLoaded();
        ApplyMusicPlayback();
        UpdateProcessButtons();

        SetConfigProgress("Pruefe Firewall und Online-Versionen...");
        WriteLog("Pruefe Firewall und Versionen...");
        await WaitForUiAsync();
        SystemStatusSnapshot status = await statusService.GetStatusAsync(currentConfig);
        currentStatus = status;
        ApplySystemStatus(status);
        UpdateSystemInfoItems();
        SetConfigProgress(string.Empty);

        isLoading = false;

        WriteLog("Konfiguration geladen: " + currentConfig.ConfigFilePath);
    }

    private void SetActiveTab(MainTabView view)
    {
        bool launcherView = view == MainTabView.Launcher;
        bool installationView = view == MainTabView.Installation;
        bool systemInfoView = view == MainTabView.SystemInformation;

        LauncherTabButton.Style = (Style)FindResource(launcherView ? "TopNavActiveButtonStyle" : "TopNavButtonStyle");
        InstallationTabButton.Style = (Style)FindResource(installationView ? "TopNavActiveButtonStyle" : "TopNavButtonStyle");
        SystemInformationTabButton.Style = (Style)FindResource(systemInfoView ? "TopNavActiveButtonStyle" : "TopNavButtonStyle");

        NetworkCard.Visibility = launcherView ? Visibility.Visible : Visibility.Collapsed;
        ActionsCard.Visibility = launcherView ? Visibility.Visible : Visibility.Collapsed;
        ConfigStatusCard.Visibility = launcherView ? Visibility.Visible : Visibility.Collapsed;
        InstallationCard.Visibility = installationView ? Visibility.Visible : Visibility.Collapsed;
        SystemInfoCard.Visibility = systemInfoView ? Visibility.Visible : Visibility.Collapsed;
        LogCard.Visibility = launcherView ? Visibility.Visible : Visibility.Collapsed;

        Grid.SetColumnSpan(LeftContentGrid, launcherView ? 1 : 2);

        if (systemInfoView)
            UpdateSystemInfoItems();

        if (installationView)
            UpdateInstallationCards();
    }

    private void UpdateSystemInfoItems()
    {
        SystemInfoItemsControl.ItemsSource = systemInfoService.GetItems(
            LanIpText.Text,
            VpnIpText.Text,
            lanAdapterName,
            vpnAdapterName,
            adminMaintenanceService.IsAdministrator(),
            currentConfig.CatanExePath,
            currentStatus);
    }

    private void UpdateInstallationCards()
    {
        UpdateInstallationCatanCard();

        UpdateInstallationToolCard(
            InstallationRadminTopBadge,
            InstallationRadminTopBadgeText,
            InstallationRadminPathText,
            InstallationRadminVersionText,
            InstallationRadminVersionBadge,
            InstallationRadminVersionBadgeText,
            InstallationRadminHintText,
            InstallationRadminOpenButton,
            InstallationRadminUpdateButton,
            currentConfig.RadminExePath,
            currentStatus?.RadminInstalledVersion,
            currentStatus?.RadminLatestVersion,
            currentStatus?.RadminUpdateAvailable ?? false,
            "Radmin VPN",
            "Radmin aktiv",
            "Radmin oeffnen",
            true);

        UpdateInstallationToolCard(
            InstallationDgVoodooTopBadge,
            InstallationDgVoodooTopBadgeText,
            InstallationDgVoodooPathText,
            InstallationDgVoodooVersionText,
            InstallationDgVoodooVersionBadge,
            InstallationDgVoodooVersionBadgeText,
            InstallationDgVoodooHintText,
            InstallationDgVoodooOpenButton,
            InstallationDgVoodooUpdateButton,
            currentConfig.DgVoodooExePath,
            currentStatus?.DgVoodooInstalledVersion,
            currentStatus?.DgVoodooLatestVersion,
            currentStatus?.DgVoodooUpdateAvailable ?? false,
            "dgVoodoo2",
            "dgVoodoo2 aktiv",
            "Tool oeffnen",
            false);
    }

    private void UpdateInstallationCatanCard()
    {
        bool hasConfiguredPath = !string.IsNullOrWhiteSpace(currentConfig.CatanExePath);
        bool pathExists = hasConfiguredPath && File.Exists(currentConfig.CatanExePath);
        bool isRunning = pathExists && runtimeService.IsProcessRunning(currentConfig.CatanExePath);

        InstallationCatanPathText.Text = DisplayPath(currentConfig.CatanExePath);
        ConfigureInstallationStartButton(InstallationCatanStartButton, pathExists, isRunning);

        if (!pathExists)
        {
            ApplyInlineBadge(InstallationCatanTopBadge, InstallationCatanTopBadgeText, hasConfiguredPath ? "Pfad" : "Fehlt", "warning");
            ApplyInlineBadge(InstallationCatanStatusBadge, InstallationCatanStatusBadgeText, "Fehlt", "warning");
            InstallationCatanStatusText.Text = hasConfiguredPath
                ? "Catan.exe wurde unter diesem Pfad nicht gefunden"
                : "kein Spielpfad konfiguriert";
            InstallationCatanHintText.Text = hasConfiguredPath
                ? "Der eingetragene Catan-Pfad ist nicht mehr gueltig. Bitte in den Einstellungen eine gueltige Catan.exe auswaehlen."
                : "Bitte zuerst in den Einstellungen den Pfad zur Catan.exe hinterlegen, damit der Launcher das Spiel starten kann.";
            return;
        }

        ApplyInlineBadge(InstallationCatanTopBadge, InstallationCatanTopBadgeText, isRunning ? "Aktiv" : "Bereit", "ok");
        ApplyInlineBadge(InstallationCatanStatusBadge, InstallationCatanStatusBadgeText, isRunning ? "Aktiv" : "Startklar", "ok");
        InstallationCatanStatusText.Text = isRunning
            ? "lokale Installation erkannt | Prozess laeuft bereits"
            : "lokale Installation erkannt | Start direkt moeglich";
        InstallationCatanHintText.Text = isRunning
            ? "Catan ist bereits gestartet. Du kannst den Pfad trotzdem jederzeit in den Einstellungen anpassen."
            : "Catan ist korrekt hinterlegt und kann direkt gestartet werden - allein oder im Komplettstart mit den anderen Tools.";
    }

    private void UpdateInstallationToolCard(
        Border topBadge,
        TextBlock topBadgeText,
        TextBlock pathText,
        TextBlock versionText,
        Border versionBadge,
        TextBlock versionBadgeText,
        TextBlock hintText,
        Button openButton,
        Button updateButton,
        string configuredPath,
        string? installedVersion,
        string? latestVersion,
        bool updateAvailable,
        string toolName,
        string activeButtonLabel,
        string idleButtonLabel,
        bool optionalTool)
    {
        bool hasConfiguredPath = !string.IsNullOrWhiteSpace(configuredPath);
        bool pathExists = hasConfiguredPath && File.Exists(configuredPath);
        bool hasStatus = currentStatus != null;
        bool isRunning = pathExists && runtimeService.IsProcessRunning(configuredPath);

        pathText.Text = DisplayPath(configuredPath);
        ConfigureInstallationOpenButton(openButton, pathExists, isRunning, toolName, activeButtonLabel, idleButtonLabel);

        if (!hasStatus)
        {
            versionText.Text = "installiert: pruefe... | online: pruefe...";
            ApplyInlineBadge(versionBadge, versionBadgeText, "Pruefung", "neutral");
            updateButton.Visibility = Visibility.Collapsed;
            updateButton.ToolTip = null;

            if (!pathExists)
            {
                if (optionalTool && !hasConfiguredPath)
                {
                    ApplyInlineBadge(topBadge, topBadgeText, "Optional", "neutral");
                    hintText.Text = toolName + " ist noch nicht hinterlegt. Fuer Internet-Matches kannst du es spaeter nachtragen.";
                }
                else
                {
                    ApplyInlineBadge(topBadge, topBadgeText, "Fehlt", "warning");
                    hintText.Text = hasConfiguredPath
                        ? "Der konfigurierte Pfad wurde nicht gefunden. Bitte Pfad oder Installation pruefen."
                        : toolName + " ist noch nicht eingerichtet. Website oder Download verwenden.";
                }

                return;
            }

            ApplyInlineBadge(topBadge, topBadgeText, "Pruefung", "neutral");
            hintText.Text = "Lokale Installation erkannt, Online-Version wird gerade geprueft.";
            return;
        }

        string installed = installedVersion ?? "unbekannt";
        string latest = latestVersion ?? "online nicht pruefbar";

        versionText.Text = "installiert: " + installed + " | online: " + latest;
        ApplyVersionStatusBadge(versionBadge, versionBadgeText, installed, latest, updateAvailable);

        bool needsDownload = !pathExists || installed == "nicht installiert";
        bool needsAction = needsDownload || updateAvailable;
        updateButton.Visibility = needsAction ? Visibility.Visible : Visibility.Collapsed;
        updateButton.Content = needsDownload ? "Jetzt laden" : "Update holen";
        updateButton.ToolTip = !needsAction
            ? null
            : needsDownload
                ? "Oeffnet die Download-Seite fuer " + toolName + "."
                : "Oeffnet die Update-Seite fuer " + toolName + ".";

        if (!pathExists)
        {
            if (optionalTool && !hasConfiguredPath)
            {
                ApplyInlineBadge(topBadge, topBadgeText, "Optional", "neutral");
                hintText.Text = toolName + " ist optional, fuer Online-Matches aber sehr hilfreich. Mit 'Jetzt laden' kommst du direkt zur Website.";
            }
            else
            {
                ApplyInlineBadge(topBadge, topBadgeText, hasConfiguredPath ? "Pfad" : "Fehlt", "warning");
                hintText.Text = hasConfiguredPath
                    ? "Der eingetragene Pfad ist nicht mehr gueltig. Bitte Datei neu waehlen oder Tool neu installieren."
                    : toolName + " wurde noch nicht eingerichtet. Website oder Download verwenden.";
            }

            return;
        }

        if (installed == "unbekannt" || latest == "online nicht pruefbar")
        {
            ApplyInlineBadge(topBadge, topBadgeText, "Unklar", "neutral");
            hintText.Text = "Die lokale Installation wurde erkannt, aber die Online-Version konnte gerade nicht sicher geprueft werden.";
            return;
        }

        if (updateAvailable)
        {
            ApplyInlineBadge(topBadge, topBadgeText, "Update", "warning");
            hintText.Text = "Online ist bereits eine neuere Version verfuegbar. Mit 'Update holen' kommst du direkt zur passenden Seite.";
            return;
        }

        ApplyInlineBadge(topBadge, topBadgeText, isRunning ? "Aktiv" : "Bereit", "ok");
        hintText.Text = isRunning
            ? toolName + " ist aktuell gestartet und einsatzbereit."
            : toolName + " ist aktuell installiert und direkt startklar.";
    }

    private void ConfigureInstallationOpenButton(Button button, bool canOpen, bool isRunning, string toolName, string activeLabel, string idleLabel)
    {
        button.IsEnabled = canOpen;
        button.Opacity = canOpen ? 1 : 0.58;
        button.Content = isRunning ? activeLabel : idleLabel;
        button.Style = (Style)FindResource(isRunning ? "MiniSuccessButtonStyle" : "MicroButtonStyle");
        button.ToolTip = canOpen
            ? toolName + " direkt oeffnen."
            : toolName + " ist noch nicht lokal verfuegbar oder nicht korrekt konfiguriert.";
    }

    private void ConfigureInstallationStartButton(Button button, bool canStart, bool isRunning)
    {
        button.IsEnabled = canStart;
        button.Opacity = canStart ? 1 : 0.58;
        button.Content = isRunning ? "Spiel aktiv" : "Spiel starten";
        button.Style = (Style)FindResource(isRunning ? "MiniSuccessButtonStyle" : "MiniPrimaryButtonStyle");
        button.ToolTip = canStart
            ? "Startet Catan direkt ohne den Komplettablauf."
            : "Catan.exe ist noch nicht verfuegbar oder nicht korrekt konfiguriert.";
    }

    private void OpenRadminWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.radmin-vpn.com/de/");
    }

    private void OpenDgVoodooWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://dege.freeweb.hu/dgVoodoo2/dgVoodoo2/");
    }

    private void OpenWindowsUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("ms-settings:windowsupdate");
    }

    private void OpenNvidiaDriverButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentStatus?.NvidiaDriverPageUrl))
            return;

        OpenUrl(currentStatus.NvidiaDriverPageUrl);
    }

    private void SystemInfoItemActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SystemInfoItem item })
            return;

        if (item.ActionId == "nvidia-driver-page")
            OpenNvidiaDriverButton_Click(sender, e);
        else if (item.ActionId == "windows-update-settings")
            OpenUrl("ms-settings:windowsupdate");
    }

    private async void CopyLanIpButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyFieldValueAsync(CopyLanIpButton, LanIpText.Text, "LAN-IP");
    }

    private async void CopyVpnIpButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyFieldValueAsync(CopyVpnIpButton, VpnIpText.Text, "VPN-IP");
    }

    private async void LanIpText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await CopyFieldValueAsync(CopyLanIpButton, LanIpText.Text, "LAN-IP");
    }

    private async void VpnIpText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await CopyFieldValueAsync(CopyVpnIpButton, VpnIpText.Text, "VPN-IP");
    }

    private void ApplySystemStatus(SystemStatusSnapshot status)
    {
        firewallRulesExist = status.FirewallRulesExist;
        FirewallBadgeText.Text = firewallRulesExist ? "Ports freigegeben" : "Ports nicht freigegeben";
        FirewallActionButton.Style = (Style)FindResource(firewallRulesExist ? "MiniSuccessButtonStyle" : "MiniPrimaryButtonStyle");
        FirewallActionButton.Content = firewallRulesExist ? "Ports sperren" : "Ports freigeben";
        FirewallActionButton.ToolTip = firewallRulesExist
            ? "Klick entfernt die Portfreigaben wieder."
            : "Klick legt die benoetigten Portfreigaben an.";

        DgVoodooVersionStatusText.Text = "installiert: " + status.DgVoodooInstalledVersion + " | online: " + status.DgVoodooLatestVersion;
        RadminVersionStatusText.Text = "installiert: " + status.RadminInstalledVersion + " | online: " + status.RadminLatestVersion;
        DgVoodooVersionSpinner.Visibility = Visibility.Collapsed;
        RadminVersionSpinner.Visibility = Visibility.Collapsed;

        ApplyInlineBadge(LanStatusBadge, LanStatusBadgeText, GetLanBadgeText(LanIpText.Text), GetLanBadgeKind(LanIpText.Text));
        ApplyVersionStatusBadge(DgVoodooVersionBadge, DgVoodooVersionBadgeText, status.DgVoodooInstalledVersion, status.DgVoodooLatestVersion, status.DgVoodooUpdateAvailable);
        ApplyVersionStatusBadge(RadminVersionBadge, RadminVersionBadgeText, status.RadminInstalledVersion, status.RadminLatestVersion, status.RadminUpdateAvailable);
        ApplyWindowsUpdateStatus(status);
        UpdateInstallationTabBadge(status);
        UpdateSystemInformationTabBadge(status);

        bool dgVoodooNeedsAction = status.DgVoodooUpdateAvailable || status.DgVoodooInstalledVersion == "nicht installiert";
        bool radminNeedsAction = status.RadminUpdateAvailable || status.RadminInstalledVersion == "nicht installiert";

        DgVoodooPatchButton.Visibility = dgVoodooNeedsAction ? Visibility.Visible : Visibility.Collapsed;
        RadminPatchButton.Visibility = radminNeedsAction ? Visibility.Visible : Visibility.Collapsed;

        Grid.SetColumnSpan(DgVoodooVersionBorder, dgVoodooNeedsAction ? 1 : 2);
        Grid.SetColumnSpan(RadminVersionBorder, radminNeedsAction ? 1 : 2);

        UpdateInstallationCards();
    }

    private void UpdateProcessButtons()
    {
        bool radminRunning = runtimeService.IsProcessRunning(currentConfig.RadminExePath);
        bool dgVoodooRunning = runtimeService.IsProcessRunning(currentConfig.DgVoodooExePath);

        if (RadminStateButton != null)
        {
            RadminStateButton.Style = (Style)FindResource(radminRunning ? "MiniSuccessButtonStyle" : "MiniSecondaryButtonStyle");
            RadminStateButton.Content = radminRunning ? "Radmin aktiv" : "Radmin oeffnen";
        }

        if (DgVoodooStateButton != null)
        {
            DgVoodooStateButton.Style = (Style)FindResource(dgVoodooRunning ? "MiniSuccessButtonStyle" : "MiniSecondaryButtonStyle");
            DgVoodooStateButton.Content = dgVoodooRunning ? "dgVoodoo2 aktiv" : "dgVoodoo2 oeffnen";
        }
    }

    private async void DgVoodooPatchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentConfig.DgVoodooExePath) || !File.Exists(currentConfig.DgVoodooExePath))
            {
                ShowError("dgVoodoo2-Update nicht moeglich", "Die konfigurierte dgVoodooCpl.exe wurde nicht gefunden. Bitte Pfad in den Einstellungen pruefen.");
                return;
            }

            string targetDirectory = Path.GetDirectoryName(currentConfig.DgVoodooExePath) ?? string.Empty;
            MessageBoxResult confirm = MessageBox.Show(
                this,
                "Die neueste dgVoodoo2-Version wird heruntergeladen und nach\n" + targetDirectory + "\nkopiert (Dateien werden ueberschrieben).\n\nFortfahren?",
                "dgVoodoo2 aktualisieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            InstallationDgVoodooUpdateButton.IsEnabled = false;
            WriteLog("Lade neueste dgVoodoo2-Version herunter...");
            string installedVersion = await dgVoodooUpdateService.InstallLatestAsync(currentConfig.DgVoodooExePath);
            WriteLog("dgVoodoo2 erfolgreich aktualisiert auf Version " + installedVersion + ".");
            await RefreshUiStateAsync();
        }
        catch (Exception ex)
        {
            ShowError("dgVoodoo2-Update fehlgeschlagen", ex.Message);
        }
        finally
        {
            InstallationDgVoodooUpdateButton.IsEnabled = true;
        }
    }

    private void RadminPatchButton_Click(object sender, RoutedEventArgs e)
    {
        WriteLog("Oeffne Radmin VPN Download-Seite...");
        OpenUrl("https://www.radmin-vpn.com/de/");
    }

    private async Task RunMaintenanceActionAsync(string startMessage, string successMessage, string errorMessage, Func<Task<bool>> action)
    {
        try
        {
            WriteLog(startMessage);
            bool success = await action();
            if (success)
            {
                WriteLog(successMessage);
                await RefreshUiStateAsync();
                return;
            }

            WriteLog(errorMessage);
            WriteLog("Hinweis: Zum Aendern der Firewall-Regeln muss die UAC-Abfrage bestaetigt werden.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            WriteLog("Adminfreigabe abgebrochen.");
        }
        catch (Exception ex)
        {
            WriteLog(errorMessage + " " + ex.Message);
        }
    }

    private void UpdateMusicUi()
    {
        MusicPercentText.Text = currentConfig.MusicVolume + " %";
        MusicToggleButton.Content = currentConfig.MusicEnabled ? ")))" : "x";
        MusicToggleButton.Background = currentConfig.MusicEnabled
            ? (System.Windows.Media.Brush)FindResource("Brush.BadgeOk")
            : (System.Windows.Media.Brush)FindResource("Brush.BadgeWarning");
        MusicToggleButton.Foreground = currentConfig.MusicEnabled
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 105, 59))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(134, 93, 28));
    }

    private void BeginLoadingIndicators()
    {
        SetFieldLoading(CatanPathText, CatanPathSpinner, "lade Catan-Pfad...");
        SetFieldLoading(DgVoodooPathText, DgVoodooPathSpinner, "lade dgVoodoo2-Pfad...");
        SetFieldLoading(RadminPathText, RadminPathSpinner, "lade Radmin-Pfad...");
        SetFieldLoading(LanIpText, LanSpinner, "ermittle...");
        SetFieldLoading(VpnIpText, VpnSpinner, "ermittle...");
        SetFieldLoading(DgVoodooVersionStatusText, DgVoodooVersionSpinner, "installiert: pruefe... | online: pruefe...");
        SetFieldLoading(RadminVersionStatusText, RadminVersionSpinner, "installiert: pruefe... | online: pruefe...");
        WindowsUpdateStatusText.Text = "wird geprueft...";
        InstallationCatanPathText.Text = "lade...";
        InstallationCatanStatusText.Text = "lokale Installation wird geprueft...";
        InstallationCatanHintText.Text = "Lokale Installation wird geprueft...";
        InstallationRadminPathText.Text = "lade...";
        InstallationDgVoodooPathText.Text = "lade...";
        InstallationRadminVersionText.Text = "installiert: pruefe... | online: pruefe...";
        InstallationDgVoodooVersionText.Text = "installiert: pruefe... | online: pruefe...";
        InstallationRadminHintText.Text = "Lokale Installation wird geprueft...";
        InstallationDgVoodooHintText.Text = "Lokale Installation wird geprueft...";
        HideInlineBadge(InstallationCatanTopBadge, InstallationCatanTopBadgeText);
        HideInlineBadge(InstallationCatanStatusBadge, InstallationCatanStatusBadgeText);
        HideInlineBadge(InstallationRadminTopBadge, InstallationRadminTopBadgeText);
        HideInlineBadge(InstallationDgVoodooTopBadge, InstallationDgVoodooTopBadgeText);
        HideInlineBadge(InstallationRadminVersionBadge, InstallationRadminVersionBadgeText);
        HideInlineBadge(InstallationDgVoodooVersionBadge, InstallationDgVoodooVersionBadgeText);
        HideInlineBadge(WindowsUpdateBadge, WindowsUpdateBadgeText);
        ConfigureInstallationStartButton(InstallationCatanStartButton, false, false);
        ConfigureInstallationOpenButton(InstallationRadminOpenButton, false, false, "Radmin VPN", "Radmin aktiv", "Radmin oeffnen");
        ConfigureInstallationOpenButton(InstallationDgVoodooOpenButton, false, false, "dgVoodoo2", "dgVoodoo2 aktiv", "Tool oeffnen");
        InstallationRadminUpdateButton.Visibility = Visibility.Collapsed;
        InstallationDgVoodooUpdateButton.Visibility = Visibility.Collapsed;
        HideInlineBadge(LanStatusBadge, LanStatusBadgeText);
        HideInlineBadge(VpnStatusBadge, VpnStatusBadgeText);
        HideInlineBadge(DgVoodooVersionBadge, DgVoodooVersionBadgeText);
        HideInlineBadge(RadminVersionBadge, RadminVersionBadgeText);
        HideInlineBadge(InstallationTabBadge, InstallationTabBadgeText);
        HideInlineBadge(SystemInformationTabBadge, SystemInformationTabBadgeText);
        ResetCopyButton(CopyLanIpButton);
        ResetCopyButton(CopyVpnIpButton);
        InstallationTabButton.ToolTip = null;
        SystemInformationTabButton.ToolTip = null;
        WindowsUpdateActionButton.Content = "Oeffnen";
        WindowsUpdateActionButton.Style = (Style)FindResource("MicroButtonStyle");
        WindowsUpdateActionButton.ToolTip = "Oeffnet die Windows-Update-Einstellungen.";
    }

    private void UpdateInstallationTabBadge(SystemStatusSnapshot status)
    {
        int updateCount = 0;

        if (status.DgVoodooUpdateAvailable)
            updateCount++;

        if (status.RadminUpdateAvailable)
            updateCount++;

        if (updateCount <= 0)
        {
            HideInlineBadge(InstallationTabBadge, InstallationTabBadgeText);
            InstallationTabButton.ToolTip = null;
            return;
        }

        string text = updateCount > 9 ? "9+" : updateCount.ToString();
        ApplyInlineBadge(InstallationTabBadge, InstallationTabBadgeText, text, "warning");
        InstallationTabButton.ToolTip = updateCount == 1
            ? "Im Tab Installation ist 1 Tool-Update verfuegbar."
            : "Im Tab Installation sind " + updateCount + " Tool-Updates verfuegbar.";
    }

    private void UpdateSystemInformationTabBadge(SystemStatusSnapshot status)
    {
        int updateCount = 0;

        if (status.NvidiaUpdateAvailable)
            updateCount++;

        if (status.WindowsUpdateCheckSucceeded && status.WindowsUpdatePendingCount > 0)
            updateCount++;

        if (updateCount <= 0)
        {
            HideInlineBadge(SystemInformationTabBadge, SystemInformationTabBadgeText);
            SystemInformationTabButton.ToolTip = null;
            return;
        }

        string text = updateCount > 9 ? "9+" : updateCount.ToString();
        ApplyInlineBadge(SystemInformationTabBadge, SystemInformationTabBadgeText, text, "warning");
        SystemInformationTabButton.ToolTip = updateCount == 1
            ? "Im Tab Systeminformation ist 1 Update-Hinweis verfuegbar."
            : "Im Tab Systeminformation sind " + updateCount + " Update-Hinweise verfuegbar.";
    }

    private void ApplyWindowsUpdateStatus(SystemStatusSnapshot status)
    {
        string suffix = string.IsNullOrWhiteSpace(status.WindowsUpdateLastCheckedText)
            ? string.Empty
            : " | " + status.WindowsUpdateLastCheckedText;

        if (!status.WindowsUpdateCheckSucceeded)
        {
            WindowsUpdateStatusText.Text = "Status nicht pruefbar" + suffix;
            ApplyInlineBadge(WindowsUpdateBadge, WindowsUpdateBadgeText, "Unklar", "neutral");
            WindowsUpdateActionButton.Content = "Oeffnen";
            WindowsUpdateActionButton.Style = (Style)FindResource("MicroButtonStyle");
            WindowsUpdateActionButton.ToolTip = "Oeffnet die Windows-Update-Einstellungen.";
            return;
        }

        if (status.WindowsUpdatePendingCount > 0)
        {
            WindowsUpdateStatusText.Text = (status.WindowsUpdatePendingCount == 1 ? "1 Update verfuegbar" : status.WindowsUpdatePendingCount + " Updates verfuegbar") + suffix;
            ApplyInlineBadge(WindowsUpdateBadge, WindowsUpdateBadgeText, "Update", "warning");
            WindowsUpdateActionButton.Content = "Updates";
            WindowsUpdateActionButton.Style = (Style)FindResource("MiniPrimaryButtonStyle");
            WindowsUpdateActionButton.ToolTip = "Oeffnet Windows Update mit den verfuegbaren Updates.";
            return;
        }

        WindowsUpdateStatusText.Text = "keine Updates verfuegbar" + suffix;
        ApplyInlineBadge(WindowsUpdateBadge, WindowsUpdateBadgeText, "Aktuell", "ok");
        WindowsUpdateActionButton.Content = "Oeffnen";
        WindowsUpdateActionButton.Style = (Style)FindResource("MicroButtonStyle");
        WindowsUpdateActionButton.ToolTip = "Oeffnet die Windows-Update-Einstellungen.";
    }

    private void ApplyInlineBadge(Border border, TextBlock textBlock, string text, string kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            HideInlineBadge(border, textBlock);
            return;
        }

        border.Visibility = Visibility.Visible;
        textBlock.Text = text;

        switch (kind)
        {
            case "ok":
                border.Background = (Brush)FindResource("Brush.BadgeOk");
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(125, 154, 98));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(72, 98, 49));
                break;
            case "warning":
                border.Background = (Brush)FindResource("Brush.BadgeWarning");
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 146, 61));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(122, 83, 31));
                break;
            default:
                border.Background = (Brush)FindResource("Brush.BadgeNeutral");
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(199, 172, 132));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(106, 77, 44));
                break;
        }
    }

    private static void HideInlineBadge(Border border, TextBlock textBlock)
    {
        border.Visibility = Visibility.Collapsed;
        textBlock.Text = string.Empty;
    }

    private void ApplyVersionStatusBadge(Border border, TextBlock textBlock, string installedVersion, string latestVersion, bool updateAvailable)
    {
        if (latestVersion == "online nicht pruefbar")
        {
            ApplyInlineBadge(border, textBlock, "Unklar", "neutral");
            return;
        }

        if (installedVersion == "nicht installiert")
        {
            ApplyInlineBadge(border, textBlock, "Fehlt", "warning");
            return;
        }

        if (installedVersion == "unbekannt")
        {
            ApplyInlineBadge(border, textBlock, "Unklar", "neutral");
            return;
        }

        ApplyInlineBadge(border, textBlock, updateAvailable ? "Update" : "Aktuell", updateAvailable ? "warning" : "ok");
    }

    private static string GetLanBadgeText(string lanIp)
    {
        if (string.IsNullOrWhiteSpace(lanIp) || lanIp == "-" || lanIp.EndsWith("...", StringComparison.Ordinal))
            return string.Empty;

        return lanIp.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase) ? "Offline" : "Verfuegbar";
    }

    private static string GetLanBadgeKind(string lanIp)
    {
        if (string.IsNullOrWhiteSpace(lanIp) || lanIp == "-" || lanIp.EndsWith("...", StringComparison.Ordinal))
            return "neutral";

        return lanIp.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase) ? "warning" : "ok";
    }

    private static string GetVpnBadgeText(string vpnIp)
    {
        if (string.IsNullOrWhiteSpace(vpnIp) || vpnIp == "-" || vpnIp.EndsWith("...", StringComparison.Ordinal))
            return string.Empty;

        return vpnIp.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase) ? "Offline" : "Verbunden";
    }

    private static string GetVpnBadgeKind(string vpnIp)
    {
        if (string.IsNullOrWhiteSpace(vpnIp) || vpnIp == "-" || vpnIp.EndsWith("...", StringComparison.Ordinal))
            return "neutral";

        return vpnIp.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase) ? "warning" : "ok";
    }

    private async Task CopyFieldValueAsync(Button button, string value, string label)
    {
        if (!IsCopyableValue(value))
        {
            WriteLog(label + " konnte nicht kopiert werden: kein gueltiger Wert vorhanden.");
            return;
        }

        try
        {
            Clipboard.SetText(value.Trim());
            WriteLog(label + " in die Zwischenablage kopiert.");
            await FlashCopyButtonAsync(button);
        }
        catch (Exception ex)
        {
            WriteLog(label + " konnte nicht kopiert werden. " + ex.Message);
        }
    }

    private async Task FlashCopyButtonAsync(Button button)
    {
        object originalContent = button.Content;
        button.Content = "Kopiert";

        try
        {
            await Task.Delay(1200);
        }
        finally
        {
            button.Content = originalContent;
        }
    }

    private void ResetCopyButton(Button button)
    {
        button.Content = "Kopieren";
        button.IsEnabled = false;
    }

    private void UpdateCopyButtonState(Button button, string value)
    {
        button.Content = "Kopieren";
        button.IsEnabled = IsCopyableValue(value);
    }

    private static bool IsCopyableValue(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value != "-" &&
               !value.EndsWith("...", StringComparison.Ordinal) &&
               !value.Equals("nicht gefunden", StringComparison.OrdinalIgnoreCase);
    }

    private void SetFieldLoading(TextBlock target, FrameworkElement spinner, string placeholder)
    {
        target.Text = placeholder;
        spinner.Visibility = Visibility.Visible;
    }

    private void SetFieldValue(TextBlock target, FrameworkElement spinner, string value)
    {
        target.Text = value;
        spinner.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(target, LanIpText))
        {
            UpdateCopyButtonState(CopyLanIpButton, value);
            ApplyInlineBadge(LanStatusBadge, LanStatusBadgeText, GetLanBadgeText(value), GetLanBadgeKind(value));
        }
        else if (ReferenceEquals(target, VpnIpText))
        {
            UpdateCopyButtonState(CopyVpnIpButton, value);
            ApplyInlineBadge(VpnStatusBadge, VpnStatusBadgeText, GetVpnBadgeText(value), GetVpnBadgeKind(value));
        }
    }

    private void SetNetworkProgress(string message)
    {
        NetworkProgressText.Text = message;
        NetworkProgressText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetConfigProgress(string message)
    {
        ConfigProgressText.Text = message;
        ConfigProgressText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task WaitForUiAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Delay(60);
    }

    private void EnsureMusicLoaded()
    {
        if (musicDeviceOpen)
            return;

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "musik.mp3"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "musik.mp3")
        };

        launcherMusicPath = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(launcherMusicPath))
        {
            MusicToggleButton.IsEnabled = false;
            MusicSlider.IsEnabled = false;

            if (!missingMusicLogged)
            {
                WriteLog("Launcher-Musik nicht gefunden: musik.mp3");
                missingMusicLogged = true;
            }

            return;
        }

        int result = mciSendString("open \"" + launcherMusicPath + "\" type mpegvideo alias " + LauncherMusicAlias, null, 0, IntPtr.Zero);
        if (result != 0)
        {
            launcherMusicPath = null;
            MusicToggleButton.IsEnabled = false;
            MusicSlider.IsEnabled = false;

            if (!missingMusicLogged)
            {
                WriteLog("Launcher-Musik konnte nicht geladen werden.");
                missingMusicLogged = true;
            }

            return;
        }

        musicDeviceOpen = true;
        MusicToggleButton.IsEnabled = true;
        MusicSlider.IsEnabled = true;
    }

    private void ApplyMusicPlayback()
    {
        if (!musicDeviceOpen)
            return;

        int mciVolume = Math.Max(0, Math.Min(1000, currentConfig.MusicVolume * 10));
        mciSendString("setaudio " + LauncherMusicAlias + " volume to " + mciVolume, null, 0, IntPtr.Zero);

        if (currentConfig.MusicEnabled)
            mciSendString("play " + LauncherMusicAlias + " repeat", null, 0, IntPtr.Zero);
        else
            mciSendString("stop " + LauncherMusicAlias, null, 0, IntPtr.Zero);
    }

    private void StopLauncherMusic(bool closeDevice)
    {
        if (!musicDeviceOpen)
            return;

        mciSendString("stop " + LauncherMusicAlias, null, 0, IntPtr.Zero);

        if (!closeDevice)
            return;

        mciSendString("close " + LauncherMusicAlias, null, 0, IntPtr.Zero);
        musicDeviceOpen = false;
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: "NoWindowDrag" })
                return true;

            if (source is ButtonBase || source is Slider || source is Thumb || source is RepeatButton || source is TextBox)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void EnsureExists(string path, string displayName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(displayName + " wurde nicht gefunden. Bitte pruefe die config.ini.", path);
    }

    private string DisplayPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "nicht konfiguriert" : path;
    }

    private void ShowError(string title, string message)
    {
        WriteLog(title + ": " + message);
        LauncherMessageDialog.ShowWarning(this, title, message);
    }

    private void WriteLog(string message)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
        LogTextBox.AppendText(line);
        LogTextBox.ScrollToEnd();
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError("Link konnte nicht geoeffnet werden", ex.Message);
        }
    }
}
