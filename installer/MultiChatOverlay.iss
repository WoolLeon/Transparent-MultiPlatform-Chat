#define AppName "Multi Chat Overlay"
#define AppVersion "0.1.0"
#define AppPublisher "Multi Chat Overlay"
#define AppExeName "MultiChatOverlay.App.exe"

[Setup]
AppId={{25D87B89-0FC7-42EA-9A1E-49D98BB09A51}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Multi Chat Overlay
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=MultiChatOverlay-Setup
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "MicrosoftEdgeWebView2Setup.exe"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: not IsWebView2Installed
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsWebView2Installed: Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{localappdata}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{autopf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{autopf}\Microsoft\EdgeWebView\Application'));
end;
