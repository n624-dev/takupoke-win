# 復旧の証拠・採用・旧記録の契約

対応関係・宣言名・実行方法のSHA-256：
`88343e8ee4d599a8b473d62dfb913919a86dec2e3340e7b41017ed3c463a05c7`

環境：Linux／Windows .NET

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/Recovery/RecoveryAuditShape.cs](../../src/Takupoke.Core/Recovery/RecoveryAuditShape.cs)
- [src/Takupoke.Core/Recovery/RecoveryEngine.cs](../../src/Takupoke.Core/Recovery/RecoveryEngine.cs)
- [src/Takupoke.Core/Recovery/RecoveryManualAssistance.cs](../../src/Takupoke.Core/Recovery/RecoveryManualAssistance.cs)
- [src/Takupoke.Core/Recovery/RecoveryModelManifest.cs](../../src/Takupoke.Core/Recovery/RecoveryModelManifest.cs)
- [src/Takupoke.Core/Recovery/RecoveryModels.cs](../../src/Takupoke.Core/Recovery/RecoveryModels.cs)
- [src/Takupoke.Core/Recovery/RecoveryPolicy.cs](../../src/Takupoke.Core/Recovery/RecoveryPolicy.cs)
- [src/Takupoke.Core/Recovery/RecoveryPromptInstructions.cs](../../src/Takupoke.Core/Recovery/RecoveryPromptInstructions.cs)
- [src/Takupoke.Core/Recovery/RecoveryRoleLabels.cs](../../src/Takupoke.Core/Recovery/RecoveryRoleLabels.cs)
- [src/Takupoke.Core/Recovery/RecoverySourceCoverage.cs](../../src/Takupoke.Core/Recovery/RecoverySourceCoverage.cs)
- [src/Takupoke.Core/Recovery/RecoverySourceIndex.cs](../../src/Takupoke.Core/Recovery/RecoverySourceIndex.cs)
- [src/Takupoke.Core/Recovery/RecoveryStructure.cs](../../src/Takupoke.Core/Recovery/RecoveryStructure.cs)
- [src/Takupoke.Core/Recovery/RecoveryValidator.cs](../../src/Takupoke.Core/Recovery/RecoveryValidator.cs)
- [tests/Takupoke.Core.Tests/fixtures/recovery-exam.json](../../tests/Takupoke.Core.Tests/fixtures/recovery-exam.json)
- [tests/Takupoke.Core.Tests/fixtures/recovery-return.json](../../tests/Takupoke.Core.Tests/fixtures/recovery-return.json)

## [tests/Takupoke.Core.Tests/AiFeaturePermissionTests.cs](../../tests/Takupoke.Core.Tests/AiFeaturePermissionTests.cs)

- `OffOnOffRevokesOldOperationsWithoutRevivingThem`

## [tests/Takupoke.Core.Tests/RecoveryIndexTests.cs](../../tests/Takupoke.Core.Tests/RecoveryIndexTests.cs)

- `SpatialIndexCannotDropAMinuteIntersectionThroughRoundedUnionBounds`
- `TallSourceFromAnotherCellStillIntersectsFarAwayCells`
- `ExcessiveGeometryWorkStopsBeforeAnyModelIsQueried`
- `TotalOriginalTextWorkIsBoundedBeforeConcatenationOrModelFallback`
- `InvalidSourceCoordinatesAreRejectedBeforeSpatialIndexing`

## [tests/Takupoke.Core.Tests/RecoveryInlineParallelTests.cs](../../tests/Takupoke.Core.Tests/RecoveryInlineParallelTests.cs)

- `StoredInlineTuplesCannotBeReusedOrRevalidatedAsOneCompoundLesson`
- `LegitimateOldInlineAuditNeedsCurrentRevalidationAndPreservesSingleCompoundField`
- `LegacyFixedCompoundTupleIsRejectedEvenWithAValidCurrentAcceptance`
- `SingleCompoundFixedFieldRemainsAnExactLiteral`
- `CurrentStructureAndFieldProviderHistoriesAreValidatedIndependentlyWithoutRewritingEither`
- `TwoIndependentlyBoundFixedTuplesRemainTwoLessons`

## [tests/Takupoke.Core.Tests/RecoveryPromptInstructionsTests.cs](../../tests/Takupoke.Core.Tests/RecoveryPromptInstructionsTests.cs)

- `FieldInstructionIsPackagedAsTheExactSharedUtf8Asset`
- `FieldInputAddsOnlyPublicStateTokensAndPreservesOriginalDataOrder`
- `StructureInstructionAndInputKeepTheirExistingContract`

