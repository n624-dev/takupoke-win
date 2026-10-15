# 原画像確認・本文訂正・モデル設定・動的詳細

対応関係・宣言名・実行方法のSHA-256：
`91a062ea825f5fb15a3b78b5fcc97ce3d458180815fe89353166acf5c05d3434`

環境：Windows WinUI x64。クロスビルドは操作成功に数えない

```bash
pwsh ./tests/Takupoke.Win.UITests/startup-smoke.ps1
```

## 変更時に確認するソース

- [src/Takupoke.Win/MainWindow.DetailRevision.cs](../../src/Takupoke.Win/MainWindow.DetailRevision.cs)
- [src/Takupoke.Win/MainWindow.Recovery.cs](../../src/Takupoke.Win/MainWindow.Recovery.cs)
- [src/Takupoke.Win/Platform/FoundryLocalRecoveryProvider.cs](../../src/Takupoke.Win/Platform/FoundryLocalRecoveryProvider.cs)
- [src/Takupoke.Win/Platform/FoundryPinnedModelStore.cs](../../src/Takupoke.Win/Platform/FoundryPinnedModelStore.cs)
- [src/Takupoke.Win/Platform/WindowsLanguageRecoveryProvider.cs](../../src/Takupoke.Win/Platform/WindowsLanguageRecoveryProvider.cs)
- [src/Takupoke.Win/Platform/WindowsPdfRecovery.cs](../../src/Takupoke.Win/Platform/WindowsPdfRecovery.cs)
- [src/Takupoke.Win/Platform/WindowsRecoveryModels.cs](../../src/Takupoke.Win/Platform/WindowsRecoveryModels.cs)
- [src/Takupoke.Win/ViewModels/RecoveryCorrectionLayout.cs](../../src/Takupoke.Win/ViewModels/RecoveryCorrectionLayout.cs)
- [src/Takupoke.Win/ViewModels/RecoveryManualInputState.cs](../../src/Takupoke.Win/ViewModels/RecoveryManualInputState.cs)
- [src/Takupoke.Win/ViewModels/RecoveryPreviewDisplay.cs](../../src/Takupoke.Win/ViewModels/RecoveryPreviewDisplay.cs)
- [tests/Takupoke.Win.UITests/PreferenceSnapshot.cs](../../tests/Takupoke.Win.UITests/PreferenceSnapshot.cs)
- [tests/Takupoke.Win.UITests/Program.LocalAiShared.cs](../../tests/Takupoke.Win.UITests/Program.LocalAiShared.cs)

## [tests/Takupoke.Win.UITests/Program.DetailUpdates.cs](../../tests/Takupoke.Win.UITests/Program.DetailUpdates.cs)

- `CheckDetailSnapshotUpdates`

## [tests/Takupoke.Win.UITests/Program.ManualRecovery.cs](../../tests/Takupoke.Win.UITests/Program.ManualRecovery.cs)

- `CheckManualRecoveryUi`
- `CheckRoomCandidatesUi`
- `CheckHeaderRecoveryUi`
- `CheckNativeSourceRecoveryUi`
- `CheckBatchRecoveryUi`
- `CheckConfidentRecoveryReviewUi`

## [tests/Takupoke.Win.UITests/Program.Recovery.cs](../../tests/Takupoke.Win.UITests/Program.Recovery.cs)

- `CheckRecoverySpecialUi`
- `CheckRecoveryParallelUi`
- `CheckRecoveryUi`
- `CheckRecoveryLatestAcquisitionFailure`

## [tests/Takupoke.Win.UITests/Program.Settings.cs](../../tests/Takupoke.Win.UITests/Program.Settings.cs)

- `CheckAnalysisFilterKeyboardRoundTrip`
