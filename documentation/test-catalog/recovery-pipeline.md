# 復旧解析・原画像・訂正・保存・取消

対応関係・宣言名・実行方法のSHA-256：
`a7d48ad971291155e6e184da8de5d18075fb19291e727f11e37b08a9c179cf46`

環境：Linux／Windows .NET

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Recovery/RecoveryAnalysisConverter.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryAnalysisConverter.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryAuditCertification.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryAuditCertification.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryCapturedLayoutBuilder.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryCapturedLayoutBuilder.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryContainmentIndex.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryContainmentIndex.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryCoordinator.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryCoordinator.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryDocumentBuilder.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryDocumentBuilder.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryLayoutGeometry.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryLayoutGeometry.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryModelBundleStore.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryModelBundleStore.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryModelStore.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryModelStore.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryOcrModels.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryOcrModels.cs)
- [src/Takupoke.Infrastructure/Recovery/RecoveryOutputDecoder.cs](../../src/Takupoke.Infrastructure/Recovery/RecoveryOutputDecoder.cs)
- [tests/Takupoke.Integration.Tests/RecoveryBuilderBudgetFixture.cs](../../tests/Takupoke.Integration.Tests/RecoveryBuilderBudgetFixture.cs)
- [tests/Takupoke.Integration.Tests/fixtures/recovery-fictional-v3-header-rows.json](../../tests/Takupoke.Integration.Tests/fixtures/recovery-fictional-v3-header-rows.json)
- [tests/Takupoke.Integration.Tests/fixtures/recovery-historical-v5-vector.json](../../tests/Takupoke.Integration.Tests/fixtures/recovery-historical-v5-vector.json)
- [tests/Takupoke.Integration.Tests/fixtures/recovery-historical-v6-result.json](../../tests/Takupoke.Integration.Tests/fixtures/recovery-historical-v6-result.json)

## [tests/Takupoke.Integration.Tests/RecoveryAuditCacheCallOrderTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryAuditCacheCallOrderTests.cs)

- `HistoricalCacheNeverFingerprintsBeforeRejectingOverLimitCount`
- `OrdinaryCurrent9AuditKeepsExistingReuseWithoutCertification`

## [tests/Takupoke.Integration.Tests/RecoveryAuditPreHashBoundTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryAuditPreHashBoundTests.cs)

- `CompleteAuditBudgetPrecedesEveryCertificationHash`
- `OversizedManualPredecessorFailsClosedBeforeItsHashIsComputed`
- `SafeOriginal8StillCertifiesWithoutRewritingOriginalBytes`
- `HistoricalManualWrapperKeepsMalformedInputBoundary`
- `HistoricalManualWrapperPreservesCancellationEvenForIneligibleOrigins`
- `CertificationAndHistoricalEnvelopeRespectCancellation`

## [tests/Takupoke.Integration.Tests/RecoveryBuilderBudgetTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryBuilderBudgetTests.cs)

- `FiveOriginalDateTablesPreserveAll680SlotsAndFormalValuesWithinBudget`
- `DuplicateSlotsAcrossPagesStillReject`
- `CancellationDuringBlankInspectionStillStopsConstruction`

## [tests/Takupoke.Integration.Tests/RecoveryCapturedLayoutTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryCapturedLayoutTests.cs)

- `CompleteFilledVectorCellsKeepOriginalTextAndCoordinatesWithoutRaster`
- `BlankCellsStillRequireRasterEvidenceAndIncompleteReaderOutputCannotBeComplete`
- `CompleteButInvalidGeometryIsRejectedInsteadOfBeingHiddenByRasterFallback`

## [tests/Takupoke.Integration.Tests/RecoveryClockOrderTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryClockOrderTests.cs)

- `RepeatedClockChartsKeepHeadersInOriginalOrderAcrossPages`
- `ExplicitDayClockHeadersKeepDayAndPeriodInventoryOrderAcrossPages`

## [tests/Takupoke.Integration.Tests/RecoveryContainmentIndexTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryContainmentIndexTests.cs)

- `IndexedSelectionEqualsCompleteScanIncludingOrderDuplicatesAndBoundaries`
- `LocalQueryChargesRelevantCandidatesAndPreservesCancellationAndBudgetErrors`

