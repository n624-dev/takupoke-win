# 時間割・授業名・時刻・日付

対応ソース・テストのSHA-256：
`47a0a9de27075048049db7e20907dd0d75c2f6b7939021dc2f5752559942597c`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/ClassSelection.cs](../../src/Takupoke.Core/ClassSelection.cs)
- [src/Takupoke.Core/ScheduleModels.cs](../../src/Takupoke.Core/ScheduleModels.cs)
- [src/Takupoke.Core/SchedulePresentation.cs](../../src/Takupoke.Core/SchedulePresentation.cs)
- [src/Takupoke.Core/ScheduleTimes.cs](../../src/Takupoke.Core/ScheduleTimes.cs)
- [src/Takupoke.Core/SchoolDate.cs](../../src/Takupoke.Core/SchoolDate.cs)
- [src/Takupoke.Core/TimetableEngine.cs](../../src/Takupoke.Core/TimetableEngine.cs)

## [tests/Takupoke.Core.Tests/SchedulePresentationTests.cs](../../tests/Takupoke.Core.Tests/SchedulePresentationTests.cs)

- `HomeAlwaysIncludesChangesWhenWeekViewIsSetToNormal`（宣言行 12）
- `EmptyBeforeSubjectUsesOnlyNormalSourceNamesAndNeverSpecialsOrFullNames`（宣言行 20）
- `SpecialNamesUseMappingOnlyOnHomeWhileWeekAndDetailRetainPdfSpelling`（宣言行 32）
- `InternationalChangesAreFilteredFromListAsWellAsGrid`（宣言行 40）
- `HomeWeekContainsTodayOnWeekendWhileInitialWeekCanAdvance`（宣言行 50）
- `GridTransformsNeverChangeHomeTextOrPersistedSource`（宣言行 58）
- `ChangeCardKeepsLastUnambiguousShortSpellingWhenNoneFits`（宣言行 67）

## [tests/Takupoke.Core.Tests/TimetableEngineTests.cs](../../tests/Takupoke.Core.Tests/TimetableEngineTests.cs)

- `AdjacentIdenticalLessonsMergeWithoutChangingInput`（宣言行 17）
- `ParallelLessonsRemainInSeparateLanes`（宣言行 26）
- `LastMakeupWinsAndAllOriginalsRemainInDetails`（宣言行 34）
- `OnlyConfirmedConsecutiveNotationAffectsGrid`（宣言行 43）
- `DisjointChangeRemainsInListWithSeparateClockRanges`（宣言行 56）
- `ApiNoClassSuppressesNormalLessonsButKeepsMakeup`（宣言行 65）
- `MissingExamNeverBorrowsNormalLessonOrClock`（宣言行 72）
- `ExamAndReturnOverlapWithoutLosingEitherLesson`（宣言行 81）
- `NormalModeStillUsesSpecialsButIgnoresChanges`（宣言行 90）
- `InternationalStudentFilterUsesPrefix`（宣言行 96）
- `InProgressIncludesStartAndExcludesEnd`（宣言行 105）
- `CancellationIsNeverInProgress`（宣言行 117）
- `UnknownTermAndOutOfTermNeverLeakNormalLessons`（宣言行 124）
- `ReturnUsesDedicatedClockOnlyOnFirstDate`（宣言行 130）
- `FixedClassSelectionAllowsOnlyFirstYearPairing`（宣言行 138）
- `SemesterBoundsAllowTheWeekOverlappingOctoberFirst`（宣言行 148）
