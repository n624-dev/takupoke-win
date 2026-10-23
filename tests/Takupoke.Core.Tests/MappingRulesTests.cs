using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class MappingRulesTests
{
    [Fact]
    public void ExactSubjectSpellingTakesPriorityWithinThePreferredClass()
    {
        var rules = new MappingRules([
            new("架空科目A", "架空正式科目A", ["3_IT"]), new("架空科目Ａ", "架空別科目A", ["3_IT"]),
            new("架空科目A", "架空共通科目A")], [], [], [new("架空姓", "架空正式教員A", "架空正式科目A", "3_IT", 2032)]);
        Assert.Equal("架空正式科目A", rules.CanonicalSubject("架空科目A", "3_IT"));
        Assert.Equal("架空別科目A", rules.CanonicalSubject("架空科目Ａ", "3_IT"));
        Assert.Null(rules.CanonicalSubject("架空科目𝔸", "3_IT"));
        Assert.Equal("架空共通科目A", rules.CanonicalSubject("架空科目A", "2_IT"));
        Assert.Equal("架空姓", rules.SeparateChangeField("架空科目A（架空姓）", "3_IT", 2032).Teacher);
        Assert.Equal("", rules.SeparateChangeField("架空科目A（架空姓）", "3_IT", 2033).Teacher);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitOrdinarySubjectIsNotHiddenByOmittedInternationalPrefix(bool specific)
    {
        var rules = new MappingRules([new("架空科目A", "架空一般科目A", specific ? ["2_IT"] : null),
            new("留 架空科目A", "架空留学生科目A", ["2_IT"], true)], [], []);
        Assert.Equal(!specific, rules.IsInternational("架空科目A", "2_IT"));
        Assert.True(rules.IsInternational("留 架空科目A", "2_IT"));
        Assert.False(rules.IsInternational("架空科目A", "3_IT"));
    }
    [Fact]
    public void ConditionalTeacherUsesExactThenUniqueWidthMatchInItsOwnContext()
    {
        var subject = new MappingRule("留 架空正式科目A", "架空正式科目A", ["2_IT"], true);
        var context = new TeacherContextRule("架空姓Ａ", "架空正式教員A", "架空正式科目A", "2_IT", 2032);
        var rules = new MappingRules([subject], [], [], [context]);
        Assert.Equal("架空姓A", rules.SeparateChangeField("架空正式科目A(架空姓A)", "2_IT", 2032).Teacher);
        Assert.True(rules.IsInternationalChangeField("架空正式科目A(架空姓A)", "2_IT", 2032));
        Assert.False(rules.IsInternationalChangeField("架空正式科目A(架空姓A)", "2_IT", 2033));
        var ambiguous = new MappingRules([subject], [], [], [context, context with { Alias = "架空姓A", FullName = "架空別教員B" }]);
        var change = new ScheduleChange("2032-04-05", "2_IT", "1", "", "架空正式科目A(架空姓A)", "", "", "", "架空原文", "架空正規化");
        Assert.Equal("架空別教員B", ambiguous.Present(change).After.DetailTeacher);
        Assert.Equal("", ambiguous.SeparateChangeField("架空正式科目A(架空姓𝔸)", "2_IT", 2032).Teacher);
    }
    [Fact]
    public void OmittedInternationalShortSubjectStillResolvesItsConditionalTeacher()
    {
        var rules = new MappingRules([new("留 架空略科A", "架空正式科目A", ["2_IT"], true)], [], [],
            [new("架空姓", "架空正式教員A", "架空正式科目A", "2_IT", 2032)]);
        Assert.Equal("架空正式科目A", rules.CanonicalSubject("架空略科A", "2_IT"));
        Assert.Equal("架空姓", rules.SeparateChangeField("架空略科A（架空姓）", "2_IT", 2032).Teacher);
        Assert.True(rules.IsInternationalChangeField("架空略科A（架空姓）", "2_IT", 2032));
        Assert.False(rules.IsInternationalChangeField("架空略科A（架空姓）", "3_IT", 2032));
    }

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
        Assert.Equal("架空科目B", rules.CanonicalSubject("A", "1_CN"));
        Assert.Equal("架空科目A", rules.CanonicalSubject("Ａ", "1_CN"));
        Assert.Null(rules.CanonicalSubject("𝔸", "1_CN"));
        Assert.Equal("架空科目B", rules.Apply(new("A"), "1_CN").SubjectFullName);
    }

    [Fact]
    public void ScopedInternationalRuleWinsOverGenericAliasRegardlessOfOrder()
    {
        MappingRule generic = new("架空科目A", "架空正式科目A");
        MappingRule scoped = new("留 架空科目A", "架空正式科目A", ["1_CN"], true);
        foreach (var rules in new[] { new MappingRules([generic, scoped], [], []), new MappingRules([scoped, generic], [], []) })
        {
            Assert.True(rules.IsInternational("架空科目A", "1_CN"));
            Assert.False(rules.IsInternational("架空科目A", "2_CN"));
            Assert.Equal("架空正式科目A", rules.Apply(new("架空科目A"), "1_CN").SubjectFullName);
        }
    }
    [Theory]
    [InlineData("科A（教C）（室A）")]
    [InlineData("科A（室A）（教C）")]
    [InlineData("科A(室A)(教C)")]
    public void ContextualTeacherAndConfirmedRoomSeparateInEitherOrder(string source)
    {
        Assert.Equal(new LessonNames("科A", "教C", "室A"), Rules.SeparateChangeField(source, "1_CN", 2032));
        Assert.Contains("教C", Rules.SeparateChangeField(source, "1_CN", 2033).Subject);
        Assert.Contains("教C", Rules.SeparateChangeField(source, "2_CN", 2032).Subject);
        Assert.Contains("未確認", Rules.SeparateChangeField("科A（未確認）（教C）", "1_CN", 2032).Subject);
    }

}