## [tests/Takupoke.Integration.Tests/RecoveryCorrectionPreviewTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryCorrectionPreviewTests.cs)

- `CorrectionsMapOriginalUserProvenanceAndEveryOccupiedSlotWithoutChangingReceipt`
- `MergedParallelCorrectionsNeverHighlightNeighborLesson`
- `DisplayCorrectionsOwnTheirValuesAfterOriginalParentListMutates`
- `OnlyActualValidatedIdenticalOriginalFormalProjectionAllowsDiff`
- `SameVerifiedFormalResultHasNoInventedDiff`
- `OrdinaryProjectionUsesOriginalClassKeysAfterDepartmentMigration`
- `UnmatchedOrUnverifiedPreviousDataNeverGuessesAlignment`
- `NewSourceEvenWithIdenticalValuesHasNoGuessedPreviousMapping`
- `ResponsiveChoiceHasFiniteBoundaryWithoutInputStateChanges`
- `CancellationAndInvalidCurrentHashCannotProduceDisplay`
- `ConfidentCodeAdviceIsDisplayOnlyAndDisappearsAfterActualSourceOwnedReview`
- `CodeAdviceSpellsCharacterTypesWithoutNormalizingOrInventingWarnings`
- `RoomSelectionDeduplicatesMergedSlotsKeepsOtherClassesAndFailsAtomicallyAtBudget`

## [tests/Takupoke.Integration.Tests/RecoveryDateHeaderOwnershipTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryDateHeaderOwnershipTests.cs)

- `HyphenatedClassRailDoesNotBecomeJanuaryDateInCompleteExam`
- `RealHyphenatedJanuaryDatesRemainDatesWithIndependentInlineRoles`
- `ForeignHyphenatedHeaderDoesNotSupplyTrustedUnlabeledBodyRoles`
- `UnlabeledJanuaryBodyDoesNotGainRolesFromUnsupportedStrictDateSyntax`
- `MergedGradeEvidenceOwnsOnlyEachPhysicalClassRowInCompleteReturn`
- `ChangingPrintedDepartmentKeepsOwnershipOnItsOriginalPhysicalRow`
- `MatchingPrintedAiGradeAndCanonicalClassKeepAllOriginalEvidence`
- `AiHeaderProofIsIndependentOfPageOrderAndCount`
- `CanonicalOnlyAiHeaderDoesNotInventAnUnprintedGrade`
- `InvalidPrintedGradeNeverGetsIgnoredOrUsedAsAnAiClass`
- `ValidatorIndependentlyRejectsForgedAiCompositeProof`

## [tests/Takupoke.Integration.Tests/RecoveryFontEvidenceTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryFontEvidenceTests.cs)

- `OptionalDirectFontProofCannotConflictWithItsSource`

## [tests/Takupoke.Integration.Tests/RecoveryHeaderReviewTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryHeaderReviewTests.cs)

- `HeaderCanOnlyBeConfirmedUnchangedWithOwnedOriginalPixelsAndAudit`
- `HeaderInputIsReadOnlyAndStillRequiresExplicitAcknowledgement`
- `EmptyReadOnlySelectionPreservesLegacyBodyInputBinding`
- `UnknownOrMissingStructuralHeaderCannotBeTypedIntoExistence`

## [tests/Takupoke.Integration.Tests/RecoveryInlineParallelCacheTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryInlineParallelCacheTests.cs)

- `AlteredPreviousAcceptanceCannotPassCurrentReuseDisplayOrStoreDespiteValidCurrentHashes`
- `CachedInlineCompoundAuditIsNotReusedAndReachesTheActualBuilder`
- `OldPersistedInlineCompoundPreviewCannotBeAdoptedEvenWithCurrentVersion`
- `PreviouslyAcceptedGoodAuditIsRecertifiedAndAutomaticallyReusedWithoutAnotherConfirmation`
- `ExactV4ToV5ChainPreservesFirstConsentThroughV6AndRepeatedReuse`
- `UnchangedNonparallelOcrAuditNeedsOldHashesAndCurrentSemanticRevalidation`
- `ValidV5DistinctFieldAndStructureHistoriesRemainIndependentThroughV6`
- `ExactV6RecertifiedChainKeepsEarliestConsentThroughCurrentVersion`
- `ExactV7ChainRetainsEarliestConsentAndRejectsChangedAncestry`
- `V7ManualAuditPreservesOriginalCaptureSnapshotCorrectionsAndConsent`
- `HistoricallyImpossibleNewSeparatorProofCannotUpgradeARehashedOldAudit`
- `RehashedHistoricalV6CannotInventNewAcquisitionOrHumanProof`
- `HistoricallyImpossibleDistinctVersion4MetadataCannotInventPriorConfirmation`
- `ValidUpgradeCannotReuseAnAcceptanceChangedBeforeTheStorageTransaction`
- `TamperedOldAcceptanceCannotBeRecertifiedOrBypassRebuilding`
- `CurrentValidChangedContentCannotMasqueradeAsTheExactStoredAcceptanceUpgrade`
- `SameHashVersion24SuccessCannotSkipAnActualVersion25RefreshParse`

