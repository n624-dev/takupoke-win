# API・年度別行事・更新失敗

対応ソース・テストのSHA-256：
`86c57ae72f5621a5051cb89bb9109792d47a17345b648a25c46a2b5010bbde4b`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Api/ApiClient.cs](../../src/Takupoke.Infrastructure/Api/ApiClient.cs)
- [src/Takupoke.Infrastructure/Api/ApiModels.cs](../../src/Takupoke.Infrastructure/Api/ApiModels.cs)
- [src/Takupoke.Infrastructure/Api/ApiPayloads.cs](../../src/Takupoke.Infrastructure/Api/ApiPayloads.cs)
- [src/Takupoke.Infrastructure/Api/EventSourceChecker.cs](../../src/Takupoke.Infrastructure/Api/EventSourceChecker.cs)
- [src/Takupoke.Infrastructure/Api/SharedDataUpdater.cs](../../src/Takupoke.Infrastructure/Api/SharedDataUpdater.cs)

## [tests/Takupoke.Integration.Tests/ApiClientTests.cs](../../tests/Takupoke.Integration.Tests/ApiClientTests.cs)

- `HttpTimeoutIsReportedAsCommunicationFailure`（宣言行 47）
- `CallerCanCancelAnActiveRequestWithoutReportingCommunicationFailure`（宣言行 57）
- `PublicRevisionNeverSendsAuthenticationAndUsesInstalledRevision`（宣言行 71）
- `RejectsRevisionBodyOrMalformedIdentifier`（宣言行 82）
- `ChangedDuringDownloadDoesNotProduceNewPayload`（宣言行 90）
- `ValidLinksAreDecodedOnlyAfterHeadersAndContentTypeMatch`（宣言行 100）
- `MatchingWeakEventsEtagReusesSavedPayload`（宣言行 109）
- `EventsFirstDownloadThenRepeatedWeakEtagChecksReachTheServer`（宣言行 118）
- `WeakEventsChecksRejectMismatchedValidatorsAndNonempty304Bodies`（宣言行 149）
- `StreamingLimitAppliesWithoutContentLength`（宣言行 164）
- `MappingRejectsUnverifiedOrAmbiguousPackages`（宣言行 170）
- `MappingAcceptsVerifiedV2ContextWithoutExposingSourceCsv`（宣言行 180）
- `ApiTimesRejectUnknownFieldsRatherThanIgnoringThem`（宣言行 187）

## [tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs](../../tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs)

- `ChecksOnlyHeadAndReportsDifferenceWithoutReplacingSavedEvents`（宣言行 22）
- `UnavailableYearOrMissingSavedSourceTagDoesNotContactSchoolSite`（宣言行 35）

## [tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs](../../tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs)

- `DamagedYearDoesNotBlockHealthyYearsAndCanBeReplaced`（宣言行 13）
- `CancellationIsNotReportedAsDamagedData`（宣言行 34）

## [tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs](../../tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs)

- `SingleAuthenticationAndFailureOfOneDatasetDoesNotRollBackOthers`（宣言行 63）
- `AuthenticationCancellationLeavesPreviouslySavedDataUntouched`（宣言行 89）
- `CurrentRevisionsRequireNeitherAuthenticationNorDownloads`（宣言行 108）
- `PublicFailureIsReportedIndependentlyAndRetryClearsOnlyItsFailure`（宣言行 128）
- `PublicCheckCancellationPropagatesWithoutReplacingKnownState`（宣言行 164）
