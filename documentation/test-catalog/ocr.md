# CPU OCR・文字位置・モデル・原画像切り出し

対応関係・宣言名・実行方法のSHA-256：
`d19a2386b26775bf397fdf67bc6bab7ef40b84556488546f40d46a5fe13c0976`

環境：Linux／Windows .NET。Windows描画・実推論は対応workflowで検証

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/Recovery/RecoveryCodeCandidates.cs](../../src/Takupoke.Core/Recovery/RecoveryCodeCandidates.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrAsciiRefinement.cs](../../src/Takupoke.Infrastructure/Recovery/OcrAsciiRefinement.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrCropCompleteness.cs](../../src/Takupoke.Infrastructure/Recovery/OcrCropCompleteness.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrCtcDecoder.cs](../../src/Takupoke.Infrastructure/Recovery/OcrCtcDecoder.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrDetectorProbability.cs](../../src/Takupoke.Infrastructure/Recovery/OcrDetectorProbability.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrHeaderContext.cs](../../src/Takupoke.Infrastructure/Recovery/OcrHeaderContext.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrInkCropFrame.cs](../../src/Takupoke.Infrastructure/Recovery/OcrInkCropFrame.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs](../../src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrRoleLabelRetry.cs](../../src/Takupoke.Infrastructure/Recovery/OcrRoleLabelRetry.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrWhiteGapPartition.cs](../../src/Takupoke.Infrastructure/Recovery/OcrWhiteGapPartition.cs)
- [src/Takupoke.Infrastructure/Recovery/OnnxAsciiOcr.cs](../../src/Takupoke.Infrastructure/Recovery/OnnxAsciiOcr.cs)
- [src/Takupoke.Infrastructure/Recovery/OnnxJapaneseOcr.cs](../../src/Takupoke.Infrastructure/Recovery/OnnxJapaneseOcr.cs)
- [src/Takupoke.Win/Assets/Recovery/foundry-candidates.json](../../src/Takupoke.Win/Assets/Recovery/foundry-candidates.json)
- [src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json](../../src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json)
- [src/Takupoke.Win/Assets/Recovery/ocr-characters.json](../../src/Takupoke.Win/Assets/Recovery/ocr-characters.json)
- [tests/Takupoke.Integration.Tests/CapturedOcrHeaderFixture.cs](../../tests/Takupoke.Integration.Tests/CapturedOcrHeaderFixture.cs)
- [tests/Takupoke.Integration.Tests/OcrHeaderGoldenSnapshots.cs](../../tests/Takupoke.Integration.Tests/OcrHeaderGoldenSnapshots.cs)

## [tests/Takupoke.Core.Tests/RecoveryCodeCandidatesTests.cs](../../tests/Takupoke.Core.Tests/RecoveryCodeCandidatesTests.cs)

- `JapaneseRoomNamesKeepEveryNonconfusableCharacterAndLiteralSpace`
- `KnownRosterSuppliesOnlyLiteralConfusionsAndKeepsMultipleAlternatives`
- `UnrelatedNamesUnknownCodesCaseAndTooManyChangesAreNotInvented`
- `EveryClassInMergedCellMustBeCoveredAndGlobalNamesRemainAvailable`
- `FullNameMayBeCodeButWorkAndCancellationStayBounded`

## [tests/Takupoke.Integration.Tests/ExamCompositeOcrHeaderTests.cs](../../tests/Takupoke.Integration.Tests/ExamCompositeOcrHeaderTests.cs)

- `CompleteConsistentHeadersOwnTheEntirePhysicalSixPeriodBand`
- `MismatchedDuplicateOrIncompleteCompositeRefuses`
- `TextCannotOverrideSourceOrPhysicalOwnership`
- `VectorLegacyGradeHeadersRetainGapAndPageMapping`
- `OcrLegacyGradeOnlyRemainsSupportedWhenWholeOwnerHasNoSuffixInk`
- `VectorCompositeDoesNotEnableNewGrammar`

## [tests/Takupoke.Integration.Tests/OcrAsciiRefinementTests.cs](../../tests/Takupoke.Integration.Tests/OcrAsciiRefinementTests.cs)

- `NoncanonicalHeaderRetainsRawPrimaryAndItsNativeConfidence`
- `LowConfidenceOrDifferentLengthTailIsNotSubstitutedAndPrefixIsUnchanged`
- `UnrelatedAsciiDoesNotRouteAndMissingSourceCropRefuses`
- `OptionalCodeExpertKeepsStrictHeadersAndProseAndPreservesManualNativeScores`
- `InvalidExpertCodeOrFramedPaddingUsesCompleteOriginalSpecialistDecision`
- `NumericOnlyTailKeepsPrimaryWhenSpecialistUncertain`
- `ManualCaptureRetainsSpecialistNativeUncertaintyWhileStrictRoutePreservesPrimary`
- `StandaloneCodesUseSpecialistAndRetainPrimaryWhenNativeOutputFailsThreshold`

