# 通知・差分・実配信

対応ソース・テストのSHA-256：
`329d1d95b23d1aedab3171fe72740f4da1e68d71932e3804993822ae32ad1576`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/NotificationDiff.cs](../../src/Takupoke.Core/NotificationDiff.cs)
- [src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs](../../src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs)

## [tests/Takupoke.Core.Tests/NotificationDiffTests.cs](../../tests/Takupoke.Core.Tests/NotificationDiffTests.cs)

- `InitialImportSavesBaselineWithoutNotification`（宣言行 11）
- `ReorderingDuplicatesAndClassSelectionAloneDoNotNotify`（宣言行 18）
- `ChangedSameSlotCountsOnceAndIgnoresPastOrOtherClasses`（宣言行 26）
- `OnlySuccessfulSpecialAnalysisDigestsAreComparedAndDisabledKindsClearPending`（宣言行 34）
- `PendingChangesAreFilteredByCurrentClassesAndDateWithoutChangingTheStableOsTag`（宣言行 42）
- `LegacyChangeNoticeIsDiscardedButComparisonBaselineRemainsUsable`（宣言行 57）
- `UnsentSlotsAccumulateWithoutCountingRepeatedUpdatesToTheSameSlotTwice`（宣言行 68）

## [tests/Takupoke.Integration.Tests/NotificationCoordinatorTests.cs](../../tests/Takupoke.Integration.Tests/NotificationCoordinatorTests.cs)

- `FirstImportAndClassSwitchDoNotNotifyButChangedSelectedSlotDoes`（宣言行 44）
- `FailedSendSurvivesStoreRestartAndIsSentOnlyOnce`（宣言行 57）
- `PreviouslyAcceptedOsNoticeIsAcknowledgedWithoutResending`（宣言行 76）
- `OsPermissionDisabledUpdatesBaselineAndClearsPendingAndDelivered`（宣言行 86）
- `CancellationAfterOsAcceptsDoesNotLeaveNoticePending`（宣言行 99）
- `FailedChangeSendIsDiscardedAfterClassSwitchOrTheTargetDayPasses`（宣言行 109）
- `RetryRecountsOnlyRemainingSelectedSlotsAndUsesTheOriginalOsTag`（宣言行 137）
- `LegacyChangeNoticeWithoutTargetsIsDiscardedAndDoesNotBreakNewNotifications`（宣言行 157）
- `FailedSpecialPdfNoticeRemainsEligibleAfterClassAndDayChanges`（宣言行 172）
