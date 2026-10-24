# 時間割・授業名・時刻・日付

対応関係・宣言名・実行方法のSHA-256：
`64dce3bb26113283b4c997f7082b60d9b63dfbaf7568abce41d0cf834a323b43`

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
- [src/Takupoke.Core/HomeActivityClock.cs](../../src/Takupoke.Core/HomeActivityClock.cs)

## [tests/Takupoke.Core.Tests/SchedulePresentationTests.cs](../../tests/Takupoke.Core.Tests/SchedulePresentationTests.cs)

- `ContextualInternationalChangesUseClassAndSchoolYearBeforeReplacingOrdinaryLessons`
- `DisjointChangeDetailsFindOnlyRequestedOriginalsWithoutChangingGrid`
- `HomeAlwaysIncludesChangesWhenWeekViewIsSetToNormal`
- `EmptyBeforeSubjectUsesOnlyNormalSourceNamesAndNeverSpecialsOrFullNames`
- `SpecialNamesUseMappingOnlyOnHomeWhileWeekAndDetailRetainPdfSpelling`
- `InternationalChangesAreFilteredFromListAsWellAsGrid`
- `HomeWeekContainsTodayOnWeekendWhileInitialWeekCanAdvance`
- `GridTransformsNeverChangeHomeTextOrPersistedSource`
- `ChangeCardKeepsLastUnambiguousShortSpellingWhenNoneFits`

## [tests/Takupoke.Core.Tests/TimetableEngineTests.cs](../../tests/Takupoke.Core.Tests/TimetableEngineTests.cs)

- `ProjectionMemoIsScopedToItsEngineAndEvictionRetainsExactResults`
- `ChangeAndMakeupMergesRespectBreaksAndLunch`
- `IndependentSpecialCellsKeepTheirGapWhenVisuallyMerged`
- `HiddenSpecialDoesNotRemoveTheVisibleCommonClock`
- `ChangeClockIsUnknownWhenOneRequiredSpecialKindIsMissing`
- `OverlappingSpecialCardsKeepIndividualTimesAndProgressWhileCommonAndChangeRemainAmbiguous`
- `MergedSpecialCardsUseOnlyTheirOwnSourceAndNeverBorrowMissingTimes`
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
- `HiddenInternationalChangesKeepOrdinaryAndSpecialLessonsIncludingSaturday`
- `InProgressIncludesStartAndExcludesEnd`
- `CancellationIsNeverInProgress`
- `UnknownTermAndOutOfTermNeverLeakNormalLessons`
- `ReturnUsesDedicatedClockOnlyOnFirstDate`
- `FixedClassSelectionAllowsOnlyFirstYearPairing`
- `SemesterBoundsAllowTheWeekOverlappingOctoberFirst`
- `ReplacedMergedSpecialUsesVerifiedClocksForRemainingFragments`
- `ConflictingOverridesWarnAndUseRealWeekdayWithoutLosingOtherTags`
- `LegacyReturnRangeRemainsVerbatimButUnknownColumnDateCannotSupplyPeriodClocks`

## [tests/Takupoke.Core.Tests/ClockEvidenceTests.cs](../../tests/Takupoke.Core.Tests/ClockEvidenceTests.cs)

- `WholeReplacementKeepsRecordedContinuousClockWhilePartialChangesUsePeriodClocks`
- `WholeReplacementRequiresEveryApplicableDocumentToProvideTheSameRange`
- `NormalVisualMergesKeepBreaksAndLunchWithDefaultAndDateSpecificClocks`

## [tests/Takupoke.Core.Tests/HomeActivityClockTests.cs](../../tests/Takupoke.Core.Tests/HomeActivityClockTests.cs)

- `OnlyCardSetChangesTriggerRedrawIncludingSimultaneousEdges`
- `JapaneseMidnightAndNewlyConfiguredDayScheduleDoNotUseOldIntervals`
- `ClockRollbackForwardJumpReconfigureAndClearReevaluateActualActivity`
- `SameCardKeepsItsNormalBreakAndLunchInActivityRanges`
