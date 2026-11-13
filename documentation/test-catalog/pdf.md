# Strict PDF・文字・罫線・試験・返却

対応関係・宣言名・実行方法のSHA-256：
`2254f09aeb5687d2b2e17ca06933263ec910aad66682a422b71c3055a7f724bf`

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
- [tests/Takupoke.Integration.Tests/fixtures/fictional-noto-special.json](../../tests/Takupoke.Integration.Tests/fixtures/fictional-noto-special.json)

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

- `SpecialTextShowOrderIncludesEmptyStringsQuotesAndWholeTJ`
- `RealFontSpecialControlsKeepFullExamAndReturnTimetables`
- `SpecialPaintMustProveActualTextVisibility`
- `IndependentCompoundFillControlsKeepTheSpecialTimetable`
- `HiddenSpecialReplacementKeepsPreviousGoodAfterEncryptedStoreRestart`
- `RealFontSpecialReaderAcceptsOnlyClipsContainingEveryGlyphAndRule`
- `SpecialStrokePaintRequiresItsOutlineUnlessTheCompleteSameInkFillRemains`
- `SameInkModeTwoRequiresTheEntireOpaqueFilledGlyphInsideTheClip`
- `SpecialClipBoundsMiterJoinsAndRefusesUnboundedDeviceHairlines`
- `SpecialClipContainsStrokeInkIncludingTransformAndTextMode`
- `RectangularSpecialClipsPreserveOnlyUncutInkAndRestoreOnQ`
- `IntersectingFilledRulesAndPartialEarlierBackgroundKeepContrastChecks`
- `OrdinaryTimetablesKeepTheirCompleteBackgroundRequirement`
- `ScopedReturnSubsetCannotReplaceTheFullReturnAfterRestart`
- `FivePageExamScopeSurvivesEncryptedRestartAndMalformedReplacement`
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
