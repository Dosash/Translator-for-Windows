; Translator for Windows — Inno Setup 6 script.
;
; Per-user installer, no admin rights required. Built by build.ps1 (which supplies AppVersion,
; AppArch, Rid, SourceDir, OutputDir via /D), but can also be compiled directly for local testing:
;
;   ISCC.exe /DAppVersion=1.0.0 /DAppArch=x64 /DRid=win-x64 ^
;            /DSourceDir=..\dist\publish\win-x64 /DOutputDir=..\dist installer\Translator.iss
;
; Requires Inno Setup 6.5+ (for the full set of bundled [Languages] translations used below).

#define MyAppName "Translator"
#define MyAppExeName "Translator.exe"
#define MyAppPublisher "Translator contributors"
#define MyAppURL "https://github.com/Dosash/Translator-for-Windows"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef Rid
  #define Rid "win-" + AppArch
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\publish\" + Rid
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Fixed AppId: keep this GUID stable across all future versions so upgrades are detected
; correctly and Add/Remove Programs shows a single entry.
AppId={{7A5D095D-294B-4E4F-8A3F-8C1F252FC64D}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#AppVersion}

; Per-user install — no UAC prompt, no admin rights needed or requested.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousAppDir=yes

; Windows 10 1809 (build 17763) and later, x64 or ARM64 depending on which payload was published.
MinVersion=10.0.17763
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

; Close a running Translator (via the Restart Manager) before copying files, and do the same
; automatically during uninstall; do not auto-relaunch it afterwards — [Run] below already offers
; to start it once setup finishes.
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=TranslatorSetup-{#AppVersion}-{#Rid}
SetupIconFile=..\src\Translator\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
; Language-specific entries use the "<language>.<name>=" prefix form; unprefixed entries are the fallback.
LaunchAtStartupTask=Launch at Windows sign-in
russian.LaunchAtStartupTask=Запускать при входе в Windows
ukrainian.LaunchAtStartupTask=Запускати під час входу в Windows
OtherTasks=Other:
russian.OtherTasks=Прочее:
ukrainian.OtherTasks=Інше:
DeleteUserDataPrompt=Also delete Translator's saved settings, translation history and downloaded offline language models?%n%nThis will remove:%n%1%n%2
russian.DeleteUserDataPrompt=Удалить также настройки, историю переводов и загруженные офлайн-модели Translator?%n%nБудут удалены:%n%1%n%2
ukrainian.DeleteUserDataPrompt=Видалити також налаштування, історію перекладів і завантажені офлайн-моделі Translator?%n%nБудуть видалені:%n%1%n%2

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:LaunchAtStartupTask}"; GroupDescription: "{cm:OtherTasks}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Same launch-at-startup mechanism the app itself uses for Settings -> "Launch at Windows sign-in"
; (see docs/ARCHITECTURE.md — HKCU Run value "Translator" = "<exe>" --autostart). The uninstaller
; deletes the value in [Code] as well, because the app itself may have created it.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Translator"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir, LocalAppDataDir, PromptText: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // The app can enable autostart from its own settings, so the value may exist even when the
    // installer task was not selected.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Translator');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Translator');
  end;

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    AppDataDir := ExpandConstant('{userappdata}\Translator');
    LocalAppDataDir := ExpandConstant('{localappdata}\Translator');
    if DirExists(AppDataDir) or DirExists(LocalAppDataDir) then
    begin
      PromptText := FmtMessage(CustomMessage('DeleteUserDataPrompt'), [AppDataDir, LocalAppDataDir]);
      if MsgBox(PromptText, mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(AppDataDir, True, True, True);
        DelTree(LocalAppDataDir, True, True, True);
      end;
    end;
  end;
end;
