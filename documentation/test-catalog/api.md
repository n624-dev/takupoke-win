# API・年度別行事・更新失敗

対応関係・宣言名・実行方法のSHA-256：
`ee1cbd9d36d86cbcfce9d9f252dd44e4c4b658005c0bed5536d4cfc48782fc66`

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

- `RequestDeadlineIncludesTheBodyAndKeepsUserCancellationDistinct`
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
- `VerifiedPackagePreservesScopedInternationalRuleBesideGenericAlias`

## [tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs](../../tests/Takupoke.Integration.Tests/EventSourceCheckerTests.cs)

- `MetadataChecksOnlyApiAndDistinguishesResultsWithoutUsingPdfEtag`
- `VersionHashAndYearMustMatchBeforeAnObservationCanApply`
- `TwelveHourPersistentCacheCoalescesWaitersForceBypassesAndFailureKeepsPreviousSuccess`
- `CheckingUsesSixtySecondsAndUnavailableOldEdgesPreserveNewestSuccessfulResult`
- `InvalidMetadataAndCorruptAdvisoryCacheCannotDestroyAnnualEvents`
- `MissingCheckingOrStaleCannotBecomeAnApparentlyFreshMatch`
- `CallerCancellationDoesNotReplaceAHealthyObservation`

## [tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs](../../tests/Takupoke.Integration.Tests/PublicEventsStoreTests.cs)

- `AcceptedUtf8PayloadSurvivesAnEscapedSavedEnvelope`
- `DamagedYearDoesNotBlockHealthyYearsAndCanBeReplaced`
- `CancellationIsNotReportedAsDamagedData`

## [tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs](../../tests/Takupoke.Integration.Tests/SharedDataUpdaterTests.cs)

- `LinksAuthenticationCipherFailuresRemainErrors`
- `InvalidLinksCacheDoesNotBlockChecksOrAuthenticatedReplacement`
- `CorruptTimeCacheCanBeReplacedAfterAuthentication`
- `FailedTimeRequestPreservesCorruptCacheAndDatabaseFailuresStillStop`
- `SingleAuthenticationAndFailureOfOneDatasetDoesNotRollBackOthers`
- `AuthenticationCancellationLeavesPreviouslySavedDataUntouched`
- `CurrentRevisionsRequireNeitherAuthenticationNorDownloads`
- `PublicFailureIsReportedIndependentlyAndRetryClearsOnlyItsFailure`
- `PublicCheckCancellationPropagatesWithoutReplacingKnownState`
- `CorruptMappingCanBeAuthenticatedAndReplacedWithoutPrematureDeletion`
- `NullTeacherContextClassInCacheDoesNotBlockAuthenticatedRepair`