## [tests/Takupoke.Integration.Tests/RecoveryInlineParallelTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryInlineParallelTests.cs)

- `SingleInlineLabelsCannotCertifyMultipleLessonsOrEscapeToProposal`
- `SingleCompoundFieldStillRetainsExactOriginalEvidence`
- `IndependentParallelColumnsRetainSeparateLessonsAndProvedEmptyRoom`

## [tests/Takupoke.Integration.Tests/RecoveryManualAssistanceTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualAssistanceTests.cs)

- `IndependentOriginalV6ResultJsonAndRootHashesStayExactWithoutManualFields`
- `OneAndThreeSourceOwnedFieldsRequireHumanProvenanceAndPreserveOriginalOcr`
- `FourFieldsAcrossTwoPagesFailBeforeQuestionsAndCannotBeGrouped`
- `ParallelPositionsAreSeparateTargetsAndMergedSlotsRetainBothTuples`
- `IncompleteUnknownOrStructurallyUnresolvedAcquisitionCannotExposeFewQuestions`
- `OriginalPixelCropsMustMatchCapturedGeometryAndImmutableRasterIdentity`
- `MissingPixelPayloadInMalformedPersistedCaptureRefusesInsteadOfCrashing`
- `CorrectionsCannotChangeCropRoleOriginalEvidenceOrConsentIdentity`
- `EveryUnresolvedFieldMustBeCorrectedAndCancellationNeverProducesAResult`

## [tests/Takupoke.Integration.Tests/RecoveryManualCertificationTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualCertificationTests.cs)

- `ExactV7ManualHistoriesRevalidateWithoutChangingOriginalHumanProof`
- `AlteredCurrentCertificateOrOriginalProofCannotBeRenewed`
- `ManualV7CannotInventPreManualConsentFromAnOlderVersion`
- `RehashedInvalidManualProofStillFailsCurrentSemantics`

## [tests/Takupoke.Integration.Tests/RecoveryManualExpandedBudgetTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualExpandedBudgetTests.cs)

- `WholeDocumentBudgetAppliesAcrossOwnedCells`
- `FourActualBodyCorrectionsUseNewContractAndPreserveAcquisition`
- `NewInputContractAllows128ExplicitEditsButNot129Targets`
- `HeaderIsStillReadOnlyAndCannotConsumeNewBudgetForTyping`
- `UnknownFutureSchemaDoesNotAuthorizeReview`

## [tests/Takupoke.Integration.Tests/RecoveryManualInputStateTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualInputStateTests.cs)

- `PrefillRequiresEveryAcknowledgementAndEditingResetsIt`
- `RebindingSameSessionKeepsTypedTextAndAcksButNewIdentityClearsBoth`
- `FourFieldsMissingFieldBlankAndOverLimitCannotSubmit`

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

## [tests/Takupoke.Integration.Tests/RecoveryManualReviewBudgetTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualReviewBudgetTests.cs)

- `ExplicitSelectedConfidentOcrCanBeCorrectedWithoutLoweringItsNativeScore`
- `HeaderAndUnknownSelectionCannotBecomeAHighConfidenceCorrection`
- `UnchangedReviewsAreShownAsCheckedFieldsRatherThanTextCorrections`
- `FourUncertainFieldsCanBeConfirmedWithoutChangingTheirOcrOrConfidence`
- `ConfirmationsDoNotConsumeTheThreeActualFieldEditBudget`
- `FourActualEditsAndUnreviewedOrForgedFieldsRemainRejected`
- `ReviewInputRequiresEveryAcknowledgementAndLimitsActualChanges`
- `ReviewInputStillHasABoundedNumberOfFields`