## [tests/Takupoke.Integration.Tests/OcrCropCompletenessTests.cs](../../tests/Takupoke.Integration.Tests/OcrCropCompletenessTests.cs)

- `HeaderContextCannotHideFaintInkInAddedFringeFarFromCtcPeak`
- `OpenOuterBorderDoesNotEraseClosedInteriorCellsOrHideItsTail`
- `IsolatedLongGlyphAndHShapeDoNotBecomeClosedInteriorRules`
- `ConnectedClippedStrokeExpandsOnlyToItsMinimalUnion`
- `DiagonalStrokeRequiresEightNeighborConnectivity`
- `NearbyDisconnectedUnownedFragmentIsNotBorrowed`
- `ExpandedBoundingRectangleCannotHideDisconnectedUnownedFragment`
- `ExpandedBoundingRectangleCannotEncloseAnotherOwnedFragment`
- `OneConnectedComponentCannotBelongToTwoOriginalCrops`
- `StrokePhysicallyJoinedToProvenRectangularRuleIsRejected`
- `ManualContainedRuleContactRetainsOriginalPixelsAndStillRejectsClippedStroke`
- `ManualRuleContactCannotCombineOwnersOrHideUnownedAttachedText`
- `ProvenPhysicalRulePixelsAloneDoNotRequireTextCrop`
- `OriginalWhiteCropOverlapIsRejected`
- `ExpansionCannotOverlapEvenWhiteSupportOfAnotherCrop`
- `FullyContainedInkKeepsOriginalFractionalCropExactly`
- `ConnectedInkAtImageEdgeCompletesWithinOriginalImageBounds`
- `InvalidOrOutOfImageCropDoesNotReturnCompletedBounds`
- `SubpixelCropWithoutAnyPixelCentreIsRejected`
- `MalformedOriginalRasterIsRejected`
- `CancelledOperationDoesNotReturnCompletedCrop`
- `CropScansShareExistingPixelWorkAllowanceWithOtherStages`
- `OriginalFictionalYearPixelsCompleteRecordedClippingWithoutChangingImage`

## [tests/Takupoke.Integration.Tests/OcrCtcDecoderTests.cs](../../tests/Takupoke.Integration.Tests/OcrCtcDecoderTests.cs)

- `RepeatsCollapseButBlankSeparatedCharactersAndPrintedSpaceRemain`
- `EqualScoresKeepFirstTokenAndBlankDoesNotCreateEmptyEvidence`
- `InvalidLosingTokenCannotBeHiddenByAValidWinner`
- `DifferentPinnedModelAlphabetsKeepTheirLastSpaceToken`
- `ReversedTensorStridesPreserveLogicalTimeAndSymbolCoordinates`
- `IncorrectBatchTimeOrAlphabetShapeIsRefused`
- `CancellationIsCheckedBeforeAndAfterDecodingTheFirstRow`

## [tests/Takupoke.Integration.Tests/OcrDetectorProbabilityTests.cs](../../tests/Takupoke.Integration.Tests/OcrDetectorProbabilityTests.cs)

- `OnlyOneDeclaredNchwProbabilityMapIsAllowed`
- `InDomainValuesKeepTheirExactBits`
- `OnlyOneUpperFloat32NeighbourNormalizesToUnit`
- `LargerNegativeAndNonfiniteValuesStillRefuse`

## [tests/Takupoke.Integration.Tests/OcrHeaderPrescreenTests.cs](../../tests/Takupoke.Integration.Tests/OcrHeaderPrescreenTests.cs)

- `FullSizedIndependentRowsKeepExactLabelEvidenceAndReduceMeasuredWork`
- `AdversarialHeadersHaveExactlyTheOldOutput`
- `ExcludedUnknownSourceStillExistsAndFailsGlobalClassification`
- `ComparisonBudgetRemainsTwentyMillionAndCancelable`

## [tests/Takupoke.Integration.Tests/OcrInkCropFrameTests.cs](../../tests/Takupoke.Integration.Tests/OcrInkCropFrameTests.cs)

- `RetainsDisconnectedAndColoredOriginalInkWithFixedMargin`
- `NoRuleMaskRetainsDisconnectedRuleLookingAndFaintColoredPixels`
- `ProvenRulesDoNotWidenTextButNoEmptyFieldIsInvented`
- `InvalidMaskBoundsAndCancellationAreNotAccepted`

