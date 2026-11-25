; Snapline installer. Built with Inno Setup 6:
;   ISCC.exe installer\snapline.iss /DVersion=1.6.2-windows.1 /DSource=dist\portable
; Packages the self-contained build, so nothing else needs installing.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\dist\portable"
#endif

[Setup]
AppId={{A18943D2-808C-47D6-8F51-299925DCE31C}
AppMutex=Local\SnaplineWindows.SingleInstance
AppName=Snapline (Windows Edition)
AppVersion={#Version}
AppVerName=Snapline (Windows Edition) {#Version}
AppPublisher=pasariga
AppPublisherURL=https://github.com/pasariga/snapline
AppSupportURL=https://github.com/pasariga/snapline/issues
AppUpdatesURL=https://github.com/pasariga/snapline/releases
DefaultDirName={autopf}\Snapline Windows
DefaultGroupName=Snapline (Windows Edition)
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Snapline-{#Version}-win-x64-Setup
SetupIconFile=..\src\Snapline\Assets\Snapline.ico
UninstallDisplayIcon={app}\Snapline.exe
UninstallDisplayName=Snapline (Windows Edition)
WizardStyle=modern
WizardSmallImageFile=wizard-small.bmp
WizardImageFile=wizard.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
CloseApplications=no
RestartApplications=no
LicenseFile=..\LICENSE
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl";

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Snapline when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#Source}\Snapline.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\src\Snapline\Assets\Fonts\OFL.txt"; DestDir: "{app}"; DestName: "Manrope-OFL.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Snapline (Windows Edition)"; Filename: "{app}\Snapline.exe"
Name: "{group}\Uninstall Snapline (Windows Edition)"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Snapline (Windows Edition)"; Filename: "{app}\Snapline.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SnaplineWindows"; ValueData: """{app}\Snapline.exe"""; Tasks: startup

[Run]
Filename: "{app}\Snapline.exe"; Description: "{cm:LaunchProgram,Snapline}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunKey, CurrentCommand: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if RegQueryStringValue(HKCU, RunKey, 'SnaplineWindows', CurrentCommand) and
     (CompareText(CurrentCommand, '"' + ExpandConstant('{app}\Snapline.exe') + '"') = 0) then
    RegDeleteValue(HKCU, RunKey, 'SnaplineWindows');
end;
