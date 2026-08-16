#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

#define AppName "Windows Optimizer"
#define AppExeName "WindowsOptimizer.exe"
#define AppPublisher "1LG Digital"

[Setup]
AppId={{B5ED71D9-7C10-47F5-8AF2-1F507E37C5BE}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\1LG Digital\Windows Optimizer
DefaultGroupName=1LG Digital
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=WindowsOptimizer-Setup-win-x64
SetupIconFile=..\Assets\App.ico
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Safe Windows housekeeping and performance maintenance
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Windows Optimizer"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Windows Optimizer"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Windows Optimizer"; Flags: nowait postinstall skipifsilent
