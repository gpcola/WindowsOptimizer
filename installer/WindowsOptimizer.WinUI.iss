#define MyAppName "Windows Optimizer"
#define MyAppPublisher "1LG Digital"
#define MyAppURL "https://www.1lg.com"
#define MyAppExeName "WindowsOptimizer.WinUI.exe"
#ifndef SourceDir
#define SourceDir "..\publish\winui\win-x64\Release"
#endif

[Setup]
AppId={{7F390D9F-6B22-4D41-96B0-184193D17100}
AppName={#MyAppName}
AppVersion=1.0.0
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\1LG Digital\Windows Optimizer
DefaultGroupName=1LG Digital\Windows Optimizer
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=WindowsOptimizer-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\App.ico
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Windows Optimizer"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Windows Optimizer"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Windows Optimizer"; Flags: nowait postinstall skipifsilent
