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
#define RadminFallbackUrl "https://www.radmin-vpn.com/de/"
#define DgVoodooFallbackUrl "https://dege.freeweb.hu/dgVoodoo2/dgVoodoo2/#"

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
Filename: "{app}\CatanLauncher.exe"; Description: "CatanLauncher starten"; Flags: nowait postinstall skipifsilent

[Code]
var
  DirHintLabel: TNewStaticText;

procedure ConfigureInstallDirUi;
var
  PreferredBaseDir: string;
begin
  PreferredBaseDir := 'E:\Games';
  if DirExists(PreferredBaseDir) then
    WizardForm.DirEdit.Text := AddBackslash(PreferredBaseDir) + 'CatanLauncher';

  DirHintLabel := TNewStaticText.Create(WizardForm.SelectDirPage);
  DirHintLabel.Parent := WizardForm.SelectDirPage.Surface;
  DirHintLabel.Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(10);
  DirHintLabel.Left := WizardForm.DirEdit.Left;
  DirHintLabel.Width := WizardForm.SelectDirPage.Surface.Width - WizardForm.DirEdit.Left;
  DirHintLabel.Height := ScaleY(30);
  DirHintLabel.Caption :=
    'Tipp: Du kannst den Zielpfad direkt eintippen. ' +
    'Wenn der Ordner noch nicht existiert, wird er automatisch erstellt.';
  DirHintLabel.WordWrap := True;
end;

procedure InitializeWizard;
begin
  ConfigureInstallDirUi;
end;

procedure RunOptionalToolInstallers;
var
  ScriptPath: string;
  ScriptText: string;
  ResultCode: Integer;
begin
  if WizardIsTaskSelected('installradmin') then
  begin
    ScriptPath := ExpandConstant('{tmp}\install_radmin.ps1');
    ScriptText :=
      '$ProgressPreference=''SilentlyContinue'';' +
      '$out=Join-Path $env:TEMP ''RadminVPNSetup.exe'';' +
      'try {' +
      ' Invoke-WebRequest -Uri ''{#RadminSetupUrl}'' -OutFile $out -ErrorAction Stop;' +
      ' Start-Process -FilePath $out -Wait' +
      '} catch {' +
      ' Start-Process ''{#RadminFallbackUrl}'' | Out-Null' +
      '}';

    SaveStringToFile(ScriptPath, ScriptText, False);
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;

  if WizardIsTaskSelected('installdgvoodoo') then
  begin
    ScriptPath := ExpandConstant('{tmp}\install_dgvoodoo.ps1');
    ScriptText :=
      '$ProgressPreference=''SilentlyContinue'';' +
      '$zip=Join-Path $env:TEMP ''dgVoodoo2_86_5.zip'';' +
      '$dest=''' + ExpandConstant('{app}\Tools\dgVoodoo2') + ''';' +
      'try {' +
      ' Invoke-WebRequest -Uri ''{#DgVoodooZipUrl}'' -OutFile $zip -ErrorAction Stop;' +
      ' New-Item -ItemType Directory -Force -Path $dest | Out-Null;' +
      ' Expand-Archive -Path $zip -DestinationPath $dest -Force' +
      '} catch {' +
      ' Start-Process ''{#DgVoodooFallbackUrl}'' | Out-Null' +
      '}';

    SaveStringToFile(ScriptPath, ScriptText, False);
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RunOptionalToolInstallers;
end;
