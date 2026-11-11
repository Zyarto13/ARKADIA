#ifndef AppVersion
  #define AppVersion "1.0.4"
#endif

#define AppName "ARKADIA"
#define AppPublisher "ARKADIA"
#define AppExeName "Arkadia.App.exe"

[Setup]
AppId={{B1E07485-B83F-4F47-BD2A-3921A797B73E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
LicenseFile=..\LICENSE
DefaultDirName={localappdata}\Programs\ARKADIA
DefaultGroupName=ARKADIA
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\dist
OutputBaseFilename=ARKADIA-Setup-{#AppVersion}-x64
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ARKADIA.SingleInstance
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "..\src\Arkadia.App\bin\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ARKADIA"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\ARKADIA"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
