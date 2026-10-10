# 原本取得・更新・保存・監視

対応関係・宣言名・実行方法のSHA-256：
`ad8a2f8e631bf6e644077ad63384fc01ec9135c41f318bd2312c5a48163c527d`

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

- `DeletedOriginalReportsUnavailableAndKeepsAcceptedDataAndSavedCopy`
- `ChangedButInvalidXlsxRetainsPreviousAcceptedAnalysisAndBothOriginals`
- `FirstSelectionAndOriginalSurviveRestartWhenPdfCannotBeParsed`
- `SamePathReplacementWithIdenticalBytesRetainsAnalysisAndRefreshesIdentity`
- `SamePathAtomicReplacementWithChangedBytesParsesAndSurvivesRestart`
- `ReplacementDuringCurrentReadIsRejectedAndKeepsPreviousSourceAndAnalysis`
- `WeekdayPreviewIsReadOnlyAndRejectsDifferentYearOrSource`
- `WrongExtensionAndExcelTemporaryFilesCannotReplaceSelection`

## [tests/Takupoke.Integration.Tests/SourceWatcherTests.cs](../../tests/Takupoke.Integration.Tests/SourceWatcherTests.cs)

- `UnavailableFolderDoesNotPreventOtherSelectedFilesFromReportingUpdates`

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