## [tests/Takupoke.Core.Tests/RecoveryStructureTests.cs](../../tests/Takupoke.Core.Tests/RecoveryStructureTests.cs)

- `NonAdjacentLabelResolvesByRulesThenOriginalValuesAreValidated`
- `AdjacentLabelsResolveWithoutPreparingOrLoadingModel`
- `ThreeFragmentsAndDistantRailResolveWithoutModel`
- `PermutedAliasesAndIdenticalBodiesBindOnlyByOriginalRailProof`
- `InstructionLookingBodyIsPreservedAsOriginalTextWithoutModel`
- `OrphanRailInkAndDuplicateRoleCannotBecomeACompletePartition`
- `RuleRecoveryAcrossCellsDoesNotPrepareAnyProvider`
- `CancellationAndRequestLimitsRejectBeforeRuleOrProviderWork`
- `MoreThan64MeasuredUnitsCannotEnterRailSearch`
- `AliasExpansionAcrossCertifiedRailsIsTerminalBeforeAnyProvider`
- `InvalidProposalIsTerminalBeforeFallback`
- `RuntimeFallbackCannotOverrideAnUncertifiablePartition`
- `AvailabilityFailureCanUseNextLocalProvider`
- `CancellationCannotProduceStructureOrPreview`
- `MissingCoverageAndBrokenKnownScopeRejectBeforeModelAvailability`
- `InventedCutAndSwappedRoleCannotPassPhysicalCertificate`

## [tests/Takupoke.Core.Tests/RecoveryTests.cs](../../tests/Takupoke.Core.Tests/RecoveryTests.cs)

- `GroundedRulesDoNotLoadLanguageModel`
- `ModelNotReadyWaitsWithoutDownloadingFallback`
- `MalformedOutputIsTerminalBeforeNextProvider`
- `CompleteGroundedResultCanBePreviewed`
- `UnknownIsNeverFreePeriod`
- `IncompleteReaderIsRejected`
- `PartialCellIsRejected`
- `MissingSlotIsRejected`
- `MissingResultCellIsRejected`
- `DuplicateIdsRejectWithoutThrowing`
- `NeighbourEvidenceIsRejected`
- `InventedValueIsRejected`
- `ContentCannotBecomeEmpty`
- `ChangedYearIsRejected`
- `ChangedHashIsRejected`
- `ApprovalOnlyReusesExactValidatedResult`
- `WrongSchemaRejects`
- `XlsxAndTemporaryNotReadyNeverTriggerModelFallback`
- `WrongHeaderTextIsRejected`
- `FieldsCannotSwapRoles`
- `TruncatedNamesAreRejected`
- `KnownTextCannotBecomeBlank`
- `OverlappingCellsAreRejected`
- `WrongHeadingAxisIsRejected`
- `OverflowingBoundsReject`
- `CancelledRuleRecoveryDoesNotReturnPreview`
- `ManifestRequiresKnownBackendAndMinimumOS`
- `NullProviderFieldCannotCrashValidator`
- `SpecialSchedulesWithFullScopeAndExplicitSpanTimesPass`
- `SpecialRecoveryRequiresEveryDateInTheSelectedHalf`
- `InventoryCannotDiscardTextToClaimEmpty`
- `UnassignedTextInBlankCellRejects`
- `UnboundLiteralCannotBeClassifiedAsPeriodHeader`
- `UnusedSpanCannotClassifyOrphanAsClock`
- `OrphanCannotBeHiddenInYearOrClassEvidence`
- `OrphanCannotBeHiddenInReturnNote`
- `OrphanTextOutsideCellsRejects`
- `CommonClockCannotOverrideDateSpecificTime`
- `AnotherDayClockCannotBeQuoted`
- `ReversedSpanClockCannotPassEvenWhenTextExists`
- `ReturnNormalTimeRequiresActualApplicableNote`
- `SeventeenClassesCannotIncludeAnUnexpectedReplacement`
- `ProvedRoleScopesUseRulesWithoutModelAvailabilityOrInference`
- `ProposalCannotSwapSubjectAndTeacherEvenWhenEveryAtomIsUsed`
- `FixedCellCannotUseUnusedScopeToHideOrphanText`
- `DuplicateProposalScopesRejectWithoutException`
- `PinnedFoundryVariantHasSafeDistinctFileKey`
- `RepeatedReturnNotesAreVerifiedIndependentlyPerPage`
- `ReturnRestdaySpanUsesTheApplicablePrintedNormalTimeNote`
- `FixedBindingCannotTreatOriginalRolePrefixAsAValue`
