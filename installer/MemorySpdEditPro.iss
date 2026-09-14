; MemorySpdEdit Pro Ver.001 — Inno Setup script
#define MyAppName "MemorySpdEdit Pro"
#define MyAppNameCN "内存SPD修改工具 Pro"
#define MyAppVersion "Ver.001"
#define MyAppPublisher "SuperGun"
#define MyAppURL "https://github.com/hbsyth/MemorySpdEdit-Pro"
#define MyAppExeName "MemorySpdEdit-Pro.exe"

[Setup]
AppId={{A8F3C2E1-9B47-4D6A-8E21-MemorySpdEditPro}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\publish\release
OutputBaseFilename=MemorySpdEdit-Pro-Ver.001-Setup
SetupIconFile=..\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=0.0.1.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=0.0.1
VersionInfoDescription={#MyAppNameCN} {#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: checkedonce

[Files]
Source: "..\publish\green\SpdEditor.exe"; DestName: "{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppNameCN}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
