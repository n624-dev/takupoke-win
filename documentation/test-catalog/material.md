# 原本取得・更新・保存・監視

対応ソース・テストのSHA-256：
`585114da251eb33aace187128c4245095bf420aa472d14f54815fe3095c920dd`

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

## [tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs](../../tests/Takupoke.Integration.Tests/FileReadResponsivenessTests.cs)

- `OpeningAFileProviderCannotBlockTheCallingUiThread`（宣言行 20）

## [tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs](../../tests/Takupoke.Integration.Tests/MaterialCoordinatorTests.cs)

- `DeletedOriginalReportsUnavailableAndKeepsAcceptedDataAndSavedCopy`（宣言行 20）
- `ChangedButInvalidXlsxRetainsPreviousAcceptedAnalysisAndBothOriginals`（宣言行 45）
- `FirstSelectionAndOriginalSurviveRestartWhenPdfCannotBeParsed`（宣言行 69）
- `ReplacedFileAtSamePathIsNotSilentlyAdopted`（宣言行 93）
- `WeekdayPreviewIsReadOnlyAndRejectsDifferentYearOrSource`（宣言行 112）
- `WrongExtensionAndExcelTemporaryFilesCannotReplaceSelection`（宣言行 138）

## [tests/Takupoke.Integration.Tests/SourceWatcherTests.cs](../../tests/Takupoke.Integration.Tests/SourceWatcherTests.cs)

- `UnavailableFolderDoesNotPreventOtherSelectedFilesFromReportingUpdates`（宣言行 8）

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`（宣言行 36）
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`（宣言行 60）
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`（宣言行 82）
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`（宣言行 94）
- `ExcludingEveryChangeRefusesAndPreservesLastGood`（宣言行 112）
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`（宣言行 129）
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`（宣言行 155）
