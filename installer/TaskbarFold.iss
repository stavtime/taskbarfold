; Inno Setup script for TaskbarFold.
;
; Installs per-user into %LOCALAPPDATA% so the installer never needs elevation. That is
; deliberate: nothing in this application requires administrator rights, and asking for
; them for a taskbar utility is the kind of thing that stops people installing it.

#define AppName "TaskbarFold"

; Overridden by CI with the git tag: ISCC /DAppVersion=0.2.0
; The literal below is only the fallback for a local build.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppPublisher "TaskbarFold contributors"
#define AppUrl "https://github.com/stavtime/taskbarfold"
#define AppExe "TaskbarFold.exe"

[Setup]
AppId={{7C3B1A64-9D2E-4E71-9C2F-2E5A6B4D8F13}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
LicenseFile=..\LICENSE

; Per-user install, no UAC prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\artifacts\installer
OutputBaseFilename=TaskbarFold-{#AppVersion}-setup
SetupIconFile=..\src\TaskbarFold.App\Assets\TaskbarFold.ico
UninstallDisplayIcon={app}\{#AppExe}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; The published binary is self-contained x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup"

[Files]
Source: "..\artifacts\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startup

[Registry]
; --tray so signing in does not throw the settings window at the user.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "TaskbarFold"; ValueData: """{app}\{#AppExe}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the resident before removing its executable, otherwise the file stays locked.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopResident"

[UninstallDelete]
; Generated shortcuts and the icon cache; the user's groups.json is left alone on purpose.
Type: filesandordirs; Name: "{userappdata}\TaskbarFold\cache"
Type: filesandordirs; Name: "{userappdata}\TaskbarFold\icons"
