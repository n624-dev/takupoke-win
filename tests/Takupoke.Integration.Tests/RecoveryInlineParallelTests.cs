using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryInlineParallelTests
{
    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲・室乙", false)]
    [InlineData("科甲・科乙", "教甲", "室甲・室乙", true)]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲", false)]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲", true)]
    [InlineData("科甲", "教甲・教乙", "室甲・室乙", false)]
    [InlineData("科甲", "教甲・教乙", "室甲・室乙", true)]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲・室乙", false)]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲・室乙", true)]
    [InlineData("科甲･科乙", "教甲", "室甲･室乙", true)]
    public void SingleInlineLabelsCannotCertifyMultipleLessonsOrEscapeToProposal(
        string subject, string teacher, string room, bool allowProposal)
    {
        var page = Layout(subject, teacher, room);
        var error = Assert.ThrowsAny<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64),
            MaterialKind.Timetable, [page], (_, b) => InkFree(page, b), allowStructureProposal: allowProposal));
        Assert.Equal("並記された各授業の独立した原文ラベルを確認できません。", error.Message);
    }

    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲")]
    [InlineData("科甲", "教甲・教乙", "室甲")]
    [InlineData("科甲", "教甲", "室甲・室乙")]
    [InlineData("科甲･科乙", "教甲", "室甲")]
    public async Task SingleCompoundFieldStillRetainsExactOriginalEvidence(string subject, string teacher, string room)
    {
        var page = Layout(subject, teacher, room);
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page], (_, b) => InkFree(page, b));
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.True(RecoveryValidator.Validate(doc, run.Result!).CanAdopt);
        var actual = Assert.Single(run.Result!.Cells.SelectMany(c => c.Lessons));
        Assert.Equal(subject, actual.Subject.Value); Assert.Equal(teacher, actual.Teacher.Value); Assert.Equal(room, actual.Room.Value);
        var originals = doc.Sources.ToDictionary(s => s.Id);
        Assert.Equal(subject, string.Concat(actual.Subject.Evidence.Select(id => originals[id].Text)));
        Assert.Equal(teacher, string.Concat(actual.Teacher.Evidence.Select(id => originals[id].Text)));
        Assert.Equal(room, string.Concat(actual.Room.Evidence.Select(id => originals[id].Text)));
    }

    private static PdfPageLayout Layout(string subject, string teacher, string room)
    {
        var original = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        return original with { Glyphs = original.Glyphs.Select(g => g with {
            Text = g.Text switch { "架空科目A" => subject, "架空教員B" => teacher, "架空教室C" => room, _ => g.Text }
        }).ToArray() };
    }
    private static bool InkFree(PdfPageLayout page, RecoveryBox box) =>
        !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)));

    [Fact]
    public async Task IndependentParallelColumnsRetainSeparateLessonsAndProvedEmptyRoom()
    {
        var original = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        var glyphs = original.Glyphs.Where(g => g.Y < 80 || g.X < 70 || g.Y >= 140).ToList();
        for (var role = 0; role < 3; role++)
        {
            var y = 85 + role * 20; var label = new[] { "科目", "教員", "教室" }[role];
            glyphs.Add(new(label, 74, y, 4, 8));
            glyphs.Add(new(new[] { "科甲", "教甲", "室甲" }[role], 88, y, 6, 8));
            glyphs.Add(new(label, 120, y, 4, 8));
            if (role != 2) glyphs.Add(new(new[] { "科乙", "教乙" }[role], 135, y, 6, 8));
        }
        var page = original with { Glyphs = glyphs };
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page], (_, b) => InkFree(page, b));
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.True(RecoveryValidator.Validate(doc, run.Result!).CanAdopt);
        var lessons = run.Result!.Cells.SelectMany(c => c.Lessons).ToArray();
        Assert.Equal(2, lessons.Length);
        Assert.Equal("科甲", lessons[0].Subject.Value); Assert.Equal("教甲", lessons[0].Teacher.Value); Assert.Equal("室甲", lessons[0].Room.Value);
        Assert.Equal("科乙", lessons[1].Subject.Value); Assert.Equal("教乙", lessons[1].Teacher.Value);
        Assert.Equal(RecoveryValueState.Empty, lessons[1].Room.State);
        Assert.Empty(lessons[1].Room.Evidence);
    }
}
