# 通知・差分・実配信

対応関係・宣言名・実行方法のSHA-256：
`3c4d166d5aa85461cd119513996be54cb540a86aadc77190f224f025f9b63a0b`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
dotnet test tests/Takupoke.Platform.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/NotificationDiff.cs](../../src/Takupoke.Core/NotificationDiff.cs)
- [src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs](../../src/Takupoke.Infrastructure/Notifications/NotificationCoordinator.cs)
- [tests/Takupoke.Platform.Tests/Takupoke.Platform.Tests.csproj](../../tests/Takupoke.Platform.Tests/Takupoke.Platform.Tests.csproj)
- [tests/Takupoke.Platform.Tests/NotificationApiFakes.cs](../../tests/Takupoke.Platform.Tests/NotificationApiFakes.cs)
- [src/Takupoke.Win/Platform/WindowsNotifications.cs](../../src/Takupoke.Win/Platform/WindowsNotifications.cs)

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
- `ActualNotificationToggleKeepsTheLastEditDuringAnEarlierSave`
- `CompletingAnEarlierYearSaveKeepsANewerUnsubmittedDraft`
- `StartupRegistrationRollsBackIfThePreferenceFileCannotBeSaved`
- `StartupFailureRestoresTheOldStateOrRetainsAnExplicitWarning`
- `FailedYearSaveKeepsItsDraftAndSuccessfulUnchangedSaveClearsIt`
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

## [tests/Takupoke.Platform.Tests/WindowsNotificationsTests.cs](../../tests/Takupoke.Platform.Tests/WindowsNotificationsTests.cs)

- `RealDeliverySuccessRemainsInStatusWhileTheOsSettingIsReadAgain`
- `DeliveryRejectionOrExceptionIsDisplayedAndSuccessReplacesIt`
- `UnsupportedAndFailedRegistrationStayVisibleAndCannotDeliver`
- `SettingReadFailureAndCancellationKeepTheirOwnFailureBehavior`
