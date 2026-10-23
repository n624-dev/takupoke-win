# 通知・差分・実配信

対応関係・宣言名・実行方法のSHA-256：
`1cb30e1f88ff60bf6298fdf0ec96e383715cb116ec339efeb36ac25ad1ea5094`

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

## [tests/Takupoke.Integration.Tests/AppLifecycleTests.cs](../../tests/Takupoke.Integration.Tests/AppLifecycleTests.cs)

- `IncompatibleLinksCacheStillDisplaysIndependentSavedLessons`
- `EventDirectoryEnumerationFailureDoesNotHideLessonsAndCanBeRetried`
- `UnlockDuringUnfinishedReadReloadsAfterCancellationWithEitherTraySetting`
- `FinishingBeforeUnlockAndRemainingLockedNeverApplyTheCancelledSnapshot`
- `ExplicitStopDiscardsTheQueuedUnlockRefreshUntilExplicitResume`
- `QueuedUnlockUsesTheNewRetentionLeaseAndKeepsTheOriginalFile`
- `SamePeriodStartupReconcilesPendingWithoutClearingAcceptedOsHistory`
- `AcceptedNoticeWithFailedPendingSaveIsAcknowledgedOnRestartWithoutResending`
- `ActualRetentionChangeClearsOsHistoryAndOldPending`
- `RetentionClearStillRunsWhenTheOldOperationIsCancelledBeforeReload`
- `ActualSelectionHandlerPreservesRoundTripsDuringNotificationWaitAndRestart`
- `SelectionSaveFailureKeepsPreferencesAndCanBeRetriedAfterRebuildingTheControl`
- `CorruptRetentionSuspendsPrivateViewsWithoutClearingOsHistoryUntilConfirmedRecovery`
- `SelectedSpecialWithoutFirstAnalysisRequiresAttentionOnBothScheduleViews`