## [tests/Takupoke.Integration.Tests/OcrInputTransformTests.cs](../../tests/Takupoke.Integration.Tests/OcrInputTransformTests.cs)

- `DecodedSpaceRemainsInEvidenceAtItsRealCtcPosition`
- `ReferenceWidthsAndNormalizedZeroPadding`
- `UnpaddedRetryPreservesEverySampleAndRequiresNewTimeCoordinates`
- `RecognitionMatchesIndependentOpenCvLinearSamplesWithinOneByte`
- `DetectionUsesBgrLinearSamplingAndExistingNormalization`
- `CtcTimeCoordinatesInvertPaddingAndRejectUnsupportedIntervals`
- `UnpaddedCtcCoordinatesKeepOriginalFractionalCrop`
- `FractionalCropNeverSamplesNeighborPixelCentres`
- `CapacityAndCancellationFailBeforeReturningInput`

## [tests/Takupoke.Integration.Tests/OcrNativeLineHeaderTests.cs](../../tests/Takupoke.Integration.Tests/OcrNativeLineHeaderTests.cs)

- `CapturedNativeLineRetainsOriginalAtomsAndFailsDownstreamAfterOwnedHeaderSelection`
- `ShortCodeCropCannotMoveBeforeItsProvedOriginalJapaneseContinuation`
- `OriginalWhitespaceBetweenExplicitYearAndTermKeepsItsHeaderOwnership`
- `NativeMetadataCannotEnableSupplementalHeadersForVectorPages`
- `YearSuffixOnAnotherNativeLineIsNotJoined`
- `NativeLineIdentityReusedOnDifferentRowsIsRejected`
- `InvalidNativeOrderIsNotSortedOrRepaired`
- `PhysicalRuleCrossingNativeHeaderRejectsSupplementalYear`
- `IndependentLegacyHeadersRemainAvailableWhenNativeLineCrossesRule`
- `OtherNativeLineOverlappingHeaderIsNotIgnored`
- `RulesOnlyTouchingNativeHeaderBoundaryKeepOriginalYear`
- `WholeAtomicYearAndTermCannotBeSplitIntoInventedAtoms`
- `ObservedWrongYearStaysWrongWithoutFixtureGoldRepair`
- `DifferentObservedYearsRemainAmbiguous`
- `LegacyAndSupplementalHeadersDoNotDuplicateEvidenceAtoms`

## [tests/Takupoke.Integration.Tests/OcrRoleLabelRetryTests.cs](../../tests/Takupoke.Integration.Tests/OcrRoleLabelRetryTests.cs)

- `RetryRequiresAnAlreadyObservedRoleLabel`
- `NewNativeObservationMustMatchEveryLabelLetter`
- `PrefixBoundaryNeedsACompletelyWhiteOriginalColumn`

## [tests/Takupoke.Integration.Tests/OcrSourceRowExtractionTests.cs](../../tests/Takupoke.Integration.Tests/OcrSourceRowExtractionTests.cs)

- `AllFiveCapturedExactNativeDatesStayOneStrictRun`
- `UnsafeMetadataCannotJoinTheCapturedDate`
- `VectorSpacingKeepsTheOriginalGapRule`
- `VectorSourceLineMetadataDoesNotEnableOcrGrouping`
- `InvalidGeometryCannotEstablishAnOcrRow`
- `CapturedWeekdayHeaderUsesEveryOriginalEvidenceAtom`
- `UnsafeWeekdayCannotGainAWholeRowEvidenceChain`
- `ChangedHeaderEvidenceCannotReuseAnAcceptedScope`

## [tests/Takupoke.Integration.Tests/OcrWhiteGapPartitionTests.cs](../../tests/Takupoke.Integration.Tests/OcrWhiteGapPartitionTests.cs)

- `UsesWidestOriginalWhiteBandAndPreservesWholeRightExtent`
- `NonwhiteAtEitherEdgeOfFractionalHeightBlocksCut`
- `ColoredPixelCannotBeTreatedAsWhiteAndEqualBandsUseLeftmost`
- `InvalidBoundaryDoesNotCreateCrop`
- `InvalidSourceBoundsAndCancellationAreRetained`

## [tests/Takupoke.Integration.Tests/OnnxAsciiOcrTests.cs](../../tests/Takupoke.Integration.Tests/OnnxAsciiOcrTests.cs)

- `NativeOutputsRemainLiteralAndLowConfidenceIsNotRaised`
- `UnsupportedPaddingRetainsAllRawPiecesButProvidesNoPartialEvidence`
- `CancellationAndUnsupportedCropDoNotProduceObservation`
- `MalformedDictionaryIsRefusedBeforeOpeningModel`
