using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class PdfClassAssignmentTests
{
    [Theory]
    [InlineData("1_1", "架空CN演習", "1_CN")]
    [InlineData("1_2", "(ES) 実習", "1_ES")]
    [InlineData("1_3", "IT/IT", "1_IT")]
    [InlineData("1_1", "CN/ES", "1_1")]
    [InlineData("1_1", "UNIT", "1_1")]
    [InlineData("1_1", "SCN", "1_1")]
    [InlineData("2_CN", "ES", "2_CN")]
    public void OnlyOneStandaloneDepartmentChangesFirstYearAffiliation(string original, string subject, string expected)
    {
        var lesson = new NormalLesson(original, 1, 1, new(subject, "架空教員", "架空教室"), "元のセル", 1);
        var assigned = PdfClassAssignment.Assign(lesson);
        Assert.Equal(expected, assigned.ClassName);
        Assert.Equal(expected == original ? null : original, assigned.SourceClassName);
        Assert.Equal(lesson.Names, assigned.Names); Assert.Equal(lesson.SourceText, assigned.SourceText);
        Assert.Equal(assigned, PdfClassAssignment.Assign(assigned));
    }

    [Fact]
    public void EmptyFirstYearSpecialTableSuppressesDepartmentOrdinaryLessonWithoutInventingAnExam()
    {
        var day = new DateOnly(2026, 4, 6);
        var timetable = new TimetableAnalysis(2026, "前期", [new("1_CN", 1, 1, new("架空通常科目"), "", 1)]);
        var original = new SpecialAnalysis(MaterialKind.Exam, 2026, [day.Iso()], ["1_1"], new Dictionary<int,TimeRange>(), []);
        var special = PdfClassAssignment.Assign(original);
        Assert.True(special.Applies(day, "1_CN")); Assert.False(special.Applies(day, "2_CN"));
        Assert.False(special.Applies(day.AddDays(1), "1_CN"));
        var slot = new TimetableEngine(new(timetable, Specials:[special])).Slot(day, 1, "1_CN");
        Assert.Empty(slot.BaseLessons); Assert.Empty(slot.SpecialLessons);
        Assert.Single(new TimetableEngine(new(timetable, Specials:[original])).Slot(day, 1, "1_CN").BaseLessons);
    }
}
