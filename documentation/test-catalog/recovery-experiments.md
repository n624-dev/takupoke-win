# OCR・native PDFの比較用workflowと再現条件

対応関係・宣言名・実行方法のSHA-256：
`b5b410941f2fd5c5811adde4cd5228b2ca873c1f2c13c0967aeea111d97e77c7`

環境：Linux／Windows .NETと明示したWindows比較workflow

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [.github/workflows/independent-wide-vector-native.yml](../../.github/workflows/independent-wide-vector-native.yml)
- [.github/workflows/label-selection-native-probe.yml](../../.github/workflows/label-selection-native-probe.yml)
- [.github/workflows/native-grammar-error-probe.yml](../../.github/workflows/native-grammar-error-probe.yml)
- [.github/workflows/native-model-batch-comparison.yml](../../.github/workflows/native-model-batch-comparison.yml)
- [.github/workflows/ocr-acquisition-accuracy.yml](../../.github/workflows/ocr-acquisition-accuracy.yml)
- [.github/workflows/ocr-acquisition-probe.yml](../../.github/workflows/ocr-acquisition-probe.yml)
- [.github/workflows/ocr-literal-acquisition-diagnostic.yml](../../.github/workflows/ocr-literal-acquisition-diagnostic.yml)
- [.github/workflows/ocr-literal-factory-validation.yml](../../.github/workflows/ocr-literal-factory-validation.yml)
- [.github/workflows/ocr-literal-retry-accuracy.yml](../../.github/workflows/ocr-literal-retry-accuracy.yml)
- [.github/workflows/ocr-literal-retry-native.yml](../../.github/workflows/ocr-literal-retry-native.yml)
- [.github/workflows/ocr-model-comparison.yml](../../.github/workflows/ocr-model-comparison.yml)
- [.github/workflows/ocr-original-image.yml](../../.github/workflows/ocr-original-image.yml)
- [.github/workflows/ocr-physical-framing.yml](../../.github/workflows/ocr-physical-framing.yml)
- [.github/workflows/ocr-production-validation.yml](../../.github/workflows/ocr-production-validation.yml)
- [.github/workflows/ocr-source-order-accuracy.yml](../../.github/workflows/ocr-source-order-accuracy.yml)
- [.github/workflows/ocr-whole-model-comparison.yml](../../.github/workflows/ocr-whole-model-comparison.yml)
- [.github/workflows/ordered-row-proof-native.yml](../../.github/workflows/ordered-row-proof-native.yml)
- [.github/workflows/pdf-colour-acquisition.yml](../../.github/workflows/pdf-colour-acquisition.yml)
- [.github/workflows/pdf-source-final.yml](../../.github/workflows/pdf-source-final.yml)
- [.github/workflows/raster-acquisition-native-probe.yml](../../.github/workflows/raster-acquisition-native-probe.yml)
- [.github/workflows/readable-native-probe.yml](../../.github/workflows/readable-native-probe.yml)
- [.github/workflows/recovery-model-evaluation.yml](../../.github/workflows/recovery-model-evaluation.yml)
- [.github/workflows/single-header-role-native-probe.yml](../../.github/workflows/single-header-role-native-probe.yml)
- [.github/workflows/windows-fictional-benchmark.yml](../../.github/workflows/windows-fictional-benchmark.yml)
- [.github/workflows/windows-system-ocr.yml](../../.github/workflows/windows-system-ocr.yml)

## [tests/Takupoke.Integration.Tests/OnnxAsciiOcrTests.cs](../../tests/Takupoke.Integration.Tests/OnnxAsciiOcrTests.cs)

- `NativeOutputsRemainLiteralAndLowConfidenceIsNotRaised`
- `UnsupportedPaddingRetainsAllRawPiecesButProvidesNoPartialEvidence`
- `CancellationAndUnsupportedCropDoNotProduceObservation`
- `MalformedDictionaryIsRefusedBeforeOpeningModel`

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

## [tests/Takupoke.Integration.Tests/RecoveryCapturedLayoutTests.cs](../../tests/Takupoke.Integration.Tests/RecoveryCapturedLayoutTests.cs)

- `CompleteFilledVectorCellsKeepOriginalTextAndCoordinatesWithoutRaster`
- `BlankCellsStillRequireRasterEvidenceAndIncompleteReaderOutputCannotBeComplete`
- `CompleteButInvalidGeometryIsRejectedInsteadOfBeingHiddenByRasterFallback`
