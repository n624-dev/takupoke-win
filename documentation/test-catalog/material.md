# 原本取得・更新・保存・監視

対応関係・宣言名・実行方法のSHA-256：
`fb4c4708283c53be7d968781a523145a826c77d6ae939a41a8e9e7e204912466`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Materials/FileSourceReader.cs](../../src/Takupoke.Infrastructure/Materials/FileSourceReader.cs)
- [src/Takupoke.Infrastructure/Materials/MaterialCoordinator.ChangeRows.cs](../../src/Takupoke.Infrastructure/Materials/MaterialCoordinator.ChangeRows.cs)
- [src/Takupoke.Infrastructure/Materials/MaterialCoordinator.cs](../../src/Takupoke.Infrastructure/Materials/MaterialCoordinator.cs)
- [src/Takupoke.Infrastructure/Materials/MaterialProjection.cs](../../src/Takupoke.Infrastructure/Materials/MaterialProjection.cs)
- [src/Takupoke.Infrastructure/Materials/SourceWatcher.cs](../../src/Takupoke.Infrastructure/Materials/SourceWatcher.cs)
- [src/Takupoke.Win/Platform/WindowsFileIdentity.cs](../../src/Takupoke.Win/Platform/WindowsFileIdentity.cs)
- [tests/Takupoke.Integration.Tests/AppViewModelTestPlatform.cs](../../tests/Takupoke.Integration.Tests/AppViewModelTestPlatform.cs)
- [tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj](../../tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj)
- [tests/Takupoke.Integration.Tests/ViewModelFileIdentity.cs](../../tests/Takupoke.Integration.Tests/ViewModelFileIdentity.cs)

## [tests/Takupoke.Integration.Tests/AppViewModelRefreshTests.cs](../../tests/Takupoke.Integration.Tests/AppViewModelRefreshTests.cs)

