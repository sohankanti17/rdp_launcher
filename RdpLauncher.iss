; Inno Setup script for RDP Launcher
; Builds a small installer that detects the .NET 8 Desktop Runtime, installs it
; automatically (via winget) if missing, then installs the app + shortcuts.
;
; Prereqs to compile this:
;   1. Build the small framework-dependent app first:
;        dotnet publish -c Release --self-contained false
;   2. Install Inno Setup 6.1+  (winget install JRSoftware.InnoSetup)
;   3. Open this .iss in the Inno Setup Compiler and click Compile
;      (or run:  iscc RdpLauncher.iss)
;
; Place this file in the project root (next to app.ico). Output: RdpLauncherSetup.exe

#define AppName    "RDP Launcher"
#define AppVersion "1.1.0"
#define AppExe     "RdpLauncher.exe"
#define PublishDir "bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\RdpLauncher
DefaultGroupName=RDP Launcher
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=RdpLauncherSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=app.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startupicon"; Description: "Start RDP Launcher automatically at logon"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.ico";                 DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\RDP Launcher";         Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall RDP Launcher"; Filename: "{uninstallexe}"
Name: "{commondesktop}\RDP Launcher"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userstartup}\RDP Launcher";   Filename: "{app}\{#AppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch RDP Launcher now"; Flags: nowait postinstall skipifsilent

[Code]
{ True if any Microsoft.WindowsDesktop.App 8.x runtime folder exists. }
function DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  BasePath: String;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(BasePath + '\8.*', FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InstallRuntimeViaWinget(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('winget',
    'install --id Microsoft.DotNet.DesktopRuntime.8 --silent --accept-package-agreements --accept-source-agreements',
    '', SW_SHOW, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if not DesktopRuntimeInstalled() then
    begin
      if MsgBox('RDP Launcher needs the .NET 8 Desktop Runtime, which isn''t installed on this PC.' + #13#10#13#10 +
                'Install it now automatically?', mbConfirmation, MB_YESNO) = IDYES then
      begin
        if not InstallRuntimeViaWinget() then
          MsgBox('The automatic install did not complete (winget may be unavailable).' + #13#10 +
                 'You can install it manually from:' + #13#10 +
                 'https://dotnet.microsoft.com/download/dotnet/8.0  (Desktop Runtime, x64)',
                 mbInformation, MB_OK);
      end;
    end;
  end;
end;
