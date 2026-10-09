# WinUI画面・行選択・表示領域・実操作・再起動

対応関係・宣言名・実行方法のSHA-256：
`beaa70ea20de9380a7979eead2b730f0e0de692d8c5905c1c418d260145dc833`

環境：Windows WinUI x64。Linuxクロスビルドは操作成功に数えない

```bash
./tests/Takupoke.Win.UITests/startup-smoke.ps1
```

## 変更時に確認するソース

- [src/Takupoke.Win/App.xaml](../../src/Takupoke.Win/App.xaml)
- [src/Takupoke.Win/App.xaml.cs](../../src/Takupoke.Win/App.xaml.cs)
- [src/Takupoke.Win/Assets/FluentIcons.xaml](../../src/Takupoke.Win/Assets/FluentIcons.xaml)
- [src/Takupoke.Win/Assets/FluentIcons/SOURCE.json](../../src/Takupoke.Win/Assets/FluentIcons/SOURCE.json)
- [src/Takupoke.Win/MainWindow.Analysis.cs](../../src/Takupoke.Win/MainWindow.Analysis.cs)
- [src/Takupoke.Win/MainWindow.ChangePreview.cs](../../src/Takupoke.Win/MainWindow.ChangePreview.cs)
- [src/Takupoke.Win/MainWindow.Contrast.cs](../../src/Takupoke.Win/MainWindow.Contrast.cs)
- [src/Takupoke.Win/MainWindow.Data.cs](../../src/Takupoke.Win/MainWindow.Data.cs)
- [src/Takupoke.Win/MainWindow.Documents.cs](../../src/Takupoke.Win/MainWindow.Documents.cs)
- [src/Takupoke.Win/MainWindow.Home.cs](../../src/Takupoke.Win/MainWindow.Home.cs)
- [src/Takupoke.Win/MainWindow.Links.cs](../../src/Takupoke.Win/MainWindow.Links.cs)
- [src/Takupoke.Win/MainWindow.PopupRendering.cs](../../src/Takupoke.Win/MainWindow.PopupRendering.cs)
- [src/Takupoke.Win/MainWindow.Presentation.cs](../../src/Takupoke.Win/MainWindow.Presentation.cs)
- [src/Takupoke.Win/MainWindow.Settings.cs](../../src/Takupoke.Win/MainWindow.Settings.cs)
- [src/Takupoke.Win/MainWindow.Timetable.cs](../../src/Takupoke.Win/MainWindow.Timetable.cs)
- [src/Takupoke.Win/MainWindow.xaml](../../src/Takupoke.Win/MainWindow.xaml)
- [src/Takupoke.Win/MainWindow.xaml.cs](../../src/Takupoke.Win/MainWindow.xaml.cs)
- [src/Takupoke.Win/Platform/BrowserAuthenticator.cs](../../src/Takupoke.Win/Platform/BrowserAuthenticator.cs)
- [src/Takupoke.Win/Platform/DesktopIntegration.cs](../../src/Takupoke.Win/Platform/DesktopIntegration.cs)
- [src/Takupoke.Win/Platform/OfflineTestNetwork.cs](../../src/Takupoke.Win/Platform/OfflineTestNetwork.cs)
- [src/Takupoke.Win/Platform/WindowsFileIdentity.cs](../../src/Takupoke.Win/Platform/WindowsFileIdentity.cs)
- [src/Takupoke.Win/Platform/WindowsNotifications.cs](../../src/Takupoke.Win/Platform/WindowsNotifications.cs)
- [src/Takupoke.Win/Program.cs](../../src/Takupoke.Win/Program.cs)
- [src/Takupoke.Win/ProtocolActivation.cs](../../src/Takupoke.Win/ProtocolActivation.cs)
- [src/Takupoke.Win/Takupoke.Win.csproj](../../src/Takupoke.Win/Takupoke.Win.csproj)
- [src/Takupoke.Win/ViewModels/AppViewModel.cs](../../src/Takupoke.Win/ViewModels/AppViewModel.cs)
- [tests/Takupoke.Win.UITests/Program.Controls.cs](../../tests/Takupoke.Win.UITests/Program.Controls.cs)
- [tests/Takupoke.Win.UITests/Program.Fixture.cs](../../tests/Takupoke.Win.UITests/Program.Fixture.cs)
- [tests/Takupoke.Win.UITests/Program.cs](../../tests/Takupoke.Win.UITests/Program.cs)
- [tests/Takupoke.Win.UITests/ScreenReview.cs](../../tests/Takupoke.Win.UITests/ScreenReview.cs)
- [tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj](../../tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj)
- [tests/Takupoke.Win.UITests/startup-smoke.ps1](../../tests/Takupoke.Win.UITests/startup-smoke.ps1)

## [tests/Takupoke.Win.UITests/Program.ApplicationChecks.cs](../../tests/Takupoke.Win.UITests/Program.ApplicationChecks.cs)

- `CheckApplication`

## [tests/Takupoke.Win.UITests/Program.AuthenticationChecks.cs](../../tests/Takupoke.Win.UITests/Program.AuthenticationChecks.cs)

- `CheckAuthentication`

## [tests/Takupoke.Win.UITests/Program.ChangeRows.cs](../../tests/Takupoke.Win.UITests/Program.ChangeRows.cs)

- `CheckChangeRowSkips`

## [tests/Takupoke.Win.UITests/Program.InstallerChecks.cs](../../tests/Takupoke.Win.UITests/Program.InstallerChecks.cs)

- `CheckInstallerDisplay`

## [tests/Takupoke.Win.UITests/Program.TimetableChecks.cs](../../tests/Takupoke.Win.UITests/Program.TimetableChecks.cs)

- `CheckLessonFocusAcrossClock`
- `CheckTimetableMenuAcrossClock`

## [tests/Takupoke.Win.UITests/ShortcutReview.cs](../../tests/Takupoke.Win.UITests/ShortcutReview.cs)

- `CheckShortcut`
