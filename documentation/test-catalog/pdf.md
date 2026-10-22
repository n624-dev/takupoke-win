# Strict PDF・文字・罫線・試験・返却

対応関係・宣言名・実行方法のSHA-256：
`8bb7dea3f58eef71fe466036a895cbf4ac46d6189fb6e8d934cd0ea114030340`

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
- `CmapRejectsUnsupportedOrAmbiguousMappings`
- `ContentOrderSurvivesOverlappingCharacterBounds`
- `DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected`
- `TimetablePreservesParallelLessonsAndEmptyRoom`
- `TimetableRejectsAmbiguousParallelPairing`
- `CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana`
- `PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges`
- `ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics`
- `ReaderRejectsMissingMappingAndFormObjects`

## [tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs](../../tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs)

- `RealFontControlUsesItsActualCidMappingAndProducesOneFictionalLesson`
- `VisibleBlackDeviceColoursAndRestoredStatesKeepTheLesson`
- `WhiteTextOnTheWhitePageRejectsTheWholeInput`
- `OpaqueColouredBackgroundsAreComparedRatherThanDroppingWhiteLetters`
- `StrokeAndFillModesRequireVerifiedVisibleInk`
- `UnprovedTransparencyAndBlendModesRejectInsteadOfPretendingOpaque`
- `LaterPaintAndMixedInvisibleCellsCannotBecomePartialSuccess`
- `PartialBackgroundsAndOverlappingFillSubpathsRemainUnverified`
- `ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpaces`
- `InvisibleReplacementKeepsThePreviousAnalysisAndBothOriginalsAcrossRestart`
