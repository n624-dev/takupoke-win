# WinUI画面・行選択・表示領域・実操作・再起動

対応ソース・テストのSHA-256：
`3ac3f742d53cc0030f525c8554f3f4e03f1dc51041b896ba25c3c335df4f85be`

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

- `CheckApplication`（宣言行 19）

## [tests/Takupoke.Win.UITests/Program.AuthenticationChecks.cs](../../tests/Takupoke.Win.UITests/Program.AuthenticationChecks.cs)

- `CheckAuthentication`（宣言行 23）

## [tests/Takupoke.Win.UITests/Program.ChangeRows.cs](../../tests/Takupoke.Win.UITests/Program.ChangeRows.cs)

- `CheckChangeRowSkips`（宣言行 13）

## [tests/Takupoke.Win.UITests/Program.InstallerChecks.cs](../../tests/Takupoke.Win.UITests/Program.InstallerChecks.cs)

- `CheckInstallerDisplay`（宣言行 19）

## [tests/Takupoke.Win.UITests/Program.TimetableChecks.cs](../../tests/Takupoke.Win.UITests/Program.TimetableChecks.cs)

- `CheckLessonFocusAcrossClock`（宣言行 23）
- `CheckTimetableMenuAcrossClock`（宣言行 44）

## [tests/Takupoke.Win.UITests/ShortcutReview.cs](../../tests/Takupoke.Win.UITests/ShortcutReview.cs)

- `CheckShortcut`（宣言行 10）
