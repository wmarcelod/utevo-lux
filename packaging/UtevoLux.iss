; Inno Setup script for Utevo Lux — per-user install (no admin prompt), Start Menu + uninstaller.
; MyAppVersion and SrcDir are normally passed on the command line by packaging/release.ps1:
;   ISCC /DMyAppVersion=0.1.2 /DSrcDir="C:\path\to\publish" /O"C:\out" /FUtevoLux-Setup packaging\UtevoLux.iss
; Standalone use: publish first, then compile (defaults below point at the standard publish folder).
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SrcDir
  #define SrcDir "..\UtevoLux\bin\Release\net8.0-windows\win-x64\publish"
#endif
#define MyAppName "Utevo Lux"
#define MyAppPublisher "wmarcelod"
#define MyAppURL "https://github.com/wmarcelod/utevo-lux"
#define MyAppExeName "UtevoLux.exe"

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
OutputDir=.
OutputBaseFilename=UtevoLux-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\UtevoLux\Assets\icon.ico
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
