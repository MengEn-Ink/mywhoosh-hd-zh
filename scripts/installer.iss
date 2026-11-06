#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef BundleDir
  #error BundleDir is required
#endif
#ifndef BuildOutput
  #error BuildOutput is required
#endif
#ifdef IsolatedTest
  #define ProductId "MyWhooshHdZh-IsolatedInstallerTest"
#else
  #define ProductId "MyWhooshHdZh-Desktop"
#endif

[Setup]
AppId={#ProductId}
AppName=MyWhoosh HD 中文助手
AppVersion={#AppVersion}
AppPublisher=MyWhoosh HD ZH contributors
AppPublisherURL=https://github.com/MengEn-Ink/mywhoosh-hd-zh
DefaultDirName={localappdata}\Programs\MyWhooshHdZh
DefaultGroupName=MyWhoosh HD 中文助手
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern
WizardResizable=yes
SetupIconFile=..\src\Whoosh.Gui\app.ico
UninstallDisplayIcon={app}\MyWhooshZH.exe
AppMutex=Local\MyWhooshHdZh.Desktop
CloseApplications=no
RestartApplications=no
OutputDir={#BuildOutput}
#ifdef IsolatedTest
OutputBaseFilename=installer-isolated-test
#else
OutputBaseFilename=mywhoosh-hd-zh-{#AppVersion}-win-x64-setup
#endif
Compression=lzma2
SolidCompression=yes
Uninstallable=yes
AllowRootDirectory=no
AllowUNCPath=no
UsePreviousAppDir=yes
InfoBeforeFile=installer-info.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
english.ButtonBack=< 上一步
english.ButtonNext=下一步 >
english.ButtonInstall=安装
english.ButtonOK=确定
english.ButtonCancel=取消
english.ButtonFinish=完成
english.SelectDirDesc=选择安装位置
english.SelectDirLabel1=中文助手应该安装到哪里？
english.SelectTasksDesc=选择快捷方式
english.SelectTasksLabel2=选择需要创建的快捷图标。
english.ReadyLabel1=准备安装到硬盘。
english.InfoBeforeLabel=请阅读安装与卸载说明。
english.WelcomeLabel1=欢迎安装 MyWhoosh HD 中文助手
english.WelcomeLabel2=将中文助手安装到本机硬盘，并创建快捷方式。%n%n安装程序不会自动汉化、修改窗口或启动游戏。使用助手时再明确选择操作。%n%n升级和卸载保留汉化备份，默认不设置开机自启。
english.SelectDirLabel3=选择安装目录。请使用独立程序文件夹，不要选择游戏、备份或存档目录。
english.FinishedHeadingLabel=中文助手安装完成
english.FinishedLabel=桌面和开始菜单可打开中文助手或快捷浮窗。关闭主面板会收起到托盘；从托盘选择退出才停止助手。%n%n卸载不恢复游戏，也不会删除汉化备份。需要回英文时请先在助手中操作。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷图标"; Flags: checkedonce

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installer-owner.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
#ifdef IsolatedTest
Name: "{param:TESTROOT}\shortcuts\MyWhoosh 中文助手"; Filename: "{app}\MyWhooshZH.exe"; WorkingDir: "{app}"
Name: "{param:TESTROOT}\shortcuts\MyWhoosh 快捷浮窗"; Filename: "{app}\MyWhooshZH.exe"; Parameters: "--floating"; WorkingDir: "{app}"
#else
Name: "{group}\MyWhoosh 中文助手"; Filename: "{app}\MyWhooshZH.exe"; WorkingDir: "{app}"
Name: "{group}\MyWhoosh 快捷浮窗"; Filename: "{app}\MyWhooshZH.exe"; Parameters: "--floating"; WorkingDir: "{app}"
Name: "{group}\卸载中文助手"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MyWhoosh 中文助手"; Filename: "{app}\MyWhooshZH.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autodesktop}\MyWhoosh 快捷浮窗"; Filename: "{app}\MyWhooshZH.exe"; Parameters: "--floating"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Run]
Filename: "{app}\MyWhooshZH.exe"; Description: "打开中文助手"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InitializeSetup(): Boolean;
begin
#ifdef IsolatedTest
  Result := (GetEnv('GITHUB_ACTIONS') = 'true') and (ExpandConstant('{param:TESTROOT|}') <> '');
#else
  Result := True;
#endif
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir, StateDir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    Dir := AddBackslash(Lowercase(ExpandConstant('{app}')));
    StateDir := AddBackslash(Lowercase(ExpandConstant('{localappdata}\MyWhooshHdZh')));
    if (Pos(StateDir, Dir) = 1) or (Pos('\windowsapps\', Dir) > 0) or (Pos('\mywhoosh\saved\', Dir) > 0) then begin
      MsgBox('不要把程序安装到游戏或汉化备份目录，请选一个独立文件夹。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, StateDir: String;
  Owner: AnsiString;
  Rec: TFindRec;
begin
  Result := '';
  Dir := AddBackslash(Lowercase(ExpandConstant('{app}')));
  StateDir := AddBackslash(Lowercase(ExpandConstant('{localappdata}\MyWhooshHdZh')));
  if (Pos(StateDir, Dir) = 1) or (Pos(Dir, StateDir) = 1) or (Pos('\windowsapps\', Dir) > 0) or (Pos('\packages\', Dir) > 0) or (Pos('\mywhoosh\saved\', Dir) > 0) then begin
    Result := '安装目录不能与游戏、账户数据或汉化备份目录重叠。';
    Exit;
  end;
  if DirExists(Dir) and not LoadStringFromFile(Dir + 'installer-owner.txt', Owner) then begin
    if FindFirst(Dir + '*', Rec) then begin
      try
        repeat
          if (Rec.Name <> '.') and (Rec.Name <> '..') then begin
            Result := '请选择空的独立文件夹，或已有中文助手的安装目录。';
            Exit;
          end;
        until not FindNext(Rec);
      finally
        FindClose(Rec);
      end;
    end;
  end else if DirExists(Dir) and (Trim(Owner) <> 'MyWhooshHdZh-Desktop') then
    Result := '安装目录归属不匹配。';
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('只卸载中文助手和快捷方式。汉化备份保留，游戏不会自动回英文。继续吗？', mbConfirmation, MB_YESNO) = IDYES;
end;
