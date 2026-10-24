# Strict PDF・文字・罫線・試験・返却

対応関係・宣言名・実行方法のSHA-256：
`14a1dea8e4f6a2c43560d4fce7a03e428aa24584bcf5efa3237626dc40822d2c`

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
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs](../../src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs](../../src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPaintVisibility.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPaintVisibility.cs)
- [tests/shared/FictionalTimetablePdf.cs](../../tests/shared/FictionalTimetablePdf.cs)
- [tests/Takupoke.Integration.Tests/fixtures/fictional-noto.json](../../tests/Takupoke.Integration.Tests/fixtures/fictional-noto.json)

## [tests/Takupoke.Integration.Tests/PdfParsingTests.cs](../../tests/Takupoke.Integration.Tests/PdfParsingTests.cs)

- `StrictDrawingGeometryUsesMetricsSpacingAndVerifiedText`
- `RejectsMissingUnicodeInvisibleTextUnfinishedTextAndMismatch`
- `CompositeFontsUseDefaultWidthWhenIndividualWidthsAreMissing`
- `CmapDecodesBfcharAndSequentialRanges`
- `CmapPreservesSurrogatesAndSequentialAndArrayRanges`
- `CmapInnerWorkHonorsCallerCancellation`
- `CmapMaximumDomainPreservesMappings`
- `CmapRejectsOverlappingSpacesAndOutOfDomainCodes`
- `CmapRejectsUnsupportedOrAmbiguousMappings`
- `ContentOrderSurvivesOverlappingCharacterBounds`
- `DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected`
- `MixedHeightTimetableLinesKeepEstablishedVerticalOrder`
- `TimetablePreservesParallelLessonsAndEmptyRoom`
- `TimetableRejectsAmbiguousParallelPairing`
- `CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana`
- `PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges`
- `ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics`
- `ReaderRejectsMissingMappingAndFormObjects`

## [tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs](../../tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs)

- `RuleContrastUsesTheActualOpaqueBackgroundAndRejectsLaterCover`
- `MergedAndVisibleSplitRuleControlsUseTheirActualPeriodSpan`
- `InvisibleStrokeOrFilledBoundaryCannotChangeTheAcceptedMergedSpan`
- `EntirelyWhiteRulesCannotProveAWhitePageTable`
- `CroppedOutAndAnnotationObscuredTimetablesRejectTheWholeInput`
- `FullCropAndNoAnnotationControlsKeepTheSameLesson`
- `RealFontControlUsesItsActualCidMappingAndProducesOneFictionalLesson`
- `DeviceColourSpaceWhiteInkStillRejectsTheWholeInput`
- `DeviceColourSpacesTrackRestorationImplicitSelectionAndInitialBlack`
- `UnverifiedSpacesAndIncorrectComponentCountsAreRejected`
- `StandardDeviceColourSpaceOperatorsProduceTheSameVerifiedBlackLesson`
- `VisibleBlackDeviceColoursAndRestoredStatesKeepTheLesson`
- `WhiteTextOnTheWhitePageRejectsTheWholeInput`
- `OpaqueColouredBackgroundsAreComparedRatherThanDroppingWhiteLetters`
- `StrokeAndFillModesRequireVerifiedVisibleInk`
- `UnprovedTransparencyAndBlendModesRejectInsteadOfPretendingOpaque`
- `LaterPaintAndMixedInvisibleCellsCannotBecomePartialSuccess`
- `PartialBackgroundsAndOverlappingFillSubpathsRemainUnverified`
- `ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpaces`
- `InvisibleReplacementKeepsThePreviousAnalysisAndBothOriginalsAcrossRestart`
