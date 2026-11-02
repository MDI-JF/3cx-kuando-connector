; Inno Setup installer - built from the self-contained .NET publish directory.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #error PublishDir preprocessor variable is required
#endif
[Setup]
AppId={{A2D0CC51-FB01-4CC7-8FC4-135A443D552A}
AppName=3CX kuando Connector
AppVersion={#AppVersion}
AppPublisher=3CX kuando Connector Contributors
DefaultDirName={autopf}\3CX kuando Connector
DefaultGroupName=3CX kuando Connector
DisableProgramGroupPage=yes
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts
OutputBaseFilename=3CXKuandoConnector-v{#AppVersion}-win-x64-setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\3cxKuando.exe
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\3CX kuando Connector"; Filename: "{app}\3cxKuando.exe"
Name: "{group}\Uninstall 3CX kuando Connector"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\3cxKuando.exe"; Description: "3CX kuando Connector starten"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Clean up the executable name from pre-release builds.
Type: files; Name: "{app}\ThreeCxKuando.exe"

[UninstallDelete]
; Never delete per-user settings or DPAPI-protected secrets on uninstall.
