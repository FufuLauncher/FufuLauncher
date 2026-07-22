#define AppName       "FufuLauncher"
#define AppVersion    "1.4.3.1"
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
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chs.DownloadingDotNet=正在获取必要组件 (Microsoft .NET 8.0 桌面运行时)...
chs.InstallingDotNet=正在部署必要组件 (Microsoft .NET 8.0 桌面运行时)，请稍候...
chs.InstallFailed=必要组件 (.NET 8.0 桌面运行时) 部署未成功。此组件为运行该程序所必需，请稍后手动安装。主程序将继续安装。
chs.DownloadFailed=无法获取必要组件 (.NET 8.0 桌面运行时)。请检查网络连接状态，或稍后手动完成安装。主程序将继续安装。

chs.DownloadingVC=正在获取必要组件 (Visual C++ v14 Redistributable)...
chs.InstallingVC=正在部署必要组件 (Visual C++ v14 Redistributable)，请稍候...
chs.InstallFailedVC=必要组件 (Visual C++ v14 Redistributable) 部署未成功。请稍后手动安装环境，主程序将继续安装。
chs.DownloadFailedVC=无法获取必要组件 (Visual C++ v14 Redistributable)。请检查网络连接状态，或稍后手动完成安装。

chs.RuntimeExecFailed=必要组件安装程序无法执行。请稍后手动安装环境，主程序将继续安装。

en.DownloadingDotNet=Retrieving prerequisite (Microsoft .NET 8.0 Desktop Runtime)...
en.InstallingDotNet=Deploying prerequisite (Microsoft .NET 8.0 Desktop Runtime), please wait...
en.InstallFailed=The deployment of the prerequisite (.NET 8.0 Desktop Runtime) was unsuccessful. This component is required; please install it manually later. The main installation will now continue.
en.DownloadFailed=Unable to retrieve the prerequisite (.NET 8.0 Desktop Runtime). Please verify your network connection or install it manually later. The main installation will now continue.

en.DownloadingVC=Retrieving prerequisite (Visual C++ v14 Redistributable)...
en.InstallingVC=Deploying prerequisite (Visual C++ v14 Redistributable), please wait...
en.InstallFailedVC=The deployment of the prerequisite (Visual C++ v14 Redistributable) was unsuccessful. Please install it manually later. The main installation will now continue.
en.DownloadFailedVC=Unable to retrieve the prerequisite (Visual C++ v14 Redistributable). Please verify your network connection or install it manually later.

en.RuntimeExecFailed=The prerequisite installer failed to execute. Please install it manually later. The main installation will now continue.

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

[InstallDelete]
Type: filesandordirs; Name: "{app}\Cache"
Type: files; Name: "{app}\resources.pri"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Cache"
Type: filesandordirs; Name: "{app}\Logs"
Type: dirifempty;     Name: "{app}"

[Code]

var
  GDesktopIconExists: Boolean;
  DownloadPage: TDownloadWizardPage;

