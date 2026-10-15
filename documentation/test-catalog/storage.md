# 暗号化DB・原子保存・設定・年度期限

対応関係・宣言名・実行方法のSHA-256：
`355c63488c463a71e324987ca134fc35f643801848e276abe408268ef9d0de5c`

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

## [tests/Takupoke.Integration.Tests/DataDirectoryTests.cs](../../tests/Takupoke.Integration.Tests/DataDirectoryTests.cs)

- `MigrationPreservesKeysEncryptedDataOriginalsAndPreferences`
- `ExistingDirectoriesAreNeverMergedOrOverwritten`
- `FailedMoveKeepsExistingDataUsableAndDoesNotOverwriteDestination`
- `EmptyLegacyDirectoryDoesNotHideCurrentData`

## [tests/Takupoke.Integration.Tests/PreferenceSnapshotTests.cs](../../tests/Takupoke.Integration.Tests/PreferenceSnapshotTests.cs)

- `SharedSnapshotAllowsAtomicPreferenceReplacementAndKeepsCompleteOldValue`
- `SnapshotRetriesRealSharingViolationUntilExclusiveHandleIsReleased`

## [tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs](../../tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs)

- `AiPermissionDefaultsOffForNewAndLegacyPreferencesAndPersistsExplicitChoice`
- `DefaultRemovesSavedColorAndPreservesOtherPersonalSettings`

## [tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs](../../tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs)

- `SchoolPayloadIsEncryptedAndSurvivesRestart`
- `PeriodRolloverClearsSchoolDataPreservesPreferencesAndRejectsOldLease`
- `LockPreventsReadsAndOldOperationsCannotResumeAfterUnlock`
- `ParseFailureKeepsNewSelectionAndLastSuccessfulAnalysisWithItsOwnOriginal`
- `CorruptDatabaseDoesNotGetReinitializedWithinCurrentPeriod`
- `CipherRejectsTamperingAndDifferentRecordPurpose`
- `PendingFailureAndJobCommitTogetherAndNewSourceInvalidatesJob`
- `SameHashReselectionInvalidatesFailureCacheBeforeAReplacementParseStarts`
- `PriorYearCannotReachRecoveryPreview`
- `PriorYearPersistedPreviewOrAcceptanceCannotBeAdopted`
- `WrongTermCannotReachPreviewOrReplaceTheCurrentFormalAnalysis`
- `SpecialDatesFromAnotherHalfCannotReachRecoveryPreview`
- `SpecialDatesFromAnotherHalfCannotBeAdoptedFromOldPreviewOrAcceptance`
- `RecoveryAdoptionCommitsFormalAuditAcceptanceAndClearsPendingTogether`
- `OldStrictFailureCannotAuthorizePreviewAdoptionOrAcceptanceReuse`
- `StrictSuccessInvalidatesOldPreviewAndCannotBeOverwrittenByRecovery`
- `CancelledAdoptionPreservesPreviewAndSelectionChangeRejectsIt`
- `OldStrictClassAssignmentMigratesOnceAndPersistsOriginalCellKeys`
- `OldRecoveryProjectionIsVerifiedBeforeAffiliationMigrationAndTamperingIsRejected`
- `ForegroundRecoveryPreservesPersistedJobStartWhileCreatingPreview`
