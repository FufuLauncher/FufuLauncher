!define APP_NAME "芙芙启动器"
!define APP_VERSION "1.0.9.0"
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

InstallDir "$LOCALAPPDATA\${APP_NAME}"
RequestExecutionLevel admin

ManifestDPIAware true

# 界面设置
!define MUI_ABORTWARNING
!define MUI_ICON "install.ico"
!define MUI_UNICON "uninstall.ico"

# 安装向导页面
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

# 卸载向导页面
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

# 主程序部分
Section "主程序" SecMain
    SectionIn RO

    # 设置注册表视图为64位
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
    
    # 写入安装信息
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

# 桌面快捷方式（可选）
Section "桌面快捷方式" SecDesktop
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

# 开始菜单快捷方式（可选）
Section "开始菜单快捷方式" SecStartMenu
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\卸载.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
    SetRegView 64
    
    # 删除开始菜单快捷方式
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\卸载.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"
    
    # 删除桌面快捷方式
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    RMDir /r "$INSTDIR"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
SectionEnd

Function .onInit
    # 检查是否为64位系统
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "此应用不支持32位系统。"
        Abort
    ${EndIf}
    
    # 如果已安装，提示卸载
    SetRegView 64

    ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString"
    StrCmp $R0 "" CheckHKLM

    FoundInstallation:
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
        "${APP_NAME} 已安装。$\n$\n点击'确定'移除旧版本，或'取消'取消安装。" \
        IDOK uninst
    Abort

    CheckHKLM:
    ReadRegStr $R0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString"
    StrCmp $R0 "" done
    Goto FoundInstallation

    uninst:
        ClearErrors
        ${GetParent} $R0 $R1

        ExecWait '$R0 _?=$R1'
        IfErrors no_remove_uninstaller
        Goto done
        
    no_remove_uninstaller:
        # 如果卸载失败，继续安装
    done:

FunctionEnd


