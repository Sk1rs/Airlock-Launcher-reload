#define AppName "Airlock Launcher"
#define AppExe "Airlock Launcher.exe"
#ifndef Payload
  #define Payload "dist\Airlock Launcher - Dev"
#endif
#ifndef AppVersion
  #define FeatureVersion GetFileVersion(Payload + "\bin_x64\Airlock.Features.dll")
  #define AppVersion Copy(FeatureVersion, 1, Len(FeatureVersion) - 2)
#endif

[Setup]
AppId={{7F2F9360-81F7-444C-AC23-1E2A1C04708C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile={#Payload}\airlock.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile={#Payload}\LICENSE
OutputDir=dist
OutputBaseFilename=AirlockLauncher-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Payload}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\airlock.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\bin_x64\*"; DestDir: "{app}\bin_x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\dotnet_x64\*"; DestDir: "{app}\dotnet_x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\zapret\*"; DestDir: "{app}\zapret"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\ss14"; ValueType: string; ValueData: "URL:ss14"
Root: HKCU; Subkey: "Software\Classes\ss14"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\ss14\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\ss14s"; ValueType: string; ValueData: "URL:ss14s"
Root: HKCU; Subkey: "Software\Classes\ss14s"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\ss14s\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
  Expected: String;
begin
  if CurUninstallStep = usUninstall then begin
    Expected := '"' + ExpandConstant('{app}\{#AppExe}') + '" "%1"';
    if RegQueryStringValue(HKCU, 'Software\Classes\ss14\shell\open\command', '', Command) and
       (CompareText(Command, Expected) = 0) then
      RegDeleteValue(HKCU, 'Software\Classes\ss14\shell\open\command', '');
    if RegQueryStringValue(HKCU, 'Software\Classes\ss14s\shell\open\command', '', Command) and
       (CompareText(Command, Expected) = 0) then
      RegDeleteValue(HKCU, 'Software\Classes\ss14s\shell\open\command', '');
  end;
end;

