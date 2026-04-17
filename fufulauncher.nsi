!define APP_NAME "FufuLauncher"
!define APP_VERSION "1.1.0.2"
!define APP_PUBLISHER "FufuLauncher"
!define APP_WEB_SITE "https://github.com/FufuLauncher/FufuLauncher"
!define APP_EXE "FufuLauncher.exe"
!define SOURCE_DIR ".\FufuLauncher\bin\x64\Release\net8.0-windows10.0.26100.0"

VIProductVersion "${APP_VERSION}"
VIFileVersion "${APP_VERSION}"

Name ${APP_NAME}

OutFile "${APP_NAME}_Setup_v${APP_VERSION}.exe"

!include "MUI2.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; 采用标准的 Windows 用户级程序安装路径
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
RequestExecutionLevel user

ManifestDPIAware true

!define MUI_ABORTWARNING
!define MUI_ICON "install.ico"
!define MUI_UNICON "uninstall.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; 定义检测并结束进程的宏
!macro CheckAndKillProcess UN
    LoopCheck_${UN}:
        ; 使用 tasklist 查找进程，将结果压入堆栈
        nsExec::ExecToStack 'cmd /c tasklist /NH /FI "IMAGENAME eq ${APP_EXE}" | find /I "${APP_EXE}"'
        Pop $0 ; 获取执行状态（0 表示找到进程，1 表示未找到）
        Pop $1 ; 获取命令输出内容
        
        StrCmp $0 "0" ProcessFound_${UN} ProcessNotFound_${UN}

    ProcessFound_${UN}:
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "检测到 ${APP_NAME} 正在运行。$\n$\n点击“确定”以终止进程并继续，或点击“取消”退出。" IDOK KillProcess_${UN} IDCANCEL CancelInstall_${UN}

    KillProcess_${UN}:
        ; 终止进程
        nsExec::ExecToStack 'taskkill /F /IM ${APP_EXE} /T'
        Sleep 1000 ; 等待进程完全退出
        Goto LoopCheck_${UN} ; 再次检查以确保进程已结束

    CancelInstall_${UN}:
        Abort

    ProcessNotFound_${UN}:
!macroend

Section "主程序" SecMain
    SectionIn RO

    SetRegView 64
    SetOutPath "$INSTDIR"
    
    File /r "${SOURCE_DIR}\*"
    
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayVersion" "${APP_VERSION}"
    
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "桌面快捷方式" SecDesktop
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section "开始菜单快捷方式" SecStartMenu
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\卸载.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "开机自启动" SecAutoStart
    SetRegView 64
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" \
        "${APP_NAME}" '"$INSTDIR\${APP_EXE}"'
SectionEnd

Section /o "固定到任务栏" SecTaskbar
    nsExec::ExecToStack 'powershell.exe -NoProfile -WindowStyle Hidden -Command "(New-Object -ComObject Shell.Application).NameSpace(''$INSTDIR'').ParseName(''${APP_EXE}'').InvokeVerb(''taskbarpin'')"'
SectionEnd

Section "Uninstall"
    SetRegView 64
    
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\卸载.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"
    
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    ; 此命令仅移除安装目录（Programs\FufuLauncher），不影响 LocalAppData\FufuLauncher 配置文件
    RMDir /r "$INSTDIR"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
    
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP_NAME}"
SectionEnd

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "此应用不支持32位系统。"
        Abort
    ${EndIf}
  
    ; 安装前检测进程
    !insertmacro CheckAndKillProcess ""

    SetRegView 64

    ; 仅检测当前用户的注册表键值
    ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString"
    StrCmp $R0 "" done

    ; 删除了无用的 FoundInstallation 标签声明
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
        "${APP_NAME} 已安装。$\n$\n点击“确定”移除旧版本，或“取消”取消安装。" \
        IDOK uninst
    Abort

    uninst:
        ClearErrors
    
        ${GetParent} $R0 $R1

        ExecWait '$R0 _?=$R1'
        IfErrors no_remove_uninstaller
       
        Goto done
        
    no_remove_uninstaller:
    done:

FunctionEnd

Function un.onInit
    ; 卸载前检测进程
    !insertmacro CheckAndKillProcess "un"
FunctionEnd