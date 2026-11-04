; MouseWare installer (Inno Setup 6)
; Build:  iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\MouseWare.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "MouseWare"
#define AppExe "MouseWare.exe"
#define AppUrl "https://github.com/ahmetktrn/a4_mouse_ware"

[Setup]
AppId={{6B0F5C1E-7D3A-4E59-9B7C-2F4A8D1E3C55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user without administrator rights; the user may choose "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=MouseWare-Setup-{#AppVersion}
SetupIconFile=..\src\MouseWare.App\Assets\mouseware.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.StartWithWindows=Windows açılınca MouseWare'i başlat
english.StartWithWindows=Start MouseWare with Windows
turkish.LaunchNow=MouseWare'i şimdi başlat
english.LaunchNow=Launch MouseWare now
turkish.A4TechNote=Not: A4Tech 8-in-One yazılımı açıksa yan tuşları kendisi değiştirebilir. En iyi sonuç için onu kapatın veya kaldırın.
english.A4TechNote=Note: if the A4Tech 8-in-One software is running it may change the side buttons. Close or uninstall it for best results.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:StartWithWindows}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app writes from Settings > "Start with Windows".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MouseWare"; \
  ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopMouseWare"

; The user's settings in %APPDATA%\MouseWare are kept on uninstall on purpose.

[Code]
// Stop a running copy so its file can be replaced during an update.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + CustomMessage('A4TechNote');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Remove the start-up entry even if it was added from the app's settings.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MouseWare');
end;
