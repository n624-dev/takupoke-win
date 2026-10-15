# PDF埋め込みフォント・可視文字・原本の描画照合

対応関係・宣言名・実行方法のSHA-256：
`c458f36797f94bb956c6652c44050bb5c2a1ddb08463cde46c345330b10d315e`

環境：Linux／Windows .NET。Windows描画は対応workflowで検証

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Parsing/PdfNativeGlyphTrace.cs](../../src/Takupoke.Infrastructure/Parsing/PdfNativeGlyphTrace.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader+FontRecovery.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader+FontRecovery.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader+VisibleRecovery.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader+VisibleRecovery.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfTrueTypeRecoveryMap.cs](../../src/Takupoke.Infrastructure/Parsing/PdfTrueTypeRecoveryMap.cs)

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
- `OrdinaryImportReusesOnlyCompletelyProvenVisibleNativeText`
- `BoundedRecoveryCannotOverrideUnprovedVisibility`
- `MissingUnicodeIsNotFilledByThisExplicitUnicodeEntry`
