#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif

#ifndef PublishDir
#error "PublishDir preprocessor variable is required."
#endif

#ifndef OutputDir
#define OutputDir "."
#endif

[Setup]
AppId={{A1D315A8-A0F3-4B15-A8C0-2F8F0F9DDF63}
AppName=CatanLauncher
AppVersion={#MyAppVersion}
AppPublisher=CatanLauncher Team
DefaultDirName={autopf}\CatanLauncher
DefaultGroupName=CatanLauncher
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=CatanLauncher-Setup-v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
SetupIconFile={#PublishDir}\Assets\catan_icon.ico
UninstallDisplayIcon={app}\CatanLauncher.exe

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknuepfung erstellen"; GroupDescription: "Zusaetzliche Symbole:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CatanLauncher"; Filename: "{app}\CatanLauncher.exe"
Name: "{autodesktop}\CatanLauncher"; Filename: "{app}\CatanLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CatanLauncher.exe"; Description: "CatanLauncher starten"; Flags: nowait postinstall skipifsilent
