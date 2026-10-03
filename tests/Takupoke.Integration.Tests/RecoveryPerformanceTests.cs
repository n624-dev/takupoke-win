using System.Collections;
using System.Diagnostics;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed partial class RecoveryPipelineTests
{
    internal static RecoveryDocument DenseDocument()
    {
        var layout = Layout(MaterialKind.Timetable);
        var glyphs = layout.Glyphs.Where(g => g.X < 70 || g.Y < 80 || g.Y >= 140).ToList();
        for (var day = 0; day < 5; day++) for (var period = 0; period < 8; period++) for (var role = 0; role < 3; role++)
        {
            var x = 70 + (day * 8 + period) * 100; var y = 85 + role * 20;
            glyphs.Add(new(new[] { "科目", "教員", "教室" }[role], x + 4, y, 4, 8));
            for (var group = 0; group < 24; group++) for (var ch = 0; ch < 27; ch++)
                glyphs.Add(new("A", x + 18 + group * (3 + 27 * .015) + ch * .015, y, .015, .2));
        }
        layout = layout with { Glyphs = glyphs };
        return RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [layout],
            (_, box) => !glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));
    }
    [Fact]
    public async Task DenseActualLayoutRetainsEveryOriginalAtomAndConvertsWithinABoundedTime()
    {
        var doc = DenseDocument(); Assert.Equal(77929, doc.Sources.Count); Assert.Equal(40, doc.Cells.Count);
        var watch = Stopwatch.StartNew();
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        var source = new SourceRecord("fictional", MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional", doc.PdfHash, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var formal = RecoveryAnalysisConverter.Convert(source, doc, run.Result!, DateTimeOffset.UtcNow);
        Assert.Equal(40, formal.Timetable!.Lessons.Count);
        var sources = doc.Sources.ToDictionary(s => s.Id);
        foreach (var cell in doc.Cells)
        {
            var slot = Assert.Single(cell.Slots);
            var lesson = Assert.Single(formal.Timetable.Lessons, l => l.ClassName == slot.ClassName && l.Weekday == int.Parse(slot.Day) && l.Period == slot.Period);
            Assert.Equal(new string('A', 648), lesson.Names.Subject); Assert.Equal(lesson.Names.Subject, lesson.Names.Teacher); Assert.Equal(lesson.Names.Subject, lesson.Names.Room);
            Assert.Equal(string.Join("\n", cell.SourceIds.Select(id => sources[id].Text)), lesson.SourceText);
            Assert.Equal(cell.SourceIds.Count - 3, run.Result!.Cells.Single(c => c.CellId == cell.Id).Lessons.SelectMany(l => l.Subject.Evidence.Concat(l.Teacher.Evidence).Concat(l.Room.Evidence)).Count());
        }
        // Previously the converter alone took 53 seconds on this actual builder output.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "Bounded engine/validation/conversion exceeded 15 seconds.");
        using var cancellation = new CancellationTokenSource();
        var interrupted = doc with { Sources = new CancelingSources(doc.Sources, cancellation) };
        Assert.Throws<OperationCanceledException>(() => RecoveryValidator.Validate(interrupted, run.Result!, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => RecoveryAnalysisConverter.Convert(source, doc, run.Result!, DateTimeOffset.UtcNow, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null, cancellation.Token));
    }
    private sealed class CancelingSources(IReadOnlyList<RecoverySource> sources, CancellationTokenSource cancellation) : IReadOnlyList<RecoverySource>
    {
        public int Count => sources.Count; public RecoverySource this[int i] => sources[i];
        public IEnumerator<RecoverySource> GetEnumerator()
        {
            for (var i = 0; i < sources.Count; i++) { if (i == 1000) cancellation.Cancel(); yield return sources[i]; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