## [tests/Takupoke.Integration.Tests/RecoveryManualUnchangedInputTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryManualUnchangedInputTests.cs)

- `IdenticalTextSynchronizationPreservesAckAndReady`
- `ActualEditResetsOnlyItsOwnAcknowledgement`
- `ChangingBackToOriginalStillRequiresNewAcknowledgement`
- `ImeCompositionChangesRevokeAckButRepeatedCompositionValueDoesNot`
- `OrdinalUnicodeChangeIsAnActualEdit`
- `HashSnapshotChangeStillClearsAcknowledgements`

## [tests/Takupoke.Integration.Tests/RecoveryMergedSlotTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryMergedSlotTests.cs)

- `SourceDerivedMergedCellKeepsBothSlotsAndOriginalEvidence`
- `TrustRequiresExactlyOneEqualLessonAtEverySameClassAndDaySlot`
- `TrustChargesEveryActualScanAndPropagatesSharedCancellationAndBudgetFailure`

## [tests/Takupoke.Integration.Tests/RecoveryModelStoreTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryModelStoreTests.cs)

- `BadHashKeepsPreviousModelAndPointer`
- `SmokeFailureKeepsPreviousModel`
- `ValidUpdateSwitchesThenRemovesPrevious`
- `UnvalidatedAndInsufficientMemoryNeverDownload`
- `CancelledUpdateDoesNotActivate`
- `ExplicitDeleteRemovesActivePointerAndFile`
- `SameBytesVersionChangeRemovesOldAndCleanupRetainsActiveAndLeasedModels`
- `BundleStartupCleanupRetainsActiveAndLeasedAndUnrelatedDirectories`
- `PartialBundleDeleteFailureKeepsIdentityAcrossRestartAndCanRetry`
- `DownloadProgressCountsActualBytesAcrossArtifactsAndSeparatesCachedInitialization`

## [tests/Takupoke.Integration.Tests/RecoveryNativeExamTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryNativeExamTests.cs)

- `CompleteNativeExamFamilyKeepsTwoRowsEmptyRoomAndOriginalGradeOnlyHeaders`
- `LowerTeacherAndRoomRowsCannotShiftIntoSubjectAndTeacher`
- `FullWidthNativeClockFragmentsKeepWholeOriginalAtomsAcrossLargePrintedGaps`

## [tests/Takupoke.Integration.Tests/RecoveryNativeHeadingTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryNativeHeadingTests.cs)

- `OriginalJoinedHeadingPreservesFullWidthYearEvidence`
- `WholeNativeWeekdayAnnotationRetainsAllOriginalAtoms`
- `CloseNativeClassAndBodyGlyphsAreSeparatedByTheirOriginalPhysicalOwners`

## [tests/Takupoke.Integration.Tests/RecoveryNativeReferenceTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryNativeReferenceTests.cs)

- `OutsideNativeNotesRemainOriginalSourcesAndCannotSupplyBodyRoles`
- `ProjectionMovesAllOriginalEvidenceTogetherAndKeepsNativeTextAndRoles`
- `NativeRoleProofRequiresItsOriginalPdfCurrentCaptureAndVerticalRoleOrder`
- `NativeInkSupportsKeepTheirOriginalBaselineWithoutGivingOcrNewGeometryAuthority`
- `NativeBlankCheckObservesCancellationBeforeAnEarlyInkMatch`
- `TwoPhysicallySplitNativeLessonsKeepOneSlotAndTwoOriginalTuples`
- `PartiallyClippedNativeReferenceNeedsReadOnlyOriginalPixelAcknowledgement`
- `NativeProofWithoutHumanCorrectionsStillRequiresItsOriginalCaptureReceipt`

## [tests/Takupoke.Integration.Tests/RecoveryNativeVisualReviewTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryNativeVisualReviewTests.cs)

- `NativeUnicodeNeedsOriginalCropAndExplicitAcknowledgementWithoutInventingConfidence`
- `HistoricalManualSchemasCannotAuthorizeNativeVisualUncertainty`
- `NativeReviewInputRequiresAcknowledgementAndKeepsHeadersReadOnly`

