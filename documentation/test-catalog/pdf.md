# Strict PDF・文字・罫線・試験・返却

対応関係・宣言名・実行方法のSHA-256：
`c243b363cfb0e3b19547ab4b54c8a4a15fe40ee3f8e230c651dd98dcd5d140ab`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Parsing/BoundedXml.cs](../../src/Takupoke.Infrastructure/Parsing/BoundedXml.cs)
- [src/Takupoke.Infrastructure/Parsing/BoundedZip.cs](../../src/Takupoke.Infrastructure/Parsing/BoundedZip.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfLayout.cs](../../src/Takupoke.Infrastructure/Parsing/PdfLayout.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPathEngine.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPathEngine.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfReturnGeometryDiagnostic.cs](../../src/Takupoke.Infrastructure/Parsing/PdfReturnGeometryDiagnostic.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs](../../src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs](../../src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs)

## [tests/Takupoke.Integration.Tests/PdfFontUseTests.cs](../../tests/Takupoke.Integration.Tests/PdfFontUseTests.cs)

- `UnusedUnsupportedFontDoesNotDiscardVerifiedVisibleText`
- `ActualUseOfUnsupportedFontIsRefusedAndNeverCapturedComplete`
- `UnusedSelectionStillValidatesResourceAndSize`
- `CmapCanDescribeUnusedNullButActualNullTextIsRefused`
- `ShownControlCannotBeOmittedAsWhitespace`
- `RestoredFontIsTheActualFontUsedAndUnusedFontLimitRemains`
- `InvalidUnusedCmapStructureStillRefusesEveryActualFontUse`
- `CancellationIsCheckedBeforeDeferredFontResolutionEvenForEmptyShow`

## [tests/Takupoke.Integration.Tests/PdfMissingUnicodeRecoveryTests.cs](../../tests/Takupoke.Integration.Tests/PdfMissingUnicodeRecoveryTests.cs)

- `StrictStillRefusesMissingMapAndRecoveryKeepsExactFontEvidence`
- `UnicodeRecoveryCannotBypassVisibility`
- `ExplicitInvalidMapIsNotReplacedByFontMetadata`
- `KnownToUnicodeRetainsItsExistingAuthorityAndNeedsNoRecoveryProof`
- `ActualGidZeroCannotDisappearFromTheRecoveryOutput`
- `ExplicitCidToGidMappingIsPartOfTheEvidence`
- `NativeDrawingTraceAndGlyphsUseTheSamePageRotation`
- `EffectiveDrawingBasisMatchesScalingRiseSpacingAndTextTransforms`
- `NativeTraceKeepsPositionedTextAdjustmentsAndRepeatedDrawingEvents`
- `FontRecoveryNeverOverridesReplacementClippingOrLaterOcclusion`
- `MissingUnicodeRecoveryChecksCancellationBeforeNativeDrawing`
- `AUniqueCmapEntryWithoutActualDrawableGlyphIsNotRecovered`
- `SameFontNameAndObjectDoNotEraseTheSelectedResourceIdentity`
- `RecoveryRequiresAnExactIndependentDrawingEvent`
- `StrictFirstCaptureCanBeCompletedWithoutRasterForEachTargetKind`
- `IncompleteAlternateReaderCannotReplaceAnAlreadyCompletePage`
- `AnIncompleteRecoveryPageRemainsRasterOnly`
- `VisibleOutlineBeyondPdfAdvanceIsIncludedInSourceGeometry`
- `CompositeOutlineStaysUnsupportedUntilItsRecursionIsBounded`

## [tests/Takupoke.Integration.Tests/PdfParsingTests.cs](../../tests/Takupoke.Integration.Tests/PdfParsingTests.cs)

