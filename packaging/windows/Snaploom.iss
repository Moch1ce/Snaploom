#ifndef AppVersion
  #error AppVersion must be supplied by the build script.
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by the build script.
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by the build script.
#endif
#ifndef IconPath
  #error IconPath must be supplied by the build script.
#endif
#ifndef ChineseLanguagePath
  #error ChineseLanguagePath must be supplied by the build script.
#endif

#define AppExeName "Snaploom.App.exe"

[Setup]
AppId=Snaploom.Desktop
AppName=Snaploom
AppVersion={#AppVersion}
AppVerName=Snaploom {#AppVersion}
AppPublisher=Snaploom
AppPublisherURL=https://github.com/liuchuana/Snaploom
AppSupportURL=https://github.com/liuchuana/Snaploom/issues
AppUpdatesURL=https://github.com/liuchuana/Snaploom/releases/latest
DefaultDirName={localappdata}\Programs\Snaploom
DefaultGroupName=Snaploom
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#OutputDir}
OutputBaseFilename=snaploom-{#AppVersion}-windows-x64-setup
SetupIconFile={#IconPath}
UninstallDisplayName=Snaploom
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Snaploom
VersionInfoDescription=Snaploom Windows x64 user installer
VersionInfoProductName=Snaploom
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "{#ChineseLanguagePath}"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Snaploom"; Filename: "{app}\{#AppExeName}"; AppUserModelID: "Snaploom.Desktop"
Name: "{autodesktop}\Snaploom"; Filename: "{app}\{#AppExeName}"; AppUserModelID: "Snaploom.Desktop"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,Snaploom}"; Flags: nowait postinstall skipifsilent
