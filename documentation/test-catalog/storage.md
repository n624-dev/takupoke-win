# 暗号化DB・原子保存・設定・年度期限

対応ソース・テストのSHA-256：
`b858d6f8c31640ce640cfa2cddde3d658f3404ecbd605786fc3acc9db54838b9`

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

- `RetentionUsesJapanTimeAndSchoolYear`（宣言行 9）
- `MidnightAtOctoberBoundaryInvalidatesPreviousPeriod`（宣言行 32）
- `JanuaryDoesNotInvalidateOctoberPeriod`（宣言行 42）

## [tests/Takupoke.Integration.Tests/DataDirectoryTests.cs](../../tests/Takupoke.Integration.Tests/DataDirectoryTests.cs)

- `MigrationPreservesKeysEncryptedDataOriginalsAndPreferences`（宣言行 24）
- `ExistingDirectoriesAreNeverMergedOrOverwritten`（宣言行 57）
- `FailedMoveKeepsExistingDataUsableAndDoesNotOverwriteDestination`（宣言行 69）
- `EmptyLegacyDirectoryDoesNotHideCurrentData`（宣言行 81）

## [tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs](../../tests/Takupoke.Integration.Tests/PreferencesStoreTests.cs)

- `DefaultRemovesSavedColorAndPreservesOtherPersonalSettings`（宣言行 10）

## [tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs](../../tests/Takupoke.Integration.Tests/SchoolDataStoreTests.cs)

- `SchoolPayloadIsEncryptedAndSurvivesRestart`（宣言行 30）
- `PeriodRolloverClearsSchoolDataPreservesPreferencesAndRejectsOldLease`（宣言行 43）
- `LockPreventsReadsAndOldOperationsCannotResumeAfterUnlock`（宣言行 58）
- `ParseFailureKeepsNewSelectionAndLastSuccessfulAnalysisWithItsOwnOriginal`（宣言行 70）
- `CorruptDatabaseDoesNotGetReinitializedWithinCurrentPeriod`（宣言行 90）
- `CipherRejectsTamperingAndDifferentRecordPurpose`（宣言行 99）

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`（宣言行 36）
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`（宣言行 60）
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`（宣言行 82）
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`（宣言行 94）
- `ExcludingEveryChangeRefusesAndPreservesLastGood`（宣言行 112）
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`（宣言行 129）
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`（宣言行 155）