## [tests/Takupoke.Integration.Tests/RecoveryOcrDotFailureClassificationTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOcrDotFailureClassificationTests.cs)

- `OriginalOcrDotRefusalHasBoundedSourceContractClassification`

## [tests/Takupoke.Integration.Tests/RecoveryOcrDotV9CompatibilityTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOcrDotV9CompatibilityTests.cs)

- `ExactOriginal8PromotionPreservesOriginalObjectsAndEarliestConsent`
- `InvalidOrAmbiguousOriginal8PromotionCannotRevalidate`
- `CertificatesCannotStackOrMasqueradeAsOrdinaryCurrentAcceptance`
- `OldManual7DotCannotBeCertifiedEvenWithMatchingOriginalReceiptAndCertificate8`
- `OrdinaryValidationCannotUseHistoricalEnvelopeToAdmitOldMetadata`
- `SafeManual8PreservesOriginalCaptureCorrectionsAndMetadata`

## [tests/Takupoke.Integration.Tests/RecoveryOcrDotV9Tests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOcrDotV9Tests.cs)

- `KnownOcrBodyDotRefusesBeforeFixedOrProviderFallback`
- `CoreAndCachedOld8CannotBypassKnownOcrDotGate`
- `VectorCompoundNameRetainsOriginalEvidence`
- `SafeOld8RevalidatesWithoutRewritingAnyOriginalObject`
- `GenuineManual7Certificate8SurvivesCurrent9Certification`
- `ForgedManual7Certificate8CannotBePromoted`
- `GoldenLegacyCertificateAndAuditOmitNewNullFields`

## [tests/Takupoke.Integration.Tests/RecoveryOrderedRowsCertificationTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOrderedRowsCertificationTests.cs)

- `OriginalNineConsentRevalidatesWithoutRewritingOriginalObjects`
- `ManualSevenCertificateEightThenNineRetainsBothAncestorsAndConsent`
- `ChangedAncestryCannotAuthorizeAReuse`

## [tests/Takupoke.Integration.Tests/RecoveryOrderedRowsTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOrderedRowsTests.cs)

- `All680UnlabelledTuplesReachFormalProjectionWithOriginalGeometry`
- `LiteralNativeHeaderSpaceDoesNotAuthorizeUnknownTextOrOcrWhitespace`
- `MissingRowsOpaqueAtomsHorizontalPluralAndIncompleteHeadersCannotUseConvention`
- `SwappedRolesMalformedProofAndBackdatedReceiptRefuse`

## [tests/Takupoke.Integration.Tests/RecoveryOutputDecoderTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOutputDecoderTests.cs)

- `CompleteStructureDecodesForLaterValidation`
- `MissingNullNumericAndUnknownFieldsReject`
- `DuplicateFieldsReject`

## [tests/Takupoke.Integration.Tests/RecoveryOverlappingRoleLabelTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryOverlappingRoleLabelTests.cs)

- `CompleteNativeRolePrefixWithoutRecognizedColonKeepsItsValueAtoms`
- `TheSamePrintedPrefixAndItsColonExtensionAreOneRoleLabel`
- `AnIndependentRepeatedSubjectLabelCannotBeDeduplicatedAsAnExtension`
- `APlusIsPreservedAsBodyValueInsteadOfBeingTreatedAsAColon`
- `ARoleWordInsideOneExplicitNativeFieldRemainsLiteralValue`
- `AnotherExplicitPrefixOrIndependentNativeLineCannotBecomeLiteralContinuation`
- `MixedExplicitAndBareParallelColumnsRetainBothLessons`
- `IncompleteMixedParallelColumnsRemainAmbiguous`

## [tests/Takupoke.Integration.Tests/RecoveryParallelProofTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryParallelProofTests.cs)

- `OriginalParallelTuplesSurviveMultiPageStrictRefusalAndEveryMergedSlot`
- `OriginalCompleteAndMergedMultiPageControlsRemainExact`
- `UnprovedOrMisownedSeparatorsCannotPassPreflightResultOrConversion`
- `UnknownOrUnprovedRoleLayoutsRemainTerminal`
- `EmptyParallelTeacherHalfNeedsActualInkFreeBandAndNeverShiftsTheRightTuple`
- `AWholeOriginalOcrLineCannotInventASubstringSeparatorAtom`
- `EveryCoveredSlotMustHaveTheSameOrderedStrictTuples`
- `OversizedAndRepeatedSeparatorProofInputsCannotEscapeLimitsOrReachProviders`
- `HistoricalV5NonparallelJsonAndScopeFingerprintRemainByteExact`

