using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class SchedulePresentationTests
{
    private static readonly DateOnly Day = new(2032, 4, 5);
    private static readonly NormalLesson Normal = new("1_CN", 1, 1, new("架空科目A", "架空教員A", "架空教室A"), "架空原文", 1);
    private static ScheduleChange Change(string before = "", string after = "架空科目B") => new(Day.Iso(), "1_CN", "1", before, after, "", "", "補講", "架空原文B", "架空正規化B");
    private static readonly MappingRules Mapping = new([new("架空科目A", "架空正式科目A"), new("架空科目B", "架空正式科目B")], [], []);
    [Fact]
    public void HomeAlwaysIncludesChangesWhenWeekViewIsSetToNormal()
    {
        var presentation = new SchedulePresentation(new(new(2032, "前期", [Normal]), [Change()]), Mapping);
        var preferences = new UserPreferences { IncludesChanges = false };
        Assert.IsType<NormalContent>(Assert.Single(presentation.Engine(preferences).Blocks(Day, "1_CN")).Content);
        Assert.IsType<ChangeContent>(Assert.Single(presentation.Engine(preferences, home: true).Blocks(Day, "1_CN")).Content);
    }
    [Fact]
    public void EmptyBeforeSubjectUsesOnlyNormalSourceNamesAndNeverSpecialsOrFullNames()
    {
        var presentation = new SchedulePresentation(new(new(2032, "前期", [Normal])), Mapping);
        Assert.Equal("架空科目A", presentation.BeforeSubject(Change(), detail: true));
        var special = new SpecialAnalysis(MaterialKind.Exam, 2032, [Day.Iso()], ["1_CN"], new Dictionary<int, TimeRange>(),
            [new(Day.Iso(), "1_CN", 1, 1, 1, null, ["架空科目B"], 1)]);
        var duringExam = new SchedulePresentation(new(new(2032, "前期", [Normal]), Specials: [special]), Mapping);
        Assert.Equal("記載なし", duringExam.BeforeSubject(Change()));
        Assert.Single(duringExam.SpecialOriginals(Change()));
        Assert.Empty(duringExam.NormalOriginals(Change()));
    }
    [Fact]
    public void SpecialNamesUseMappingOnlyOnHomeWhileWeekAndDetailRetainPdfSpelling()
    {
        var presentation = new SchedulePresentation(new(), Mapping);
        var block = new ScheduleBlock(1, 1, new SpecialContent(MaterialKind.Exam, new(Day.Iso(), "1_CN", 1, 1, 1, null, ["架空科目A"], 1), null));
        Assert.Equal("架空正式科目A", presentation.Names("1_CN", block, home: true).DetailSubject);
        Assert.Equal("架空科目A", presentation.Names("1_CN", block).DetailSubject);
    }
    [Fact]
    public void InternationalChangesAreFilteredFromListAsWellAsGrid()
    {
        var mapping = new MappingRules([new("留 架空科目B", "架空正式科目B", InternationalStudent: true)], [], []);
        var data = new ScheduleData(Changes: [Change(after: "架空科目B")]);
        var presentation = new SchedulePresentation(data, mapping);
        var selected = new HashSet<string> { "1_CN" };
        Assert.Empty(presentation.Engine(new()).Changes(selected, ChangeRange.All, Day, Day.Monday()));
        Assert.Single(presentation.Engine(new() { International = true }).Changes(selected, ChangeRange.All, Day, Day.Monday()));
    }
    [Theory]
    [InlineData("2032-04-10", "2032-04-05", "2032-04-12")]
    [InlineData("2032-04-11", "2032-04-05", "2032-04-12")]
    public void HomeWeekContainsTodayOnWeekendWhileInitialWeekCanAdvance(string value, string current, string initial)
    {
        var day = DateOnly.Parse(value);
        Assert.Equal(current, day.Monday().Iso()); Assert.Equal(initial, day.DisplayWeekStart().Iso());
    }
    [Fact]
    public void GridTransformsNeverChangeHomeTextOrPersistedSource()
    {
        const string source = "架空ｶﾅ・科目\nA";
        Assert.Equal("架空カナ・科目A", DisplayText.Continuous(source));
        Assert.Equal("架空カナ•科目A", DisplayText.CellSubject(source));
        Assert.Equal("架空ｶﾞﾊﾟA", DisplayText.HalfWidthKana("架空ガパA"));
        Assert.Equal("教員A", DisplayText.Metadata("（教員A）"));
    }
    [Fact]
    public void ChangeCardKeepsLastUnambiguousShortSpellingWhenNoneFits()
    {
        Assert.Equal("架空ｶﾅ", DisplayText.ChangeCardSubject("架空科目正式名A", "架空カナ", _ => false));
        Assert.Equal("架空科目正式名A", DisplayText.ChangeCardSubject("架空科目正式名A", null, _ => false));
    }
}
