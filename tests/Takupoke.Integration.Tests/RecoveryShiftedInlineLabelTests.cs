using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryShiftedInlineLabelTests
{
    [Theory]
    [InlineData(MaterialKind.Timetable, false)]
    [InlineData(MaterialKind.Timetable, true)]
    [InlineData(MaterialKind.Exam, false)]
    [InlineData(MaterialKind.ExamReturn, false)]
    public async Task UniqueExplicitRolesOnSeparateRowsDoNotNeedIdenticalHorizontalLabelStarts(MaterialKind kind, bool emptyTeacher)
    {
        var page = Layout(kind, emptyTeacher: emptyTeacher);
        var snapshot = page.Glyphs.ToArray();
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), kind, [page], (_, box) => InkFree(page, box));
        var cell = Assert.Single(doc.Cells, c => !c.ConfirmedEmpty);
        Assert.Equal(3, cell.RoleScopes.Count);
        Assert.All(cell.RoleScopes, s => Assert.Equal(0, s.LessonIndex));
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.True(RecoveryValidator.Validate(doc, run.Result!).CanAdopt);
        var lesson = Assert.Single(run.Result!.Cells.SelectMany(c => c.Lessons));
        Assert.Equal("科甲", lesson.Subject.Value);
        Assert.Equal(emptyTeacher ? "" : "教甲", lesson.Teacher.Value);
        Assert.Equal("室甲", lesson.Room.Value);
        var sources = doc.Sources.ToDictionary(s => s.Id);
        Assert.Equal("科甲", string.Concat(lesson.Subject.Evidence.Select(id => sources[id].Text)));
        Assert.Equal("室甲", string.Concat(lesson.Room.Evidence.Select(id => sources[id].Text)));
        if (emptyTeacher) Assert.Equal(RecoveryValueState.Empty, lesson.Teacher.State);
        Assert.Equal(snapshot, page.Glyphs);
    }

    [Fact]
    public void ShiftedParallelColumnsStillNeedIndependentCorrespondingAnchors()
    {
        var page = Layout(MaterialKind.Timetable, parallel: true);
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64),
            MaterialKind.Timetable, [page], (_, box) => InkFree(page, box)));
    }

    [Fact]
    public void SingleRoleLabelsOnTheSameRowCannotUseTheSeparateRowProof()
    {
        var page = Layout(MaterialKind.Timetable, overlapRows: true);
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64),
            MaterialKind.Timetable, [page], (_, box) => InkFree(page, box)));
    }

    private static PdfPageLayout Layout(MaterialKind kind, bool emptyTeacher = false, bool parallel = false, bool overlapRows = false)
    {
        var original = RecoveryPipelineTests.Layout(kind);
        var glyphs = original.Glyphs.Where(g => g.Y < 80 || g.X < 70 || g.Y >= 140).ToList();
        for (var role = 0; role < 3; role++)
        {
            var y = overlapRows && role == 1 ? 85 : 85 + role * 20;
            var label = new[] { "科目", "教員", "教室" }[role];
            var value = new[] { "科甲", "教甲", "室甲" }[role];
            glyphs.Add(new(label, 74 + role * 2.5, y, 4, 8));
            if (!emptyTeacher || role != 1) glyphs.Add(new(value, 91 + role * 2.5, y, 6, 8));
            if (parallel)
            {
                glyphs.Add(new(label, 120 + role * 2.5, y, 4, 8));
                glyphs.Add(new(new[] { "科乙", "教乙", "室乙" }[role], 135 + role * 2.5, y, 6, 8));
            }
        }
        return original with { Glyphs = glyphs };
    }

    private static bool InkFree(PdfPageLayout page, RecoveryBox box) =>
        !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)));
}