## [tests/Takupoke.Integration.Tests/RecoveryPerformanceTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryPerformanceTests.cs)

- `DenseActualLayoutRetainsEveryOriginalAtomAndConvertsWithinABoundedTime`

## [tests/Takupoke.Integration.Tests/RecoveryPipelineTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryPipelineTests.cs)

- `CanceledVectorRecoveryStopsBeforeLabelOrInkWork`
- `DenseVectorGridStopsAtTheComparisonBudget`
- `DuplicateGlyphIdentityCannotBeAssignedTwoSourceIds`
- `RepeatedOcrCoverageCannotSpendUnboundedPixelWork`
- `EmptyCellChecksShareAPageBudgetDuringRecovery`
- `CanceledRecoveryCannotContinueRasterAnalysisEvenWithNoCandidateInk`
- `PrintedRoleTableUsesRulesBeforeModelsThenFormalAnalysis`
- `RasterEmptyAndUnknownInkAreIndependentOfOcrOutput`
- `FaintOrColoredUndetectedInkCannotBecomeAnEmptyCell`
- `UndetectedInkBesideARealBorderCannotBecomeEmpty`
- `IsolatedLongCharacterStrokeCannotMaskUnrecognizedInkAsARule`
- `DanglingHShapeCannotUseRejectedStrokesToProveItsMiddleRule`
- `FoldedActualLayoutResolvesByRulesThenUsesOriginalValuesInFormalAnalysis`
- `RoleWordsInsideUnlabelledValuesCannotBeStrippedBySubstringMatching`
- `FractionalBlankRegionChecksAllPixelsWhoseCentersAreInside`

## [tests/Takupoke.Integration.Tests/RecoveryPreviewDisplayTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryPreviewDisplayTests.cs)

- `DisplayOwnsImmutableValuesAfterOriginalPreviewCollectionsChange`
- `DisplayRejectsInvalidOrOldValidatorPreviewAndHonorsCancellation`

## [tests/Takupoke.Integration.Tests/RecoveryRenderProfileTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryRenderProfileTests.cs)

- `LegacyPageJsonStaysByteExactAndPngProfileHasItsOwnFingerprint`
- `RenderingProfileChangeCannotReplayManualProof`

## [tests/Takupoke.Integration.Tests/RecoveryRuleMaskEndpointTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryRuleMaskEndpointTests.cs)

- `ActualTwoLaneCornerUnionDoesNotLoseEntirePhysicalBorders`
- `NearbyMissedGlyphOnePixelAwayRemainsUnmasked`
- `AttachedEndpointShortStrokeOutsideDeclaredRangeIsNeverMasked`
- `InternalWhiteHoleCannotBeBridgedByEndpointCorrection`
- `GeometricPhantomPerpendicularRulesDoNotProvePrintedEndpointSupport`
- `ShortPrintedEndpointStubsCannotReplaceFullPerpendicularBorders`
- `MoreThanTwoWhiteEndpointPixelsCannotBeSkipped`
- `MissingOrCompetingPerpendicularEndpointCannotAuthorizeTrim`
- `TwoWhiteEndPixelsFailWhenRemainingSupportMissesMeasuredCenter`
- `TrimmedStartMustCoverMeasuredCenterWithoutRoundingTolerance`
- `GrayEndpointInkIsNeverRemovedAsWhite`
- `SharedPixelBudgetAndCancellationRemainTerminal`

## [tests/Takupoke.Integration.Tests/RecoveryShiftedInlineLabelTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryShiftedInlineLabelTests.cs)

- `UniqueExplicitRolesOnSeparateRowsDoNotNeedIdenticalHorizontalLabelStarts`
- `DeterministicallyProvedInlineBodyRolesCanUseSourceBoundSchemaTwoReview`
- `ShiftedParallelColumnsStillNeedIndependentCorrespondingAnchors`
- `SingleRoleLabelsOnTheSameRowCannotUseTheSeparateRowProof`
