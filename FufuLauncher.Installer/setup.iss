#define AppName       "FufuLauncher"
#define AppVersion    "1.4.1.0"
#define AppPublisher  "FufuLauncher"
#define AppExe        "FufuLauncher.exe"
#define AppId         "{{A7B2C3D4-E5F6-7890-AB12-CD34EF567890}"
#define SrcDir        "..\FufuLauncher\bin\x64\Release\net8.0-windows10.0.26100.0"
#define IconFile      "..\install.ico"
#define FontName      "Microsoft YaHei UI"
#define OutDir        "installer"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} Installer

DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=no
ShowLanguageDialog=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.14393

OutputDir={#OutDir}
OutputBaseFilename={#AppName}_Setup_v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
WizardSizePercent=110
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

CloseApplications=force
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon";   Description: "创建桌面快捷方式";   GroupDescription: "附加快捷方式:"
Name: "startmenu";     Description: "创建开始菜单快捷方式"; GroupDescription: "附加快捷方式:"
Name: "autostart";     Description: "开机自启动";         GroupDescription: "其他:"; Flags: unchecked

[Files]
Source: "{#SrcDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\*";          DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.lib,*.exp,*.xml,BuildHost-*,obj\*"

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenu
Name: "{group}\卸载 {#AppName}";     Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}";    Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Cache"
Type: filesandordirs; Name: "{app}\Logs"
Type: dirifempty;     Name: "{app}"

[Code]

var
  GDesktopIconExists: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  UninstStr: string;
  UninstKey: string;
  ResultCode: Integer;
  DesktopPath: string;
  ShortcutPath: string;
begin
  Result := '';
  DesktopPath := ExpandConstant('{autodesktop}');
  ShortcutPath := AddBackslash(DesktopPath) + ExpandConstant('{#AppName}') + '.lnk';
  GDesktopIconExists := FileExists(ShortcutPath);

  if GDesktopIconExists and IsTaskSelected('desktopicon') = False then
    WizardSelectTasks('desktopicon');

  UninstKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' +
               ExpandConstant('{#SetupSetting("AppId")}') + '_is1';

  if not RegQueryStringValue(HKCU, UninstKey, 'UninstallString', UninstStr) then
    RegQueryStringValue(HKLM, UninstKey, 'UninstallString', UninstStr);

  if UninstStr <> '' then
  begin
    UninstStr := RemoveQuotes(UninstStr);
    Exec(UninstStr, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure ApplyCustomFontToControl(C: TControl);
var
  I: Integer;
  F: string;
begin
  F := '{#FontName}';
  if C is TLabel             then TLabel(C).Font.Name := F
  else if C is TNewStaticText then TNewStaticText(C).Font.Name := F
  else if C is TNewCheckListBox then TNewCheckListBox(C).Font.Name := F
  else if C is TNewListBox    then TNewListBox(C).Font.Name := F
  else if C is TNewMemo       then TNewMemo(C).Font.Name := F
  else if C is TNewEdit       then TNewEdit(C).Font.Name := F
  else if C is TNewComboBox   then TNewComboBox(C).Font.Name := F
  else if C is TNewCheckBox   then TNewCheckBox(C).Font.Name := F
  else if C is TNewRadioButton then TNewRadioButton(C).Font.Name := F
  else if C is TNewButton     then TNewButton(C).Font.Name := F
  else if C is TButton        then TButton(C).Font.Name := F
  else if C is TNewProgressBar then
  else if C is TForm          then TForm(C).Font.Name := F;

  if C is TWinControl then
    for I := 0 to TWinControl(C).ControlCount - 1 do
      ApplyCustomFontToControl(TWinControl(C).Controls[I]);
end;

procedure InitializeWizard;
begin
  ApplyCustomFontToControl(WizardForm);
end;

procedure InitializeUninstallProgressForm;
begin
  ApplyCustomFontToControl(UninstallProgressForm);
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if IsTaskSelected('desktopicon') then
    SetPreviousData(PreviousDataKey, 'desktopicon', '1')
  else
    SetPreviousData(PreviousDataKey, 'desktopicon', '0');
end;