# 暗号化DB・原子保存・設定・年度期限

対応関係・宣言名・実行方法のSHA-256：
`667c8e5f0099a01210382ee00b54d1a84194f79a87851d3aad08ab9937b33b9a`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/SchoolDataPeriod.cs](../../src/Takupoke.Core/SchoolDataPeriod.cs)
- [src/Takupoke.Infrastructure/Storage/DataCodec.cs](../../src/Takupoke.Infrastructure/Storage/DataCodec.cs)
- [src/Takupoke.Infrastructure/Storage/DataDirectory.cs](../../src/Takupoke.Infrastructure/Storage/DataDirectory.cs)
- [src/Takupoke.Infrastructure/Storage/EnvelopeCipher.cs](../../src/Takupoke.Infrastructure/Storage/EnvelopeCipher.cs)
- [src/Takupoke.Infrastructure/Storage/PreferencesStore.cs](../../src/Takupoke.Infrastructure/Storage/PreferencesStore.cs)
- [src/Takupoke.Infrastructure/Storage/PublicEventsStore.cs](../../src/Takupoke.Infrastructure/Storage/PublicEventsStore.cs)
- [src/Takupoke.Infrastructure/Storage/SchoolDataStore.cs](../../src/Takupoke.Infrastructure/Storage/SchoolDataStore.cs)
- [src/Takupoke.Infrastructure/Storage/StorageModels.cs](../../src/Takupoke.Infrastructure/Storage/StorageModels.cs)
- [src/Takupoke.Infrastructure/Storage/WindowsDpapiProtector.cs](../../src/Takupoke.Infrastructure/Storage/WindowsDpapiProtector.cs)

## [tests/Takupoke.Core.Tests/SchoolDataPeriodTests.cs](../../tests/Takupoke.Core.Tests/SchoolDataPeriodTests.cs)

- `RetentionUsesJapanTimeAndSchoolYear`
- `MidnightAtOctoberBoundaryInvalidatesPreviousPeriod`
- `JanuaryDoesNotInvalidateOctoberPeriod`

## [tests/Takupoke.Integration.Tests/DataDirectoryTests.cs](../../tests/Takupoke.Integration.Tests/DataDirectoryTests.cs)

- `MigrationPreservesKeysEncryptedDataOriginalsAndPreferences`
- `ExistingDirectoriesAreNeverMergedOrOverwritten`
- `FailedMoveKeepsExistingDataUsableAndDoesNotOverwriteDestination`
- `EmptyLegacyDirectoryDoesNotHideCurrentData`

## [tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs](../../tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs)

- `DefaultRemovesSavedColorAndPreservesOtherPersonalSettings`

## [tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs](../../tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs)

- `SchoolPayloadIsEncryptedAndSurvivesRestart`
- `PeriodRolloverClearsSchoolDataPreservesPreferencesAndRejectsOldLease`
- `LockPreventsReadsAndOldOperationsCannotResumeAfterUnlock`
- `ParseFailureKeepsNewSelectionAndLastSuccessfulAnalysisWithItsOwnOriginal`
- `CorruptDatabaseDoesNotGetReinitializedWithinCurrentPeriod`
- `MissingDatabaseDoesNotReinitializeOrDeleteRemainingKey`
- `CipherRejectsTamperingAndDifferentRecordPurpose`

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`
- `ExcludingEveryChangeRefusesAndPreservesLastGood`
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`

## [tests/Takupoke.Integration.Tests/AppLifecycleTests.cs](../../tests/Takupoke.Integration.Tests/AppLifecycleTests.cs)

- `LinkNavigationCoalescesAndCachedChecksAvoidBusyAndRedrawWithoutHidingErrorsOrRetention`
- `InstalledLinkRevisionChangeInvalidatesNavigationCache`
- `EventMetadataDoesNotOwnBusyAndConcurrentCallersShareOneActualRequest`
- `DelayedMetadataForOldEventBytesCannotOverrideNewSnapshot`
- `UnrelatedEventFetchKeepsWarningsGeneratedByRealApiAndSourceChecks`
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

## [tests/Takupoke.Integration.Tests/RetentionRecoveryTests.cs](../../tests/Takupoke.Integration.Tests/RetentionRecoveryTests.cs)

- `CorruptMarkersBlockExistingLeasesAndExplicitRecoveryPreservesPublicAndPersonalFiles`
- `FailedResetKeepsQuarantinedMarkerAndCanResumeAfterRestart`
- `NormalMarkersLockedStateAndUnreadableMarkersCannotUseTheResetPath`
