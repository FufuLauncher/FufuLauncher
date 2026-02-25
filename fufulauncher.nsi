# 定义基本安装信息
!define APP_NAME "芙芙启动器"
!define APP_VERSION "1.0.9.0"
!define APP_PUBLISHER "FufuLauncher"
!define APP_WEB_SITE "https://github.com/FufuLauncher/FufuLauncher"
!define APP_EXE "FufuLauncher.exe"

# 设置exe安装包的版本信息
VIProductVersion "${APP_VERSION}"
VIFileVersion "${APP_VERSION}"

Name ${APP_NAME}

# 设置输出安装包名称
OutFile "${APP_NAME}_Setup_v${APP_VERSION}.exe"

# 使用现代UI界面
!include "MUI2.nsh"

!include "x64.nsh"

# 定义安装目录
InstallDir "$PROGRAMFILES64\${APP_NAME}"

# 获取安装权限
RequestExecutionLevel admin

# 界面设置
!define MUI_ABORTWARNING
#!define MUI_ICON "install.ico"
#!define MUI_UNICON "uninstall.ico"

# 安装向导页面
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

# 卸载向导页面
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

# 设置界面语言
!insertmacro MUI_LANGUAGE "SimpChinese"

# 安装段
Section "主程序" SecMain
    # 设置注册表视图为64位
    SetRegView 64

    SetOutPath "$INSTDIR"
    
    File /r "C:\Users\zyizh\Documents\Temp\Extract\FufuLauncher\FufuLauncher\bin\x64\Release\net8.0-windows10.0.26100.0\*"
    
    # 创建开始菜单快捷方式
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\卸载.lnk" "$INSTDIR\Uninstall.exe"
    
    # 创建桌面快捷方式
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    
    # 写入注册表信息
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayName" "${APP_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" \
        "DisplayVersion" "${APP_VERSION}"
    
    # 写入安装信息
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

# 卸载段
Section "Uninstall"
    # 删除开始菜单快捷方式
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\卸载.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"
    
    # 删除桌面快捷方式
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    # 删除安装目录
    RMDir /r "$INSTDIR"
    
    # 删除注册表信息
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
SectionEnd

# 初始化函数
Function .onInit
    # 如果已安装，提示卸载
    ReadRegStr $R0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString"
    StrCmp $R0 "" done
    
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
        "${APP_NAME} 已安装。$\n$\n点击'确定'移除旧版本，或'取消'取消安装。" \
        IDOK uninst
    Abort

    # 在32位系统上运行时给出提示并退出
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "此应用不支持32位。"
        Abort
    ${EndIf}
    
    uninst:
        ClearErrors
        ExecWait '$R0 _?=$INSTDIR'
        
        IfErrors no_remove_uninstaller
        Goto done
        
    no_remove_uninstaller:
        # 如果卸载失败，继续安装
    done:
FunctionEnd