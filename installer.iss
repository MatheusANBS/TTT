#define AppName      "TTT"
#define AppVersion   "2.0.2"
#define AppPublisher "Kry"
#define AppExeName   "TTT.exe"
#define SourceDir    "TTT\bin\x64\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A3F2C1B4-7E8D-4F5A-9C6B-2D1E0F3A8B7C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisherURL=https://github.com
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installer_output
OutputBaseFilename={#AppName}_Setup_{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#AppName}\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0
DisableProgramGroupPage=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Self-contained publish — inclui .NET 8 runtime, não precisa de instalação prévia
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Limpa resíduos de versões anteriores antes de copiar os arquivos novos.
Type: filesandordirs; Name: "{app}"

[UninstallDelete]
; Garante que qualquer resíduo criado em tempo de execução seja removido no uninstall.
Type: filesandordirs; Name: "{app}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser
