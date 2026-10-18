# API・年度別行事・更新失敗

対応関係・宣言名・実行方法のSHA-256：
`5e25dd9bb3d85512beab2bbb46b7a9b8df46a6ed7720eb9841030220a589af09`

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

- `HttpTimeoutIsReportedAsCommunicationFailure`
- `CallerCanCancelAnActiveRequestWithoutReportingCommunicationFailure`
- `PublicRevisionNeverSendsAuthenticationAndUsesInstalledRevision`
- `RejectsRevisionBodyOrMalformedIdentifier`
- `ChangedDuringDownloadDoesNotProduceNewPayload`
- `ValidLinksAreDecodedOnlyAfterHeadersAndContentTypeMatch`
- `MatchingWeakEventsEtagReusesSavedPayload`
- `EventsFirstDownloadThenRepeatedWeakEtagChecksReachTheServer`
- `WeakEventsChecksRejectMismatchedValidatorsAndNonempty304Bodies`
- `StreamingLimitAppliesWithoutContentLength`
- `MappingRejectsUnverifiedOrAmbiguousPackages`
- `MappingAcceptsVerifiedV2ContextWithoutExposingSourceCsv`
- `ApiTimesRejectUnknownFieldsRatherThanIgnoringThem`

## [tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs](../../tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs)

- `ChecksOnlyHeadAndReportsDifferenceWithoutReplacingSavedEvents`
- `UnavailableYearOrMissingSavedSourceTagDoesNotContactSchoolSite`

## [tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs](../../tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs)

- `DamagedYearDoesNotBlockHealthyYearsAndCanBeReplaced`
- `CancellationIsNotReportedAsDamagedData`

## [tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs](../../tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs)

- `SelectedResourceCombinationsNeverAuthenticateTwiceOrFetchUnselectedData`
- `SingleAuthenticationAndFailureOfOneDatasetDoesNotRollBackOthers`
- `AuthenticationCancellationLeavesPreviouslySavedDataUntouched`
- `CurrentRevisionsRequireNeitherAuthenticationNorDownloads`
- `PublicFailureIsReportedIndependentlyAndRetryClearsOnlyItsFailure`
- `PublicCheckCancellationPropagatesWithoutReplacingKnownState`
