; Inno Setup script for Utevo Lux — per-user install (no admin prompt), Start Menu + uninstaller.
#define MyAppName "Utevo Lux"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "wmarcelod"
#define MyAppURL "https://github.com/wmarcelod/utevo-lux"
#define MyAppExeName "UtevoLux.exe"
#define SrcDir "C:\Users\wwwma\AppData\Local\Temp\claude\C--Users-wwwma-OneDrive-Documentos-OpenTibiaVision\216d07b8-6e0c-481b-8644-43b7d3f00d47\scratchpad\publish\UtevoLux"

[Setup]
AppId={{A7F3C1E9-5B2D-4E8A-9C1F-2D6B8E4A0C71}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\UtevoLux
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=C:\Users\wwwma\AppData\Local\Temp\claude\C--Users-wwwma-OneDrive-Documentos-OpenTibiaVision\216d07b8-6e0c-481b-8644-43b7d3f00d47\scratchpad
OutputBaseFilename=UtevoLux-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=C:\Users\wwwma\OneDrive\Documentos\OpenTibiaVision\UtevoLux\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
