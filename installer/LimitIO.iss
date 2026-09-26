; LimitIO Inno Setup script.
;
; Expects the three apps already published (self-contained, single-file, win-x64) into:
;   artifacts\service\   (LimitIO.Service.exe + WinDivert.dll + WinDivert64.sys)
;   artifacts\ui\         (LimitIO.UI.exe)
;   artifacts\helper\     (LimitIO.Installer.Helper.exe)
; relative to the repository root - see .github/workflows/release.yml for the exact publish commands,
; or docs/RELEASING.md for how to do this by hand. Compile with:
;   iscc installer\LimitIO.iss /DAppVersion=1.2.3
; (AppVersion defaults to 0.0.0-dev if omitted, for local test compiles.)

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#define RepoRoot ".."

[Setup]
; Inno Setup's own script language uses {...} for constant substitution (e.g. {app}, {group}), so a
; literal opening brace must be doubled to "{{" or it's parsed as a constant lookup (the closing "}"
; needs no escaping - only "{" triggers constant-parsing). This is the standard escaped form for a
; literal GUID, matching what Inno Setup's own project wizard generates.
AppId={{B4B6E1F0-6C1A-4F7E-9C8B-2B7C2C5B9A11}
AppName=LimitIO
AppVersion={#AppVersion}
AppPublisher=LimitIO Project
AppPublisherURL=https://github.com/
DefaultDirName={autopf}\LimitIO
DefaultGroupName=LimitIO
DisableProgramGroupPage=yes
OutputDir={#RepoRoot}\installer\Output
OutputBaseFilename=LimitIO-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
LicenseFile={#RepoRoot}\LICENSE
UninstallDisplayIcon={app}\ui\LimitIO.UI.exe
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#RepoRoot}\artifacts\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs
Source: "{#RepoRoot}\artifacts\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs
Source: "{#RepoRoot}\artifacts\helper\*"; DestDir: "{app}\helper"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\LimitIO"; Filename: "{app}\ui\LimitIO.UI.exe"
Name: "{group}\Uninstall LimitIO"; Filename: "{uninstallexe}"

[Registry]
; Auto-start the tray app for whichever account logs in, machine-wide rather than per-user, so it comes
; up regardless of which of possibly-several Windows accounts is used.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LimitIO"; \
    ValueData: """{app}\ui\LimitIO.UI.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\helper\LimitIO.Installer.Helper.exe"; Parameters: "install ""{app}"""; \
    Flags: runhidden waituntilterminated; StatusMsg: "Configuring the LimitIO background service..."
Filename: "{app}\ui\LimitIO.UI.exe"; Description: "Launch LimitIO now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Must run before Inno's own file/service cleanup below - reverses the ACL hardening and stops the
; service and watchdog task first, so file removal doesn't fail partway through.
Filename: "{app}\helper\LimitIO.Installer.Helper.exe"; Parameters: "uninstall ""{app}"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "UninstallLimitIOHelper"

[UninstallDelete]
; Deliberately does NOT remove %ProgramData%\LimitIO (config, password hash, usage history) - an
; accidental uninstall shouldn't silently erase a parent's configured limits if they're about to
; reinstall an update. See docs/setup.md for how to remove it by hand if a full wipe is wanted.