function IsDotNet8DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if FindFirst(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  if RegGetValueNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.0.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function IsVCRedistInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
      Result := True;
  end;
end;

function CompareVersionStr(V1, V2: string): Integer;
var
  P1, P2: Integer;
  Num1, Num2: Integer;
begin
  Result := 0;
  while (V1 <> '') or (V2 <> '') do
  begin
    P1 := Pos('.', V1);
    if P1 = 0 then P1 := Length(V1) + 1;
    P2 := Pos('.', V2);
    if P2 = 0 then P2 := Length(V2) + 1;

    if P1 > 1 then Num1 := StrToIntDef(Copy(V1, 1, P1 - 1), 0) else Num1 := 0;
    if P2 > 1 then Num2 := StrToIntDef(Copy(V2, 1, P2 - 1), 0) else Num2 := 0;

    if Num1 < Num2 then
    begin
      Result := -1;
      Exit;
    end
    else if Num1 > Num2 then
    begin
      Result := 1;
      Exit;
    end;

    if P1 <= Length(V1) then V1 := Copy(V1, P1 + 1, Length(V1)) else V1 := '';
    if P2 <= Length(V2) then V2 := Copy(V2, P2 + 1, Length(V2)) else V2 := '';
  end;
end;

function GetVersionFromJson(FileName: string): string;
var
  Lines: TArrayOfString;
  I, P1, P2: Integer;
  Line: string;
begin
  Result := '';
  if LoadStringsFromFile(FileName, Lines) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Line := Lines[I];
      P1 := Pos('"Version"', Line);
      if P1 > 0 then
      begin
        P2 := Pos(':', Copy(Line, P1 + 9, Length(Line)));
        if P2 > 0 then
        begin
          Result := Trim(Copy(Line, P1 + 9 + P2, Length(Line)));
          StringChange(Result, '"', '');
          StringChange(Result, ',', '');
          Result := Trim(Result);
          Exit;
        end;
      end;
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if CurPageID = wpReady then
  begin
    if (not IsDotNet8DesktopRuntimeInstalled()) or (not IsVCRedistInstalled()) then
    begin
      DownloadPage.Show;
      try
        if not IsDotNet8DesktopRuntimeInstalled() then
        begin
          DownloadPage.Clear;
          DownloadPage.Add('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'dotnet-desktop-runtime.exe', '');
          try
            DownloadPage.SetText(ExpandConstant('{cm:DownloadingDotNet}'), '');
            DownloadPage.Download;
            DownloadPage.SetText(ExpandConstant('{cm:InstallingDotNet}'), '');
            
            if Exec(ExpandConstant('{tmp}\dotnet-desktop-runtime.exe'), '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
            begin
              if (ResultCode <> 0) and (ResultCode <> 1641) and (ResultCode <> 3010) then
              begin
                 MsgBox(ExpandConstant('{cm:InstallFailed}'), mbError, MB_OK);
              end;
            end
            else
            begin
              MsgBox(ExpandConstant('{cm:RuntimeExecFailed}'), mbError, MB_OK);
            end;
          except
            if not DownloadPage.AbortedByUser then
              MsgBox(ExpandConstant('{cm:DownloadFailed}'), mbError, MB_OK);
          end;
        end;

        if not IsVCRedistInstalled() then
        begin
          DownloadPage.Clear;
          DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
          try
            DownloadPage.SetText(ExpandConstant('{cm:DownloadingVC}'), '');
            DownloadPage.Download;
            DownloadPage.SetText(ExpandConstant('{cm:InstallingVC}'), '');
            
            if Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
            begin
              if (ResultCode <> 0) and (ResultCode <> 1641) and (ResultCode <> 3010) then
              begin
                 MsgBox(ExpandConstant('{cm:InstallFailedVC}'), mbError, MB_OK);
              end;
            end
            else
            begin
              MsgBox(ExpandConstant('{cm:RuntimeExecFailed}'), mbError, MB_OK);
            end;
          except
            if not DownloadPage.AbortedByUser then
              MsgBox(ExpandConstant('{cm:DownloadFailedVC}'), mbError, MB_OK);
          end;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  UninstStr: string;
  UninstKey: string;
  ResultCode: Integer;
  DesktopPath: string;
  ShortcutPath: string;
  AppPath: string;
  JsonPath: string;
  OldVersion: string;
  DoUninstall: Boolean;
begin
  Result := '';
  DesktopPath := ExpandConstant('{autodesktop}');
  ShortcutPath := AddBackslash(DesktopPath) + ExpandConstant('{#AppName}') + '.lnk';
  GDesktopIconExists := FileExists(ShortcutPath);

  if GDesktopIconExists and (IsTaskSelected('desktopicon') = False) then
    WizardSelectTasks('desktopicon');

  UninstKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' +
               ExpandConstant('{#SetupSetting("AppId")}') + '_is1';

  if not RegQueryStringValue(HKCU, UninstKey, 'UninstallString', UninstStr) then
    RegQueryStringValue(HKLM, UninstKey, 'UninstallString', UninstStr);

  if UninstStr <> '' then
  begin
    DoUninstall := True;

    if not RegQueryStringValue(HKCU, UninstKey, 'InstallLocation', AppPath) then
      if not RegQueryStringValue(HKLM, UninstKey, 'InstallLocation', AppPath) then
        if not RegQueryStringValue(HKCU, UninstKey, 'Inno Setup: App Path', AppPath) then
          RegQueryStringValue(HKLM, UninstKey, 'Inno Setup: App Path', AppPath);

    if AppPath <> '' then
    begin
      JsonPath := AddBackslash(AppPath) + 'Update.json';
      if FileExists(JsonPath) then
      begin
        OldVersion := GetVersionFromJson(JsonPath);
        if OldVersion <> '' then
        begin
          if CompareVersionStr(OldVersion, '1.4.2.1') > 0 then
            DoUninstall := False;
        end;
      end;
    end;

    if DoUninstall then
    begin
      UninstStr := RemoveQuotes(UninstStr);
      Exec(UninstStr, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
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
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
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