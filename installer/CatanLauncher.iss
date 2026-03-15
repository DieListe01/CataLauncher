#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif

#ifndef PublishDir
#error "PublishDir preprocessor variable is required."
#endif

#ifndef OutputDir
#define OutputDir "."
#endif

#ifndef PrereqDir
#error "PrereqDir preprocessor variable is required."
#endif

#define RadminSetupUrl "https://download.radmin-vpn.com/download/files/Radmin_VPN_2.0.4899.9.exe"
#define DgVoodooZipUrl "https://dege.freeweb.hu/dgVoodoo2/bin/dgVoodoo2_86_5.zip"

[Setup]
AppId={{A1D315A8-A0F3-4B15-A8C0-2F8F0F9DDF63}
AppName=CatanLauncher
AppVersion={#MyAppVersion}
AppPublisher=CatanLauncher Team
DefaultDirName={localappdata}\Programs\CatanLauncher
DefaultGroupName=CatanLauncher
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir={#OutputDir}
OutputBaseFilename=CatanLauncher-Setup-v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile={#PublishDir}\Assets\catan_icon.ico
UninstallDisplayIcon={app}\CatanLauncher.exe

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Types]
Name: "standard"; Description: "Standardinstallation (ohne Zusatz-Abhaengigkeiten)"; Flags: iscustom
Name: "full"; Description: "Vollinstallation (inkl. Visual C++ Runtime, Admin erforderlich)"

[Components]
Name: "main"; Description: "CatanLauncher"; Types: standard full; Flags: fixed
Name: "prereqs"; Description: "Microsoft Visual C++ Runtime installieren"; Types: full; Check: IsAdminInstallMode

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknuepfung erstellen"; GroupDescription: "Zusaetzliche Symbole:"; Flags: unchecked
Name: "installradmin"; Description: "Radmin VPN herunterladen und installieren (fuer Onlinespiele, nicht fuer lokales Netzwerk)"; GroupDescription: "Optionale Online-Tools:"; Flags: unchecked
Name: "installdgvoodoo"; Description: "dgVoodoo2 herunterladen und in den Launcher-Ordner entpacken"; GroupDescription: "Optionale Online-Tools:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: main
Source: "{#PrereqDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: prereqs

[Icons]
Name: "{autoprograms}\CatanLauncher"; Filename: "{app}\CatanLauncher.exe"
Name: "{autodesktop}\CatanLauncher"; Filename: "{app}\CatanLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installiere Microsoft Visual C++ Runtime..."; Flags: waituntilterminated runhidden; Components: prereqs
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';$out=Join-Path $env:TEMP 'RadminVPNSetup.exe';Invoke-WebRequest -Uri '{#RadminSetupUrl}' -OutFile $out;Start-Process -FilePath $out -Wait"""; StatusMsg: "Lade Radmin VPN herunter und starte die Installation..."; Description: "Radmin VPN installieren"; Flags: waituntilterminated postinstall skipifsilent; Tasks: installradmin
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';$zip=Join-Path $env:TEMP 'dgVoodoo2_86_5.zip';$dest='{app}\Tools\dgVoodoo2';Invoke-WebRequest -Uri '{#DgVoodooZipUrl}' -OutFile $zip;New-Item -ItemType Directory -Force -Path $dest | Out-Null;Expand-Archive -Path $zip -DestinationPath $dest -Force"""; StatusMsg: "Lade dgVoodoo2 herunter und entpacke es..."; Description: "dgVoodoo2 herunterladen und entpacken"; Flags: waituntilterminated postinstall skipifsilent; Tasks: installdgvoodoo
Filename: "{app}\CatanLauncher.exe"; Description: "CatanLauncher starten"; Flags: nowait postinstall skipifsilent
