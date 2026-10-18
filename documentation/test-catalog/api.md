# API・年度別行事・更新失敗

対応関係・宣言名・実行方法のSHA-256：
`1377a29fe76fd18261c4ab566935d84d09047f49fcfa2d79d5b6f2aebd057e90`

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
- `LocalHttpCapabilityUsesTheSameRepresentationForRevisionAndDownloadOnly`
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
