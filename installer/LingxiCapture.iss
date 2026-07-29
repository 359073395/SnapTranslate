#define AppName "灵犀截图"
#define AppEnglishName "Lingxi Capture"
#define AppVersion "0.3.0"
#define AppPublisher "Lingxi Capture contributors"
#define AppExeName "LingxiCapture.exe"

[Setup]
AppId={{A3456090-959E-4F8C-8670-5B8036FC3740}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/359073395/SnapTranslate
AppSupportURL=https://github.com/359073395/SnapTranslate/issues
AppUpdatesURL=https://github.com/359073395/SnapTranslate/releases
DefaultDirName={localappdata}\Programs\LingxiCapture
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer-v0.3.0
OutputBaseFilename=LingxiCapture-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\src\SnapTranslate\Assets\lingxi-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\LingxiCapture.SingleInstance.v1
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "..\artifacts\publish-v0.3.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LingxiCapture"; ValueData: """{app}\{#AppExeName}"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动{#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