- `RecoveryOffBlocksOcrModelDownloadAndPreservesLastGood`
- `SavedRecoveryDisplayRequiresConfirmedSemanticAuditAndExactFormalProjection`
- `SelectedHighConfidenceManualPlanSurvivesSnapshotReloadAndRejectsDamagedReviewToken`
- `KnownLatestAcquisitionFailureBlocksPendingPreviewPdfWithoutChangingLastGood`
- `UnlockBeforeCancelledWorkerJoinsRestoresFormalDataAndSourceMonitoring`
- `CancelOrSuspendAfterDrainWasPostedCannotReviveARequestOnResume`
- `CancelOrSuspendClearsRequestQueuedBehindBusyWorker`
- `IneligibleAutomaticRequestIsNotQueuedWhileBusy`

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `IndependentRowAndWeekdayApprovalsAreAtomicAndSurviveRefreshReparseAndRestart`
- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`
- `ContentAndOriginalChangesInvalidateWhileFailedYearReparsePreservesPreviousDecision`
- `OldParserConsentIsRevalidatedAndCommittedWithTheCurrentAnalysis`
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`
- `ExcludingEveryChangeRefusesAndPreservesLastGood`
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`
- `FailedCurrentRevalidationPreservesOlderConfirmationAndLastGoodResult`
- `ReselectingAnotherFileWithIdenticalBytesCannotInheritTheDecision`

## [tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs](../../tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs)

- `OpeningAFileProviderCannotBlockTheCallingUiThread`

## [tests/Takupoke.Integration.Tests/LatestSourceTests.cs](../../tests/Takupoke.Integration.Tests/LatestSourceTests.cs)

- `AtomicReplacementReadsLatestBytesAndPreservesLastGoodOnStrictFailure`
- `SameBytesReplacementRefreshesIdentityAndExplicitReparseStillRunsStrict`
- `ChangedPdfInvalidatesPreviewAndCannotAdoptTheOldHash`
- `CancellationOrChangeDuringReadKeepsSelectedSnapshotAndLastGood`
- `InvalidLatestFileDoesNotReplaceTheSelectedPdf`
- `RecoveryDoesNotUseCachedOriginalWhenLatestAcquisitionFails`
- `RecoveryOffStillRetriesStrictButNeverAcquiresOcrOrInitializesProviders`
- `RecoveryOnUsesRuleResultsAndRevokesTheirReviewWhenSwitchedOff`
- `OffOnCannotAuthorizeAnExistingRuleReviewFirstLoadedWhileOff`
- `SwitchingAiOffCancelsEvenAProviderFactoryThatIgnoresCancellation`
- `ExplicitReaderIdentityGuardStillRejectsADifferentFileVersion`
- `CancelledAdoptionDuringLatestReadKeepsPreviewAndLastGood`
- `FailedLatestAcquisitionBlocksOldPendingAdoptionButKeepsLastGood`
- `IdenticalByteReplacementKeepsPendingPreviewBoundToSameHashAndCanBeAdopted`
- `ForegroundRecoveryBuildReceivesLatestPdfAndFreshCaptureAfterStrict`

## [tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs](../../tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs)

- `WeekdayConsentSurvivesRestartButExpiresOnContentUpdateAndReselection`
- `ChangedOriginalDuringWeekdayConfirmationCannotInstallConsent`
- `OlderParserSuccessForSameHashIsReparsedAndFailureKeepsPreviousFormalResult`
- `SameBytesRefreshUpdatesSourceModificationWithoutReparsingOrReacquiring`
- `DeletedOriginalReportsUnavailableAndKeepsAcceptedDataAndSavedCopy`
- `ChangedButInvalidXlsxRetainsPreviousAcceptedAnalysisAndBothOriginals`
- `FirstSelectionAndOriginalSurviveRestartWhenPdfCannotBeParsed`
- `SamePathReplacementWithIdenticalBytesRetainsAnalysisAndRefreshesIdentity`
- `SamePathAtomicReplacementWithChangedBytesParsesAndSurvivesRestart`
- `ReplacementDuringCurrentReadIsRejectedAndKeepsPreviousSourceAndAnalysis`
- `WeekdayPreviewIsReadOnlyAndRejectsDifferentYearOrSource`
- `WrongExtensionAndExcelTemporaryFilesCannotReplaceSelection`

## [tests/Takupoke.Integration.Tests/RecoveryManualLifecycleTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualLifecycleTests.cs)

- `ConfidentFieldsCanBeSelectedAsOneBoundedBatchWithoutWritingEarly`
- `FourNewContractEditsSurvivePendingRestartAndExplicitAdoption`
- `ConfidentPreviewCanBeExplicitlyCorrectedAndSavedOnlyAfterAdoption`
- `CancelReturnsToOriginalPreviewAndAReopenedReviewInvalidatesPreviousSession`
- `AChangedOrUnreadableSourceCannotOpenAConfidentFieldCorrection`
- `PendingDraftAndWholePreviewDoNotSaveUntilExplicitAdoptionAndSurviveRestart`
- `CancelRefusalAndChangedOrUnreadableSourcePreserveLastGoodHistoryAndBlob`
- `IneligibleLowConfidenceAcquisitionFailsInsteadOfWaitingForAnotherModel`
- `UnreadableOriginalAfterHumanPreviewInvalidatesOnlyPendingCorrection`
- `CertifiedV7ManualHistorySurvivesStoreDisplayRestartAndRejectsFreshOrChangedConsent`

## [tests/Takupoke.Integration.Tests/SourceWatcherTests.cs](../../tests/Takupoke.Integration.Tests/SourceWatcherTests.cs)

- `ReregisteringSourcesKeepsAnAlreadyReceivedChange`
- `UnavailableFolderDoesNotPreventOtherSelectedFilesFromReportingUpdates`

## [tests/Takupoke.Integration.Tests/WindowsSourceRefreshTests.cs](../../tests/Takupoke.Integration.Tests/WindowsSourceRefreshTests.cs)

- `AtomicReplacementUsesNewNativeFileIdAndRefreshesWithoutReselection`
