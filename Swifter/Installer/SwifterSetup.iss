; Swifter Browser - Inno Setup Script
; Requires: Inno Setup 6.3+, WebView2 Runtime bootstrapper already handled by app

#define MyAppName "Swifter Browser"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Swifter Projects"
#define MyAppURL "https://github.com/nirob100bd/Swifter"
#define MyAppExeName "Swifter.exe"
#define MyAppId "{{8F3C2A91-6D4E-4B7A-9C11-SWIFTER00001}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=SwifterSetup-{#MyAppVersion}-x64
SetupIconFile=..\swifter.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#MyAppName}
UninstallDisplaySize=134217728
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no
ShowLanguageDialog=no
Languages=english

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1
Name: "associatehtml"; Description: "Make Swifter the default browser for HTTP/HTTPS links"; GroupDescription: "File associations:"; Flags: unchecked
Name: "associatepdf"; Description: "Associate PDF files with Swifter"; GroupDescription: "File associations:"; Flags: unchecked
Name: "sendsason"; Description: "Add 'Open with Swifter' context menu for images"; GroupDescription: "Shell integration:"; Flags: unchecked
Name: "startuptask"; Description: "Launch Swifter when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\bin\Release\net8.0-windows10.0.17763.0\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net8.0-windows10.0.17763.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\bin\Release\net8.0-windows10.0.17763.0\swifter.ico"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{userappdata}\Swifter"; Flags: uninsneveruninstall
Name: "{userappdata}\Swifter\profiles"
Name: "{userappdata}\Swifter\downloads"
Name: "{userappdata}\Swifter\extensions"
Name: "{userappdata}\Swifter\cache"
Name: "{userappdata}\Swifter\logs"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} Settings"; Filename: "{app}\{#MyAppExeName}"; Parameters: "swifter://settings"
Name: "{group}\{#MyAppName} Downloads"; Filename: "{app}\{#MyAppExeName}"; Parameters: "swifter://downloads"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Registry]
Root: HKA; Subkey: "Software\Classes\http\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsvalueifempty; Tasks: associatehtml
Root: HKA; Subkey: "Software\Classes\https\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsvalueifempty; Tasks: associatehtml
Root: HKA; Subkey: "Software\Classes\http\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletevalue; Tasks: associatehtml
Root: HKA; Subkey: "Software\Classes\https\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletevalue; Tasks: associatehtml

Root: HKA; Subkey: "Software\Classes\.pdf"; ValueType: string; ValueData: "Swifter.PDF"; Flags: uninsdeletevalue; Tasks: associatepdf
Root: HKA; Subkey: "Software\Classes\Swifter.PDF"; ValueType: string; ValueData: "PDF Document (Swifter)"; Flags: uninsdeletekey; Tasks: associatepdf
Root: HKA; Subkey: "Software\Classes\Swifter.PDF\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associatepdf
Root: HKA; Subkey: "Software\Classes\Swifter.PDF\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associatepdf

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\OpenWithProgids"; ValueType: string; ValueName: "Swifter.PDF"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatepdf

Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\SwifterHere"; ValueType: string; ValueData: "Open Swifter here"; Flags: uninsdeletekey; Tasks: sendsason
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\SwifterHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%V"""; Tasks: sendsason

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\image\shell\SwifterLens"; ValueType: string; ValueData: "Search image with Google Lens (Swifter)"; Flags: uninsdeletekey; Tasks: sendsason
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\image\shell\SwifterLens\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""lens:%1"""; Tasks: sendsason

Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Swifter"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startuptask

Root: HKCU; Subkey: "Software\Swifter"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Swifter"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

Root: HKA; Subkey: "Software\Classes\swifter"; ValueType: string; ValueData: "URL:Swifter Internal Protocol"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\swifter"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\swifter\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\swifter\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[UninstallDelete]
Type: filesandordirs; Name: "{app}\*.log"
Type: files; Name: "{app}\*.pdb"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
Filename: "powershell.exe"; Parameters: "-Command ""& { [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path','User') + ';' + '{app}', 'User') }"""; Flags: runhidden; Components:

[Code]
function IsWebView2RuntimeInstalled: Boolean;
var
  pData: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pData)
    or RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pData);
  if Result then Result := (pData <> '') and (pData <> '0.0.0.0');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsWebView2RuntimeInstalled then
    begin
      MsgBox('Microsoft Edge WebView2 Runtime was not detected.'#13#10#13#10
        + 'Swifter requires WebView2 to render pages.'#13#10
        + 'Download it from:#13#10'
        + 'https://developer.microsoft.com/microsoft-edge/webview2/'#13#10#13#10
        + 'Setup will continue, but the browser will not launch until WebView2 is installed.',
        mbInformation, MB_OK);
    end;
  end;
end;
