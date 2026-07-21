; Copyright (C) 2026 Snaploom contributors
; SPDX-License-Identifier: GPL-3.0-or-later

#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef StagingDir
  #error StagingDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef IconPath
  #error IconPath is required
#endif

[Setup]
AppId=Snaploom.Desktop
AppName=Snaploom
AppVersion={#AppVersion}
AppPublisher=Snaploom contributors
AppPublisherURL=https://github.com/Moch1ce/Snaploom
DefaultDirName={localappdata}\Programs\Snaploom
DefaultGroupName=Snaploom
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=snaploom-{#AppVersion}-windows-x64-setup
SetupIconFile={#IconPath}
UninstallDisplayName=Snaploom
UninstallDisplayIcon={app}\snaploom-desktop.exe
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
MinVersion=10.0.19045

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Snaploom"; Filename: "{app}\snaploom-desktop.exe"
Name: "{userstartup}\Snaploom"; Filename: "{app}\snaploom-desktop.exe"; Tasks: startup

[Tasks]
Name: "startup"; Description: "Start Snaploom when I sign in"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Snaploom\CaptureHost"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}\snaploom-capture-host.exe"; Flags: uninsdeletekey

[Run]
Filename: "{app}\snaploom-desktop.exe"; Description: "Launch Snaploom"; Flags: nowait postinstall skipifsilent
