# 通知・差分・実配信

対応関係・宣言名・実行方法のSHA-256：
`98dce40fca74bb09b77f8243587b48803f00356cc8b8ec2aeb490cd9b87ba147`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/NotificationDiff.cs](../../src/Takupoke.Core/NotificationDiff.cs)
- [src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs](../../src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs)

## [tests/Takupoke.Core.Tests/NotificationDiffTests.cs](../../tests/Takupoke.Core.Tests/NotificationDiffTests.cs)

- `InitialImportSavesBaselineWithoutNotification`
- `ReorderingDuplicatesAndClassSelectionAloneDoNotNotify`
- `ChangedSameSlotCountsOnceAndIgnoresPastOrOtherClasses`
- `OnlySuccessfulSpecialAnalysisDigestsAreComparedAndDisabledKindsClearPending`
- `PendingChangesAreFilteredByCurrentClassesAndDateWithoutChangingTheStableOsTag`
- `LegacyChangeNoticeIsDiscardedButComparisonBaselineRemainsUsable`
- `UnsentSlotsAccumulateWithoutCountingRepeatedUpdatesToTheSameSlotTwice`
- `PartialStartupPreservesPersistedPendingButConfirmedAbsenceInvalidatesIt`

## [tests/Takupoke.Integration.Tests/NotificationCoordinatorTests.cs](../../tests/Takupoke.Integration.Tests/NotificationCoordinatorTests.cs)

- `FirstImportAndClassSwitchDoNotNotifyButChangedSelectedSlotDoes`
- `FailedSendSurvivesStoreRestartAndIsSentOnlyOnce`
- `PreviouslyAcceptedOsNoticeIsAcknowledgedWithoutResending`
- `OsPermissionDisabledUpdatesBaselineAndClearsPendingAndDelivered`
- `CancellationAfterOsAcceptsDoesNotLeaveNoticePending`
- `FailedChangeSendIsDiscardedAfterClassSwitchOrTheTargetDayPasses`
- `RetryRecountsOnlyRemainingSelectedSlotsAndUsesTheOriginalOsTag`
- `LegacyChangeNoticeWithoutTargetsIsDiscardedAndDoesNotBreakNewNotifications`
- `FailedSpecialPdfNoticeRemainsEligibleAfterClassAndDayChanges`
- `PersistedThreeKindPendingSurvivesEveryPartialLoadOrderWithoutStaleDelivery`
