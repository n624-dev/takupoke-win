#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef TargetArch
  #error TargetArch is required
#endif

[Setup]
AppId={{00C9D0A0-362C-4F7A-93E7-C25D5160C279}
AppName=たくポケ
AppVersion={#AppVersion}
AppPublisher=n624-dev
AppPublisherURL=https://github.com/n624-dev/takupoke-win
AppSupportURL=https://github.com/n624-dev/takupoke-win/issues
AppUpdatesURL=https://github.com/n624-dev/takupoke-win/releases
DefaultDirName={localappdata}\Programs\takupoke
UsePreviousAppDir=no
DefaultGroupName=たくポケ
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
MinVersion=10.0.17763
#if TargetArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputDir={#OutputDir}
OutputBaseFilename=takupoke-{#AppVersion}-{#TargetArch}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=development-info.txt
SetupIconFile={#PublishDir}\Assets\takupoke.ico
UninstallDisplayName=たくポケ
UninstallDisplayIcon={app}\takupoke.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.UpdateTitle=たくポケを更新
japanese.ReinstallTitle=たくポケを再インストール
japanese.UpdateButton=更新する
japanese.ReinstallButton=再インストール
japanese.InstalledVersion=現在のバージョン
japanese.NewVersion=インストールするバージョン
japanese.MoveFailed=以前のたくポケを整理できませんでした。アプリを完全に終了して、もう一度実行してください。
english.UpdateTitle=Update たくポケ
english.ReinstallTitle=Reinstall たくポケ
english.UpdateButton=Update
english.ReinstallButton=Reinstall
english.InstalledVersion=Installed version
english.NewVersion=Version to install
english.MoveFailed=The previous installation of たくポケ could not be removed. Fully exit the app and try again.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
#ifdef WebViewBootstrapper
Source: "{#WebViewBootstrapper}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView
#endif

[InstallDelete]
; Remove only the start-menu shortcut created under the previous product name.
Type: files; Name: "{userprograms}\たくポケ Win.lnk"

[Icons]
Name: "{userprograms}\たくポケ"; Filename: "{app}\takupoke.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\takupoke.ico"

[Registry]
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win"; ValueType: string; ValueData: "たくポケ"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win\shell\open\command"; ValueType: string; ValueData: """{app}\takupoke.exe"" ""----ms-protocol:%1"""

[Run]
#ifdef WebViewBootstrapper
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "WebView2 Runtimeを準備しています…"; Flags: waituntilterminated; Check: NeedsWebView
#endif
Filename: "{app}\takupoke.exe"; Description: "たくポケを起動"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\takupoke.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  PreviousVersion: String;
  PreviousInstallDir: String;
  PreviousUninstaller: String;
  PreviousStartupCommand: String;
  MigrateStartup: Boolean;

function OwnStartupCommand(const Command, Directory: String): Boolean;
begin
  { Only the two commands actually written by released apps are supported. }
  Result := (CompareText(Command, '"' + AddBackslash(Directory) + 'takupoke.exe" --background') = 0) or
    (CompareText(Command, '"' + AddBackslash(Directory) + 'Takupoke.Win.exe" --background') = 0);
end;

function InitializeSetup: Boolean;
begin
  PreviousVersion := '';
  PreviousInstallDir := '';
  PreviousUninstaller := '';
  PreviousStartupCommand := '';
  MigrateStartup := False;
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{00C9D0A0-362C-4F7A-93E7-C25D5160C279}_is1', 'DisplayVersion', PreviousVersion);
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{00C9D0A0-362C-4F7A-93E7-C25D5160C279}_is1', 'InstallLocation', PreviousInstallDir);
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{00C9D0A0-362C-4F7A-93E7-C25D5160C279}_is1', 'UninstallString', PreviousUninstaller);
  if (PreviousVersion <> '') and (PreviousInstallDir <> '') then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'takupoke', PreviousStartupCommand) then
      MigrateStartup := OwnStartupCommand(PreviousStartupCommand, PreviousInstallDir);
    if not MigrateStartup then
      if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TakupokeWin', PreviousStartupCommand) then
        MigrateStartup := OwnStartupCommand(PreviousStartupCommand, PreviousInstallDir);
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Uninstaller: String; ExitCode: Integer;
begin
  Result := '';
  if (PreviousVersion <> '') and (PreviousInstallDir <> '') and
     ((CompareText(AddBackslash(PreviousInstallDir), AddBackslash(WizardDirValue)) <> 0) or
      FileExists(AddBackslash(PreviousInstallDir) + 'Takupoke.Win.exe')) then begin
    { Let the registered installer remove only its own files; keep unknown files and app data. }
    Uninstaller := RemoveQuotes(PreviousUninstaller);
    if (CompareText(AddBackslash(ExtractFileDir(Uninstaller)), AddBackslash(PreviousInstallDir)) <> 0) or
       not FileExists(Uninstaller) then begin
      Result := ExpandConstant('{cm:MoveFailed}');
      Exit;
    end;
    if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', PreviousInstallDir, SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := ExpandConstant('{cm:MoveFailed}')
    else if ExitCode <> 0 then Result := ExpandConstant('{cm:MoveFailed}');
    if Result = '' then PreviousInstallDir := '';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Command, NewCommand: String;
begin
  if (CurStep = ssPostInstall) and MigrateStartup then begin
    NewCommand := '"' + ExpandConstant('{app}\takupoke.exe') + '" --background';
    { An unrelated value with the same name must never be replaced. }
    if not RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'takupoke') or
       (RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'takupoke', Command) and
        ((CompareText(Command, PreviousStartupCommand) = 0) or (CompareText(Command, NewCommand) = 0))) then
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'takupoke', NewCommand);
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TakupokeWin', Command) then
      if CompareText(Command, PreviousStartupCommand) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TakupokeWin');
  end;
end;

function UpgradeTitle: String;
begin
  if PreviousVersion = '{#AppVersion}' then Result := ExpandConstant('{cm:ReinstallTitle}')
  else Result := ExpandConstant('{cm:UpdateTitle}');
end;

procedure InitializeWizard;
begin
  if PreviousVersion <> '' then begin
    WizardForm.Caption := UpgradeTitle;
    WizardForm.WelcomeLabel1.Caption := UpgradeTitle;
    WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:InstalledVersion}') + ': ' + PreviousVersion + #13#10 +
      ExpandConstant('{cm:NewVersion}') + ': {#AppVersion}';
    WizardForm.ReadyLabel.Caption := UpgradeTitle;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (PreviousVersion <> '') and (CurPageID = wpReady) then
    if PreviousVersion = '{#AppVersion}' then WizardForm.NextButton.Caption := ExpandConstant('{cm:ReinstallButton}')
    else WizardForm.NextButton.Caption := ExpandConstant('{cm:UpdateButton}');
end;

function HasWebView(Root: Integer; const Key: String): Boolean;
var Version: String;
begin
  Result := RegQueryStringValue(Root, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function NeedsWebView: Boolean;
begin
  Result := not (HasWebView(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    HasWebView(HKCU32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'));
end;

procedure RemoveOwnStartup(const Name: String);
var Command: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', Name, Command) then
    if CompareText(Command, '"' + ExpandConstant('{app}\takupoke.exe') + '" --background') = 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', Name);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    RemoveOwnStartup('takupoke');
    RemoveOwnStartup('TakupokeWin');
  end;
end;
