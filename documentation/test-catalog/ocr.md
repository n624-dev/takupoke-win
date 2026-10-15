# CPU OCR・文字位置・モデル・原画像切り出し

対応関係・宣言名・実行方法のSHA-256：
`6eb0ffea4ae3fa57c218dd293f66522b5d2d943a445389b0116d63fec02e00ca`

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
- [src/Takupoke.Infrastructure/Recovery/OcrInkCropFrame.cs](../../src/Takupoke.Infrastructure/Recovery/OcrInkCropFrame.cs)
- [src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs](../../src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs)
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
- `ManualCaptureRetainsSpecialistNativeUncertaintyWhileStrictRoutePreservesPrimary`
- `StandaloneCodesUseSpecialistAndRetainPrimaryWhenNativeOutputFailsThreshold`

## [tests/Takupoke.Integration.Tests/OcrCropCompletenessTests.cs](../../tests/Takupoke.Integration.Tests/OcrCropCompletenessTests.cs)

- `OpenOuterBorderDoesNotEraseClosedInteriorCellsOrHideItsTail`
- `IsolatedLongGlyphAndHShapeDoNotBecomeClosedInteriorRules`
- `ConnectedClippedStrokeExpandsOnlyToItsMinimalUnion`
- `DiagonalStrokeRequiresEightNeighborConnectivity`
- `NearbyDisconnectedUnownedFragmentIsNotBorrowed`
- `ExpandedBoundingRectangleCannotHideDisconnectedUnownedFragment`
- `ExpandedBoundingRectangleCannotEncloseAnotherOwnedFragment`
- `OneConnectedComponentCannotBelongToTwoOriginalCrops`
- `StrokePhysicallyJoinedToProvenRectangularRuleIsRejected`
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
- `RecognitionMatchesIndependentOpenCvLinearSamplesWithinOneByte`
- `DetectionUsesBgrLinearSamplingAndExistingNormalization`
- `CtcTimeCoordinatesInvertPaddingAndRejectUnsupportedIntervals`
- `UnpaddedCtcCoordinatesKeepOriginalFractionalCrop`
- `FractionalCropNeverSamplesNeighborPixelCentres`
- `CapacityAndCancellationFailBeforeReturningInput`

## [tests/Takupoke.Integration.Tests/OcrNativeLineHeaderTests.cs](../../tests/Takupoke.Integration.Tests/OcrNativeLineHeaderTests.cs)

- `CapturedNativeLineRetainsOriginalAtomsAndFailsDownstreamAfterOwnedHeaderSelection`
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
