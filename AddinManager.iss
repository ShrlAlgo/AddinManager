; 脚本由 Inno Setup 脚本向导生成。
; 有关创建 Inno Setup 脚本文件的详细信息，请参阅帮助文档！

; 定义应用程序的名称
#define MyAppName "RevitAddinManager"
; 定义应用程序的版本号
#define MyAppVersion "2.0.0"
; 定义应用程序的发布者
#define MyAppPublisher "ShrlAlgo"
; 定义应用程序的网址
#define MyAppURL "https://www.ShrlAlgo.cn/"
; 定义应用程序的可执行文件名
#define MyAppExeName "MyProg-x64.exe"
#define MyDllName "AddinManager"

[Setup]
; 注意：AppId 的值唯一标识此应用程序。不要在其他应用程序的安装程序中使用相同的 AppId 值。
; (若要生成新的 GUID，请在 IDE 中单击 "工具|生成 GUID"。)
AppId={{C8434509-9BC8-4896-BD4B-9CD9483D1958}
; 程序名称
AppName={#MyAppName}
; 版本
AppVersion={#MyAppVersion}
; AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
;控制面板显示的名称
UninstallDisplayName={#MyAppName}
;安装包构建模型
ArchitecturesInstallIn64BitMode=x64os
;管理员权限
PrivilegesRequired=admin
; 是否创建应用程序目录
CreateAppDir=no
; 许可证文件路径
;LicenseFile=.\bin\Release\2018\license.txt
; 安装前显示的信息文件路径
;InfoBeforeFile=.\bin\Release\2018\Readme.txt
; 安装后显示的信息文件路径
;InfoAfterFile=.\bin\Release\2018\Readme.txt
; 取消对以下行的注释以在非管理安装模式下运行(仅针对当前用户进行安装)。
; PrivilegesRequired=lowest
; exe输出目录
OutputDir=.\Setup
; 输出文件名
OutputBaseFilename={#MyAppName}{#MyAppVersion}
; 密码
;Password=SZMEDI
; 是否加密

;Encryption=yes
; 压缩方式
Compression=lzma
;Compression=zip
; 是否使用固体压缩
SolidCompression=yes
; 向导样式
WizardStyle=modern

[Files]
; 源文件路径和目标目录
Source: ".\AddinManager\bin\Release\*"; DestDir: "C:\ProgramData\Autodesk\ApplicationPlugins\RevitAddinManager.bundle"; Flags: ignoreversion recursesubdirs createallsubdirs
; 注意：不要在任何共享系统文件上使用 "Flags: ignoreversion" 