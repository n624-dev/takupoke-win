# 原本取得・更新・保存・監視

対応関係・宣言名・実行方法のSHA-256：
`62c61268f80b5a7f270817649df0ae3e156601dc09829c67846109dc87b63a68`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Materials/FileSourceReader.cs](../../src/Takupoke.Infrastructure/Materials/FileSourceReader.cs)
- [src/Takupoke.Infrastructure/Materials/MaterialCoordinator.ChangeRows.cs](../../src/Takupoke.Infrastructure/Materials/MaterialCoordinator.ChangeRows.cs)
- [src/Takupoke.Infrastructure/Materials/MaterialCoordinator.cs](../../src/Takupoke.Infrastructure/Materials/MaterialCoordinator.cs)
- [src/Takupoke.Infrastructure/Materials/SourceWatcher.cs](../../src/Takupoke.Infrastructure/Materials/SourceWatcher.cs)
- [tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj](../../tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj)
- [src/Takupoke.Win/Platform/WindowsFileIdentity.cs](../../src/Takupoke.Win/Platform/WindowsFileIdentity.cs)

## [tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs](../../tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs)

- `OpeningAFileProviderCannotBlockTheCallingUiThread`

## [tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs](../../tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs)

- `ReturningToAcceptedYearClearsAnObsoleteParseAttemptWhenReusingTheCache`
- `DeletedOriginalReportsUnavailableAndKeepsAcceptedDataAndSavedCopy`
- `ChangedButInvalidXlsxRetainsPreviousAcceptedAnalysisAndBothOriginals`
- `CachedPdfFailureKeepsTheInitialExplanationAcrossRefreshAndRestart`
- `FirstSelectionAndOriginalSurviveRestartWhenPdfCannotBeParsed`
- `SamePathReplacementWithIdenticalBytesRetainsAnalysisAndRefreshesIdentity`
- `SamePathAtomicReplacementWithChangedBytesParsesAndSurvivesRestart`
- `ReplacementDuringCurrentReadIsRejectedAndKeepsPreviousSourceAndAnalysis`
- `WeekdayPreviewIsReadOnlyAndRejectsDifferentYearOrSource`
- `WrongExtensionAndExcelTemporaryFilesCannotReplaceSelection`
- `InvalidYearCannotReplaceAcceptedDatesAndFreshnessChecksConfiguredYear`

## [tests/Takupoke.Integration.Tests/SourceWatcherTests.cs](../../tests/Takupoke.Integration.Tests/SourceWatcherTests.cs)

- `ReplacingTheSameSourcePreservesAnAlreadyPendingRefresh`
- `UnavailableFolderDoesNotPreventOtherSelectedFilesFromReportingUpdates`
- `SameSourceIsRegisteredAgainAfterMissingFolderRecovers`

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`
- `ExcludingEveryChangeRefusesAndPreservesLastGood`
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`

## [tests/Takupoke.Integration.Tests/WindowsSourceRefreshTests.cs](../../tests/Takupoke.Integration.Tests/WindowsSourceRefreshTests.cs)

- `AtomicReplacementUsesNewNativeFileIdAndRefreshesWithoutReselection`

## [tests/Takupoke.Integration.Tests/AppLifecycleTests.cs](../../tests/Takupoke.Integration.Tests/AppLifecycleTests.cs)

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
