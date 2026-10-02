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
AppName=たくポケ Win 開発版
AppVersion={#AppVersion}
AppPublisher=n624-dev
AppPublisherURL=https://github.com/n624-dev/takupoke-win
AppSupportURL=https://github.com/n624-dev/takupoke-win/issues
AppUpdatesURL=https://github.com/n624-dev/takupoke-win/releases
DefaultDirName={localappdata}\Programs\TakupokeWin
DefaultGroupName=たくポケ Win
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
OutputBaseFilename=TakupokeWin-{#AppVersion}-win-{#TargetArch}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=development-info.txt
UninstallDisplayIcon={app}\Takupoke.Win.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.UpdateTitle=たくポケ Winを更新
japanese.ReinstallTitle=たくポケ Winを再インストール
japanese.UpdateButton=更新する
japanese.ReinstallButton=再インストール
japanese.InstalledVersion=現在のバージョン
japanese.NewVersion=インストールするバージョン
english.UpdateTitle=Update Takupoke Win
english.ReinstallTitle=Reinstall Takupoke Win
english.UpdateButton=Update
english.ReinstallButton=Reinstall
english.InstalledVersion=Installed version
english.NewVersion=Version to install

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
#ifdef WebViewBootstrapper
Source: "{#WebViewBootstrapper}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView
#endif

[Icons]
Name: "{userprograms}\たくポケ Win"; Filename: "{app}\Takupoke.Win.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win"; ValueType: string; ValueData: "たくポケ Win 認証"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\jp.n624.takupoke.win\shell\open\command"; ValueType: string; ValueData: """{app}\Takupoke.Win.exe"" ""----ms-protocol:%1"""

[Run]
#ifdef WebViewBootstrapper
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "WebView2 Runtimeを準備しています…"; Flags: waituntilterminated; Check: NeedsWebView
#endif
Filename: "{app}\Takupoke.Win.exe"; Description: "たくポケ Winを起動"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Takupoke.Win.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var PreviousVersion: String;

function InitializeSetup: Boolean;
begin
  PreviousVersion := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{00C9D0A0-362C-4F7A-93E7-C25D5160C279}_is1', 'DisplayVersion', PreviousVersion);
  Result := True;
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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TakupokeWin', Command) then
      if Pos(Lowercase(ExpandConstant('{app}\Takupoke.Win.exe')), Lowercase(Command)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TakupokeWin');
end;
