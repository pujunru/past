; Inno Setup script for Past.
;
; Produces a single PastSetup.exe that installs per-user (no admin prompt), adds a
; Start menu entry, an optional autostart entry, and a proper uninstall entry under
; "Apps & features".
;
; Build:  ISCC.exe installer\Past.iss /DSourceDir=<path to build output> /DAppVersion=x.y.z

#ifndef SourceDir
  #define SourceDir "..\src\Past.App\bin\x64\Release\net8.0-windows10.0.19041.0"
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Past"
#define AppExeName "Past.App.exe"
#define AppPublisher "Past contributors"
#define AppUrl "https://github.com/pujunru/past"

[Setup]
AppId={{8E4C0A5E-3D2B-4C7A-9F1E-6B5D8C2A7F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=PastSetup
SetupIconFile=..\src\Past.App\Assets\past.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Per-user install: no admin prompt, and it matches where the app keeps its data.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; The app is x64-only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 21H2 or later.
MinVersion=10.0.19044

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Autostart is a task, so unchecking it removes the value rather than leaving it behind.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "{#AppName}"; Flags: deletevalue uninsdeletevalue; Tasks: not autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} now"; \
    Flags: nowait postinstall skipifsilent

[Code]
{ Past is a tray app with no main window, so it is very likely running during an
  upgrade or uninstall. Its files would then be locked. Close it first. }
procedure CloseRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp;
  Result := True;
end;