- `RecoveryRetainsLiteralSpaceWithGeometryWithoutChangingStrictStream`
- `StrictDrawingGeometryUsesMetricsSpacingAndVerifiedText`
- `RejectsMissingUnicodeInvisibleTextUnfinishedTextAndMismatch`
- `CompositeFontsUseDefaultWidthWhenIndividualWidthsAreMissing`
- `CmapDecodesBfcharAndSequentialRanges`
- `CmapHandlesTheFullTwoByteDomainWithoutPairwiseRangeScans`
- `IndexedCmapStillRejectsOverlappingRangesAndUncoveredCodes`
- `CmapRejectsUnsupportedOrAmbiguousMappings`
- `ContentOrderSurvivesOverlappingCharacterBounds`
- `DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected`
- `TimetablePreservesParallelLessonsAndEmptyRoom`
- `ConflictingTitleYearsCannotSelectTheFirstYear`
- `RepeatedOrEquivalentTitleYearsRemainValid`
- `TimetableCannotShiftRoomIntoMissingTeacher`
- `AnnotationInAnUnusedTableRowCannotTurnTeacherIntoSubject`
- `AWholeTableOutsideTheVisiblePageCannotBeAccepted`
- `ShortCellCalibrationPreservesTeacherHoleAndRejectsTeacherOnlyEvenWithOutsideAnnotation`
- `ManyShortCellsReuseCalibrationsDespiteUnrelatedPageText`
- `ShortCellUsesOnlyMatchingCompleteNeighborsWhenOtherFontsHaveDifferentBaselines`
- `ShortCellStillRefusesWhenDifferentMatchingNeighborsDisagreeOnFieldRoles`
- `GridRuleWorkHasASharedLimitAcrossRepeatedQueries`
- `PartialRoleLabelInSecondParallelLessonCannotBypassRecoveryConfirmation`
- `TimetableRejectsAmbiguousParallelPairing`
- `TimetablePreservesSingleCompoundField`
- `CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana`
- `PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges`
- `ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics`
- `ReaderAcceptsFullyOpaqueGraphicsStateForAllScheduleKinds`
- `SpecialReaderRejectsInvisibleOrUnsupportedGraphicsState`
- `SpecialReaderCannotTreatClippedOrMarkedTextAsProvenVisible`
- `ReaderDoesNotReuseWhiteOrOverpaintedText`
- `ReaderStillReusesBlackTextAfterAnEarlierBackground`
- `OpaqueDeviceColourPreservesOriginalTextPositionAndCapture`
- `DeviceColourCannotBypassContrastRangeOrGraphicsState`
- `OnlyTheUsedDeviceColourRequiresItsOwnUnsubstitutedSpace`
- `GraphicsStateLineWidthCannotBypassTheOverpaintCheck`
- `WhiteFillDoesNotCreateAnInvisibleRuleAndBlackStrokeRemainsVisible`
- `UnsupportedPaintCannotBeCertifiedAsCompleteSource`
- `CroppedOutTextCannotBeReusedAsCompleteInput`
- `InvisibleDashedSegmentsAreNotReusedAsSolidTableRules`
- `ReaderRejectsMissingMappingAndFormObjects`
- `ReaderCaptureKeepsCompleteLayoutAndResetsForAnotherAttempt`
- `ImageAndMissingRulesHaveRecoverableClassification`
- `OrdinaryAcceptanceDoesNotGrantCertifiedRecoveryCompleteness`
- `OrdinaryTimetableAcceptsOmittedTrailingFieldsWithoutCertifyingRoles`
- `DistinctRejectionsRetainStableSitesWithoutSourceText`

## [tests/Takupoke.Integration.Tests/PdfTrueTypeRecoveryMapTests.cs](../../tests/Takupoke.Integration.Tests/PdfTrueTypeRecoveryMapTests.cs)

- `IdentityAndExplicitCidMapKeepExactFontGlyphAndScalar`
- `WhitespaceAliasesAreAmbiguousAndNeverReplaced`
- `EveryUnicodeCmapContributesAndConflictsRemainUnresolved`
- `Format4RangeArrayUsesItsOwnPositionAndDelta`
- `UnsupportedUnicodeCmapCannotBeIgnoredEvenWithOneGoodMap`
- `DuplicateSubtableReferencesDoNotConsumeTheWorkBudgetTwice`
- `EveryRequiredBytePrefixIsRejected`
- `DuplicateAndOverlappingSfntTablesAreRejected`
- `InvalidScalarAndGlyphRangesAreRejected`
- `OutOfOrderGroupsAndMissingUnicodeMapsAreRejected`
- `CancellationIsPropagatedBeforeAnyMetadataResult`
- `ExcessiveDistinctMappingWorkIsRefusedWithoutTruncation`

## [tests/Takupoke.Integration.Tests/PdfVisibleUnicodeRecoveryTests.cs](../../tests/Takupoke.Integration.Tests/PdfVisibleUnicodeRecoveryTests.cs)

- `BoundedFillAndStrokeUsesOriginalExplicitUnicodeAndIndependentInk`
- `DisjointThinFilledRulesAfterTextDoNotInvalidateTheWholePage`
- `ActualThinPaintExtentCanBeDisjointWhereTheConservativeLegacyMarginOverlaps`
- `OriginalRoundJoinHasItsOwnBoundedStrokeEnvelope`
- `EarlierGridAtGlyphEdgeDoesNotBecomeLaterOverpaint`
- `EarlierRuleCannotHideTheWholeOriginalFilledGlyph`
- `LaterPaintCrossingOriginalInkOrOpaqueBlocksStillRefuses`
- `OnePartialLaterGridIntersectionRequiresOriginalPixelReview`
- `FullyContainedNativeInkSupportsRectangleClipsAndPlainArtifactOrEarlierWhiteBackground`
- `ClippedOrUnbalancedNativeTextCannotBePromoted`
- `ClipRestorationAndNonSemanticMetadataOrContrastingGrayBackgroundPreserveNativeText`
- `InsufficientBackgroundContrastAndSemanticReplacementAreStillRefused`
- `PartialOriginalRectangleClipRequiresReviewInsteadOfPretendingFullVisibility`
- `EntirelyCoveredOriginalGlyphIsNotExposedAsVisibleSource`
- `OrdinaryAcceptanceAndCertifiedVisibleInkHaveIndependentContracts`
- `BoundedRecoveryCannotOverrideUnprovedVisibility`
- `MissingUnicodeIsNotFilledByThisExplicitUnicodeEntry`
- `OverprintCertificationTracksBothChannelsAndGraphicsStack`
- `BackgroundCannotCertifyAHiddenEarlierGridEvenWhenAllTextIsDisjoint`
