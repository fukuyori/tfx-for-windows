; Inno Setup script for tfx for Windows.
;
; Build it with scripts/build-installer.ps1, which passes the version and the
; path to the already-published single-file Tfx.exe via /D defines. Compiling
; this .iss directly (defaults below) also works once a release has been built.

#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif
#ifndef MySourceExe
  #define MySourceExe "..\artifacts\release\tfx-for-windows-" + MyVersion + "-win-x64\Tfx.exe"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts\release"
#endif
#ifndef MyIcon
  #define MyIcon "..\Assets\AppIcon.ico"
#endif

#define MyAppName "tfx for Windows"
#define MyAppExe "Tfx.exe"
#define MyPublisher "fukuyori"
#define MyAppUrl "https://github.com/fukuyori/tfx-for-windows"

[Setup]
; Stable AppId so future versions upgrade in place (do not change it).
AppId={{B1F6C0E2-7A3D-4C9E-9B21-5E8A1D3F2C47}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppPublisher={#MyPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\tfx
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
OutputDir={#MyOutputDir}
OutputBaseFilename=tfx-for-windows-{#MyVersion}-setup
SetupIconFile={#MyIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The published binary is win-x64 self-contained. "x64compatible" allows install
; on x64 and on ARM64 via x64 emulation (Inno Setup 6.3+).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MySourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.ja.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
