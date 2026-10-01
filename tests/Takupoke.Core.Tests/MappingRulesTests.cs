using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class MappingRulesTests
{
    private static MappingRules Rules => new(
        [new("科A", "架空正式科目A"), new("科A", "架空専門科目A", ["2_CN"]), new("留 科B", "架空正式科目B", null, true)],
        [new("教A", "架空教員A"), new("教B", "架空教員B")], [new("室A", "架空教室A")],
        [new("教C", "架空教員C", "架空正式科目A", "1_CN", 2032)]);
    [Fact]
    public void ClassSpecificRulesTakePriorityAndMetadataKeepsSeparators()
    {
        var result = Rules.Apply(new("科A", "教A、 教B", "室A"), "2_CN");
        Assert.Equal("架空専門科目A", result.SubjectFullName);
        Assert.Equal("架空教員A、 架空教員B", result.TeacherFullName);
        Assert.Equal("架空教室A", result.RoomFullName);
    }
    [Fact]
    public void ContextualTeacherRequiresMatchingYearClassAndCanonicalSubject()
    {
        Assert.Equal("教C", Rules.SeparateChangeField("科A（教C）", "1_CN", 2032).Teacher);
        Assert.Equal("科A（教C）", Rules.SeparateChangeField("科A（教C）", "1_CN", 2033).Subject);
        Assert.Equal("科A（教C）", Rules.SeparateChangeField("科A（教C）", "2_CN", 2032).Subject);
    }
    [Theory]
    [InlineData("科A（教A）（室A）", "科A", "教A", "室A")]
    [InlineData("科A（未確認）", "科A（未確認）", "", "")]
    public void SeparatesOnlyConfirmedMetadata(string source, string subject, string teacher, string room)
    {
        Assert.Equal(new LessonNames(subject, teacher, room), Rules.SeparateChangeField(source));
    }
    [Fact]
    public void ConflictingExplicitMetadataPreservesOriginalSubject()
    {
        var change = new ScheduleChange("2032-04-05", "1_CN", "1", "科A", "科A（教A）", "教B", "", "", "fake", "fake");
        Assert.Equal("科A（教A）", Rules.Present(change).After.Subject);
        Assert.Equal("架空教員B", Rules.Present(change).After.TeacherFullName);
    }
    [Fact]
    public void AmbiguousNormalizedAliasesAreNotGuessed()
    {
        var rules = new MappingRules([new("Ａ", "架空科目A"), new("A", "架空科目B")], [], []);
        Assert.Null(rules.CanonicalSubject("A", "1_CN"));
        Assert.Equal("架空科目B", rules.Apply(new("A"), "1_CN").SubjectFullName);
    }
}
