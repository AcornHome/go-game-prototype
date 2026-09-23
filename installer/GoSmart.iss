; 中国围棋 V1.0 安装脚本
; ==========================================
;
; 使用方法：
;   1. 先跑 deploy.py 生成 dist/ 目录
;   2. 用 Inno Setup 6 编译本脚本：
;      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" GoSmart.iss
;   3. 输出 ChinaGoSetup-1.0.exe（约 120MB，包含 KataGo 引擎）
;
; 注意：本脚本假设：
;   - REPO = C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype
;   - 已经运行过 .tools\deploy.py
;
; 玩家机器只需装 .NET 10 Desktop Runtime + VC++ 2015-2022 Redistributable（KataGo 依赖）

#define MyAppName "ChinaGo"
#define MyAppNameEn "ChinaGo"
#define MyAppVersion "1.5.3"
#define MyAppPublisher "ChinaGo"
#define MyAppURL "https://example.com/chinago"
#define MyAppExeName "GoGame.exe"
#define MyAppCopyright "Copyright (C) 2026 ChinaGo"

#define RepoRoot "C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype"
#define DistDir SourcePath + "..\..\go-game-prototype\dist"

[Setup]
; 安装包唯一标识（生成一次后永不改动，否则 Windows 无法识别升级）
AppId={{A8F7E1D9-3B2C-4F8E-9D1A-7B6C5D4E2F3A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile={#RepoRoot}\dist-licenses\KataGo\License.txt
InfoBeforeFile={#RepoRoot}\dist-licenses\THIRD-PARTY-NOTICES.txt
OutputDir={#RepoRoot}\installer
OutputBaseFilename=ChinaGoSetup-{#MyAppVersion}
SetupIconFile={#RepoRoot}\installer\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Languages\English.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; --- 主程序 ---
Source: "{#RepoRoot}\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\dist\GoGame.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\dist\GoGame.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\dist\GoGame.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; --- 程序图标（必须放在安装目录，否则桌面/卸载项图标回退成默认 exe 图标）---
Source: "{#RepoRoot}\installer\app.ico"; DestDir: "{app}"; Flags: ignoreversion

; --- 合规材料 ---
Source: "{#RepoRoot}\dist\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

; --- KataGo 引擎（108MB 整目录）---
; 注意：必须用 Flags: recursesubdirs 保留 weights/ 子目录
Source: "{#RepoRoot}\dist\KataGo\*"; DestDir: "{app}\KataGo"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Check: KataGoSourceExists

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; \
    Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
    Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 删除玩家存档（棋谱库），保留以防误删
Type: filesandordirs; Name: "{userdocs}\ChinaGo"
; 不删除 KataGo 配置（默认配置即可）

[Code]
// 检查 KataGo 源目录是否存在（dist 已生成）
function KataGoSourceExists(): Boolean;
begin
  Result := DirExists(ExpandConstant('{#RepoRoot}\dist\KataGo'));
  if not Result then
    MsgBox('找不到 KataGo 引擎目录：' + ExpandConstant('{#RepoRoot}\dist\KataGo') + #13#10 +
           '请先运行 .tools\deploy.py 同步引擎后再打包。',
           mbCriticalError, MB_OK);
end;

// 安装前检查 .NET 10 Desktop Runtime
// 同时检查注册表（x64/x86）和文件系统（全局安装/用户级安装），避免漏判
function FindDotNet10InDir(DirPath: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(DirPath) then
    Exit;
  if FindFirst(DirPath + '\10.*', FindRec) then
  begin
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
    FindClose(FindRec);
  end;
end;

function IsDotNetInstalled(): Boolean;
var
  RegKey: String;
  Found: Boolean;
begin
  Found := False;

  // 1) 注册表：x64 全局安装
  RegKey := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\shared\Microsoft.WindowsDesktop.App';
  if RegKeyExists(HKLM64, RegKey) or RegKeyExists(HKCU64, RegKey) then
    Found := True;

  // 2) 注册表：x86 / WOW6432Node
  RegKey := 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\shared\Microsoft.WindowsDesktop.App';
  if RegKeyExists(HKLM32, RegKey) or RegKeyExists(HKCU32, RegKey) then
    Found := True;

  // 3) 文件系统兜底：全局安装目录
  if not Found then
    Found := FindDotNet10InDir(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  if not Found then
    Found := FindDotNet10InDir(ExpandConstant('{pf32}\dotnet\shared\Microsoft.WindowsDesktop.App'));

  // 4) 文件系统兜底：用户级安装目录（dotnet-install.ps1 默认路径）
  if not Found then
    Found := FindDotNet10InDir(ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App'));

  Result := Found;
end;

function InitializeSetup(): Boolean;
var
  ErrCode: Integer;
begin
  Result := True;
  if not IsDotNetInstalled() then
  begin
    if MsgBox('本程序需要 .NET 10 Desktop Runtime。' + #13#10 +
              '是否打开官方下载页面？' + #13#10 + #13#10 +
              '下载后请重新运行本安装程序。',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
      Result := False;
    end;
  end;
end;