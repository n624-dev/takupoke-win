using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryOverlappingRoleLabelTests
{
    [Theory]
    [InlineData(MaterialKind.Timetable, ":")]
    [InlineData(MaterialKind.Timetable, "：")]
    [InlineData(MaterialKind.Exam, ":")]
    [InlineData(MaterialKind.ExamReturn, "：")]
    public void TheSamePrintedPrefixAndItsColonExtensionAreOneRoleLabel(MaterialKind kind, string colon)
    {
        var page = Layout(kind, colon);
        var snapshot = page.Glyphs.ToArray();
        var document = Build(page, kind);
        var cell = Assert.Single(document.Cells, c => !c.ConfirmedEmpty);
        Assert.Equal(3, cell.RoleScopes.Count);
        Assert.All(cell.RoleScopes, s => Assert.Equal(0, s.LessonIndex));
        var sources = document.Sources.ToDictionary(s => s.Id);
        foreach (var role in cell.RoleScopes)
        {
            Assert.Equal(3, role.LabelSourceIds.Count);
            Assert.EndsWith(colon, string.Concat(role.LabelSourceIds.Select(id => sources[id].Text)));
            var values = document.Sources.Where(s => cell.SourceIds.Contains(s.Id) && role.Box.Contains(s.Box))
                .Select(s => s.Text).ToArray();
            Assert.Equal(role.Role switch { RecoveryFieldRole.Subject => "科甲", RecoveryFieldRole.Teacher => "教甲", _ => "室甲" }, string.Concat(values));
        }
        Assert.Equal(snapshot, page.Glyphs);
    }

    [Fact]
    public void AnIndependentRepeatedSubjectLabelCannotBeDeduplicatedAsAnExtension()
    {
        var page = Layout(MaterialKind.Timetable, "：", repeatSubject: true);
        Assert.Throws<InvalidDataException>(() => Build(page, MaterialKind.Timetable));
    }

    [Fact]
    public void APlusIsPreservedAsBodyValueInsteadOfBeingTreatedAsAColon()
    {
        var page = Layout(MaterialKind.Timetable, "+");
        var doc = Build(page, MaterialKind.Timetable);
        var cell = Assert.Single(doc.Cells, c => !c.ConfirmedEmpty);
        var role = Assert.Single(cell.RoleScopes, s => s.Role == RecoveryFieldRole.Subject);
        Assert.Equal(2, role.LabelSourceIds.Count);
        Assert.Equal("+科甲", string.Concat(doc.Sources.Where(s => cell.SourceIds.Contains(s.Id) && role.Box.Contains(s.Box)).Select(s => s.Text)));
    }

    [Fact]
    public void ARoleWordInsideOneExplicitNativeFieldRemainsLiteralValue()
    {
        var page = WithTeacherValue("教員", ":", false);
        var doc = Build(page, MaterialKind.Timetable);
        var cell = Assert.Single(doc.Cells, c => !c.ConfirmedEmpty);
        var scope = Assert.Single(cell.RoleScopes, s => s.Role == RecoveryFieldRole.Teacher);
        Assert.Equal("仮想教員甲", string.Concat(doc.Sources.Where(s => cell.SourceIds.Contains(s.Id) && scope.Box.Contains(s.Box)).Select(s => s.Text)));
        Assert.Equal(3, scope.LabelSourceIds.Count);
        Assert.Equal(page.Glyphs.Count, doc.Sources.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherExplicitPrefixOrIndependentNativeLineCannotBecomeLiteralContinuation(bool independentLine)
    {
        var page = WithTeacherValue("教員", ":", !independentLine);
        if (independentLine)
            page = page with { Glyphs = page.Glyphs.Select(g => g.Text == "教員" && g.X == 123
                ? g with { SourceLine = 200 } : g).ToArray() };
        Assert.Throws<InvalidDataException>(() => Build(page, MaterialKind.Timetable));
    }

    [Fact]
    public void MixedExplicitAndBareParallelColumnsRetainBothLessons()
    {
        var page = WithParallelBareColumn(3);
        var document = Build(page, MaterialKind.Timetable);
        var cell = Assert.Single(document.Cells, c => !c.ConfirmedEmpty);
        Assert.Equal(6, cell.RoleScopes.Count);
        var sources = document.Sources.ToDictionary(s => s.Id);
        foreach (var scope in cell.RoleScopes)
        {
            var expected = scope.Role switch { RecoveryFieldRole.Subject => "科", RecoveryFieldRole.Teacher => "教", _ => "室" };
            expected += scope.LessonIndex == 0 ? "甲" : "乙";
            Assert.Equal(expected, string.Concat(document.Sources.Where(s => cell.SourceIds.Contains(s.Id) && scope.Box.Contains(s.Box)).Select(s => s.Text)));
            Assert.Equal(scope.LessonIndex == 0 ? 3 : 1, scope.LabelSourceIds.Count);
        }
        Assert.Equal(page.Glyphs.Count, sources.Count);
    }

    [Fact]
    public void IncompleteMixedParallelColumnsRemainAmbiguous()
    {
        Assert.Throws<InvalidDataException>(() => Build(WithParallelBareColumn(2), MaterialKind.Timetable));
    }

    private static PdfPageLayout WithParallelBareColumn(int rows)
    {
        var page = Layout(MaterialKind.Timetable, ":");
        var glyphs = page.Glyphs.ToList();
        for (var role = 0; role < rows; role++)
        {
            glyphs.Add(new(new[] { "科目", "教員", "教室" }[role], 123, 85 + role * 20, 6, 8, 100 + role, 0));
            glyphs.Add(new(new[] { "科乙", "教乙", "室乙" }[role], 142, 85 + role * 20, 6, 8, 100 + role, 0));
        }
        var ordered = glyphs.OrderBy(g => g.SourceLine ?? -1).ThenBy(g => g.X).ToArray();
        return page with { Glyphs = ordered.Select((g, i) => g.SourceLine is null ? g : g with { SourceOrder = i }).ToArray() };
    }

    private static PdfPageLayout WithTeacherValue(string word, string separator, bool explicitPrefix)
    {
        var page = Layout(MaterialKind.Timetable, separator);
        var glyphs = page.Glyphs.Where(g => !(g.Text == "教甲" && g.SourceLine == 101)).ToList();
        var order = glyphs.Single(g => g.SourceLine == 101 && g.Text == separator).SourceOrder!.Value + 1;
        glyphs.Add(new("仮想", 102, 105, 6, 8, 101, order));
        glyphs.Add(new(word, 123, 105, 6, 8, 101, order + 1));
        if (explicitPrefix) glyphs.Add(new(separator, 133, 105, 4, 8, 101, order + 2));
        glyphs.Add(new("甲", 142, 105, 3, 8, 101, order + (explicitPrefix ? 3 : 2)));
        // Preserve a unique complete native order after changing this line.
        var teacher = glyphs.Where(g => g.SourceLine == 101).OrderBy(g => g.SourceOrder).ToArray();
        var other = glyphs.Where(g => g.SourceLine != 101).ToArray();
        var all = other.Concat(teacher).ToArray();
        return page with { Glyphs = all.Select((g, i) => g.SourceLine is null ? g : g with { SourceOrder = i }).ToArray() };
    }

    private static RecoveryDocument Build(PdfPageLayout page, MaterialKind kind) =>
        RecoveryDocumentBuilder.Build(new string('a', 64), kind, [page],
            (_, b) => !page.Glyphs.Any(g => b.Contains(new(g.X, g.Y, g.Width, g.Height))), new HashSet<int> { 1 });

    private static PdfPageLayout Layout(MaterialKind kind, string separator, bool repeatSubject = false)
    {
        var original = RecoveryPipelineTests.Layout(kind);
        var glyphs = original.Glyphs.Where(g => g.Y < 80 || g.X < 70 || g.Y >= 140).ToList();
        var order = glyphs.Count;
        for (var role = 0; role < 3; role++)
        {
            var word = new[] { "科目", "教員", "教室" }[role]; var y = 85 + role * 20;
            glyphs.Add(new(word[0].ToString(), 74, y, 4, 8, 100 + role, order++));
            glyphs.Add(new(word[1].ToString(), 79, y, 4, 8, 100 + role, order++));
            glyphs.Add(new(separator, 92, y, 4, 8, 100 + role, order++));
            glyphs.Add(new(new[] { "科甲", "教甲", "室甲" }[role], 102, y, 6, 8, 100 + role, order++));
        }
        if (repeatSubject)
        {
            glyphs.Add(new("科目：", 120, 85, 15, 8, 200, order++));
            glyphs.Add(new("科乙", 142, 85, 6, 8, 200, order++));
        }
        return original with { Glyphs = glyphs };
    }
}
