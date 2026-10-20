# 時間割・授業名・時刻・日付

対応関係・宣言名・実行方法のSHA-256：
`e40c9fa9f4a8c1f38090f204acc3920ee3c641ef0748ae4d470055f2f5f2e7ea`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/ClassSelection.cs](../../src/Takupoke.Core/ClassSelection.cs)
- [src/Takupoke.Core/PdfClassAssignment.cs](../../src/Takupoke.Core/PdfClassAssignment.cs)
- [src/Takupoke.Core/ScheduleModels.cs](../../src/Takupoke.Core/ScheduleModels.cs)
- [src/Takupoke.Core/SchedulePresentation.cs](../../src/Takupoke.Core/SchedulePresentation.cs)
- [src/Takupoke.Core/ScheduleTimes.cs](../../src/Takupoke.Core/ScheduleTimes.cs)
- [src/Takupoke.Core/SchoolDate.cs](../../src/Takupoke.Core/SchoolDate.cs)
- [src/Takupoke.Core/TimetableEngine.cs](../../src/Takupoke.Core/TimetableEngine.cs)

## [tests/Takupoke.Core.Tests/PdfClassAssignmentTests.cs](../../tests/Takupoke.Core.Tests/PdfClassAssignmentTests.cs)

- `OnlyOneStandaloneDepartmentChangesFirstYearAffiliation`
- `EmptyFirstYearSpecialTableSuppressesDepartmentOrdinaryLessonWithoutInventingAnExam`

## [tests/Takupoke.Core.Tests/SchedulePresentationTests.cs](../../tests/Takupoke.Core.Tests/SchedulePresentationTests.cs)

- `HomeAlwaysIncludesChangesWhenWeekViewIsSetToNormal`
- `EmptyBeforeSubjectUsesMappedNormalNamesForDetailsAndNeverSpecials`
- `SpecialNamesUseMappingOnlyOnHomeWhileWeekAndDetailRetainPdfSpelling`
- `InternationalChangesAreFilteredFromListAsWellAsGrid`
- `HomeWeekContainsTodayOnWeekendWhileInitialWeekCanAdvance`
- `CardDisplayTransformsKeepStoredSourceAndKanaPoliciesSeparate`
- `ChangeCardKeepsLastUnambiguousShortSpellingWhenNoneFits`

## [tests/Takupoke.Core.Tests/TimetableEngineTests.cs](../../tests/Takupoke.Core.Tests/TimetableEngineTests.cs)

- `AdjacentIdenticalLessonsMergeWithoutChangingInput`
- `ParallelLessonsRemainInSeparateLanes`
- `LastMakeupWinsAndAllOriginalsRemainInDetails`
- `OnlyConfirmedConsecutiveNotationAffectsGrid`
- `DisjointChangeRemainsInListWithSeparateClockRanges`
- `ApiNoClassSuppressesNormalLessonsButKeepsMakeup`
- `MissingExamNeverBorrowsNormalLessonOrClock`
- `ExamAndReturnOverlapWithoutLosingEitherLesson`
- `NormalModeStillUsesSpecialsButIgnoresChanges`
- `InternationalStudentFilterUsesPrefix`
- `HiddenInternationalCancellationPreservesOrdinaryLessonsAndClock`
- `HiddenChangesPreserveVisibleCancellationAndMakeupPriority`
- `HiddenCancellationPreservesSpecialLessonsWithoutFillingSpecialBlanks`
- `HiddenCancellationKeepsWeekendWithWeekdayOverrideVisible`
- `InProgressIncludesStartAndExcludesEnd`
- `CancellationIsNeverInProgress`
- `UnknownTermAndOutOfTermNeverLeakNormalLessons`
- `ReturnUsesDedicatedClockOnlyOnFirstDate`
- `FixedClassSelectionAllowsOnlyFirstYearPairing`
- `SemesterBoundsAllowTheWeekOverlappingOctoberFirst`
