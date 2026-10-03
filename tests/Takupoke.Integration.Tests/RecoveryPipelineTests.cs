using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed partial class RecoveryPipelineTests
{
    [Fact]
    public void CanceledVectorRecoveryStopsBeforeLabelOrInkWork()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var inkCalls = 0;
        Assert.Throws<OperationCanceledException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable,
            [Layout(MaterialKind.Timetable)], (_, _) => { inkCalls++; return true; }, token: cancellation.Token));
        Assert.Equal(0, inkCalls);
    }
    [Fact]
    public void DenseVectorGridStopsAtTheComparisonBudget()
    {
        var page = Layout(MaterialKind.Timetable);
        var lines = Enumerable.Range(0, 350).SelectMany(i => new[] { new PdfRule(i * 4, 0, i * 4, 1500), new PdfRule(0, i * 4, 1500, i * 4) }).ToArray();
        page = page with { Height = 1600, Lines = lines };
        var error = Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page], (_, _) => true));
        Assert.Contains("位置比較数", error.Message);
    }
    [Fact]
    public void DuplicateGlyphIdentityCannotBeAssignedTwoSourceIds()
    {
        var page = Layout(MaterialKind.Timetable);
        page = page with { Glyphs = page.Glyphs.Append(page.Glyphs[0]).ToArray() };
        var error = Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page], (_, _) => true));
        Assert.Contains("重複", error.Message);
    }
    [Fact]
    public void RepeatedOcrCoverageCannotSpendUnboundedPixelWork()
    {
        var raster = new RecoveryRaster(512, 512, Enumerable.Repeat((byte)255, 512 * 512 * 4).ToArray());
        var repeatedBoxes = Enumerable.Repeat(new RecoveryBox(0, 0, 512, 512), 300).ToArray();
        var error = Assert.Throws<InvalidDataException>(() => raster.HasUnrecognizedInk(repeatedBoxes, []));
        Assert.Contains("画素処理数", error.Message);
        Assert.False(raster.HasUnrecognizedInk([repeatedBoxes[0]], []));
    }
    [Fact]
    public void EmptyCellChecksShareAPageBudgetDuringRecovery()
    {
        var raster = new RecoveryRaster(512, 512, Enumerable.Repeat((byte)255, 512 * 512 * 4).ToArray());
        var scan = raster.InkFreeScanner(); var box = new RecoveryBox(0, 0, 512, 512);
        var error = Assert.Throws<InvalidDataException>(() => { for (var i = 0; i < 300; i++) scan(box); });
        Assert.Contains("画素処理数", error.Message);
        Assert.True(raster.InkFreeScanner()(box));
        var page = Layout(MaterialKind.Timetable);
        page = page with { Glyphs = page.Glyphs.Where(g => g.Text != "架空教員B").ToArray() };
        var inkCalls = 0;
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page],
            (_, _) => { inkCalls++; return scan(box); }, allowStructureProposal: true));
        Assert.Equal(1, inkCalls);
    }
    [Fact]
    public void CanceledRecoveryCannotContinueRasterAnalysisEvenWithNoCandidateInk()
    {
        var raster = new RecoveryRaster(80, 80, Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => raster.Rules(cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => raster.RuleMask([], cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => raster.HasUnrecognizedInk([], [], cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => raster.InkFree(new(0, 0, 80, 80), token: cancellation.Token));
        Assert.Empty(raster.Rules()); Assert.True(raster.InkFree(new(0, 0, 80, 80)));
    }
    internal static PdfPageLayout Layout(MaterialKind kind)
    {
        var special = kind != MaterialKind.Timetable; var max = kind == MaterialKind.Exam ? 6 : 8;
        var classes = special ? RecoveryValidator.SpecialClasses : new[] { "3_CN" };
        var width = 70 + max * 5 * 100; var bottom = 80 + classes.Count * 60;
        var glyphs = new List<PdfGlyph>(); var lines = new List<PdfRule>();
        void Text(string value, double x, double y) { glyphs.Add(new(value, x, y, Math.Max(2, value.Length * 2), 8)); }
        Text("2026年度", 2, 10); if (!special) Text("前期", 50, 10);
        Text(kind switch { MaterialKind.Exam => "試験時間割", MaterialKind.ExamReturn => "試験返却時間割", _ => "時間割" }, 100, 10);
        lines.Add(new(0, 40, width, 40)); lines.Add(new(0, 60, width, 60)); lines.Add(new(0, 80, width, 80));
        lines.Add(new(0, 40, 0, bottom)); lines.Add(new(70, 40, 70, bottom));
        for (var day = 0; day < 5; day++)
        {
            Text(special ? $"10月{day + 1}日" : new[] { "月", "火", "水", "木", "金" }[day], 70 + day * max * 100 + 10, 45);
            lines.Add(new(70 + (day + 1) * max * 100, 40, 70 + (day + 1) * max * 100, bottom));
            for (var period = 0; period < max; period++)
            { var x = 70 + (day * max + period) * 100; Text((period + 1).ToString(), x + 10, 65); lines.Add(new(x, 60, x, bottom)); }
        }
        foreach (var (cls, index) in classes.Select((c, i) => (c, i)))
        { Text(cls, 5, 80 + index * 60 + 20); lines.Add(new(0, 80 + (index + 1) * 60, width, 80 + (index + 1) * 60)); }
        Text("科目", 74, 85); Text("架空科目A", 88, 85); Text("教員", 74, 105); Text("架空教員B", 88, 105); Text("教室", 74, 125); Text("架空教室C", 88, 125);
        if (special)
        {
            if (kind == MaterialKind.ExamReturn)
            { Text("10月1日の時間割は以下のとおり", 0, bottom + 20); Text("10月2日〜5日は通常の授業日どおりの授業時間", 0, bottom + 35); }
            else Text("試験時間割", 0, bottom + 20);
            var normal = new[] { "08:50〜09:35", "09:35〜10:20", "10:30〜11:15", "11:15〜12:00", "12:50〜13:35", "13:35〜14:20", "14:30〜15:15", "15:15〜16:00" };
            for (var period = 0; period < max; period++) { Text((period + 1).ToString(), 100 + period * 100, bottom + 60); Text(normal[period], 100 + period * 100, bottom + 90); }
        }
        return new(width + 20, bottom + 150, glyphs, lines);
    }
    private sealed class SyntheticProvider(RecoveryMetadata metadata) : ILocalRecoveryProvider
    {
        public string Id => "windowsLanguageModel"; public bool LocalOnly => true; public RecoveryMetadata Metadata => metadata;
        public int Calls, AvailabilityCalls;
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) { AvailabilityCalls++; return Task.FromResult(LocalProviderState.Unsupported); }
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token)
        {
            Calls++; RecoveryField Field(string value) { var source = cell.Sources.Single(s => s.Text == value); return new(RecoveryValueState.Present, value, [source.Id]); }
            return Task.FromResult<IReadOnlyList<RecoveryLesson>>([new(Field("架空科目A"), Field("架空教員B"), Field("架空教室C"), [], [])]);
        }
    }
    [Theory] [InlineData(MaterialKind.Timetable)] [InlineData(MaterialKind.Exam)] [InlineData(MaterialKind.ExamReturn)]
    public async Task PrintedRoleTableUsesRulesBeforeModelsThenFormalAnalysis(MaterialKind kind)
    {
        var layout = Layout(kind); var hash = new string('a', 64);
        var document = RecoveryDocumentBuilder.Build(hash, kind, [layout], (_, b) => !layout.Glyphs.Any(g => b.Contains(new(g.X, g.Y, g.Width, g.Height))));
        Assert.Empty(RecoveryValidator.InputErrors(document));
        var provider = new SyntheticProvider(new("windowsLanguageModel", "synthetic", "1", "1", "2", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test"));
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [provider], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State); Assert.Equal(0, provider.Calls); Assert.Equal(0, provider.AvailabilityCalls);
        var source = new SourceRecord("source", kind, "synthetic.pdf", "synthetic.pdf", "synthetic", hash, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var formal = RecoveryAnalysisConverter.Convert(source, document, run.Result!, DateTimeOffset.UtcNow);
        Assert.Equal("架空科目A", formal.Timetable?.Lessons.Single().Names.Subject ?? formal.Special?.Lessons.Single().Names.Subject);
        if (formal.Special is { } special) Assert.Equal("08:50", special.PeriodTime(new(2026, 10, 2), 1)?.Start);
    }
    [Fact] public void RasterEmptyAndUnknownInkAreIndependentOfOcrOutput()
    {
        var pixels = Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray(); var raster = new RecoveryRaster(80, 80, pixels); var box = new RecoveryBox(0, 0, 80, 80);
        Assert.True(raster.InkFree(box)); pixels[(20 * 80 + 20) * 4] = 0; Assert.False(raster.InkFree(box)); Assert.True(raster.HasUnrecognizedInk([], [])); Assert.False(raster.HasUnrecognizedInk([new(18, 18, 6, 6)], []));
    }
    [Theory] [InlineData(254, 254, 254)] [InlineData(200, 200, 200)] [InlineData(0, 0, 255)]
    public void FaintOrColoredUndetectedInkCannotBecomeAnEmptyCell(byte blue, byte green, byte red)
    {
        var pixels = Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray(); var raster = new RecoveryRaster(80, 80, pixels); var p = (20 * 80 + 20) * 4;
        pixels[p] = blue; pixels[p + 1] = green; pixels[p + 2] = red;
        Assert.False(raster.InkFree(new(0, 0, 80, 80))); Assert.True(raster.HasUnrecognizedInk([], []));
    }

    [Theory] [InlineData(1, 1)] [InlineData(1, 40)] [InlineData(40, 1)] [InlineData(78, 40)] [InlineData(40, 78)]
    public void UndetectedInkBesideARealBorderCannotBecomeEmpty(int x, int y)
    {
        var pixels = Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray();
        for (var i = 0; i < 80; i++) for (var c = 0; c < 3; c++)
        { pixels[i * 4 + c] = 0; pixels[(79 * 80 + i) * 4 + c] = 0; pixels[(i * 80) * 4 + c] = 0; pixels[(i * 80 + 79) * 4 + c] = 0; }
        var raster = new RecoveryRaster(80, 80, pixels); var rules = raster.Rules(); var box = new RecoveryBox(0, 0, 80, 80);
        Assert.Equal(4, rules.Count); Assert.False(raster.HasUnrecognizedInk([], rules)); Assert.True(raster.InkFree(box, raster.RuleMask(rules)));
        for (var c = 0; c < 3; c++) pixels[(y * 80 + x) * 4 + c] = 254;
        Assert.True(raster.HasUnrecognizedInk([], rules)); Assert.False(raster.InkFree(box, raster.RuleMask(rules)));
    }
    [Fact] public void IsolatedLongCharacterStrokeCannotMaskUnrecognizedInkAsARule()
    {
        var pixels = Enumerable.Repeat((byte)255, 100 * 100 * 4).ToArray(); var raster = new RecoveryRaster(100, 100, pixels);
        for (var x = 10; x < 90; x++) for (var c = 0; c < 3; c++) pixels[(50 * 100 + x) * 4 + c] = 0;
        Assert.Empty(raster.Rules()); Assert.True(raster.HasUnrecognizedInk([], raster.Rules()));
        for (var x = 0; x < 100; x++) for (var c = 0; c < 3; c++) { pixels[x * 4 + c] = 0; pixels[(99 * 100 + x) * 4 + c] = 0; }
        for (var y = 0; y < 100; y++) for (var c = 0; c < 3; c++) { pixels[(y * 100) * 4 + c] = 0; pixels[(y * 100 + 99) * 4 + c] = 0; }
        Assert.Equal(4, raster.Rules().Count); Assert.True(raster.HasUnrecognizedInk([], raster.Rules()));
    }

    [Fact] public void DanglingHShapeCannotUseRejectedStrokesToProveItsMiddleRule()
    {
        var pixels = Enumerable.Repeat((byte)255, 120 * 120 * 4).ToArray(); var raster = new RecoveryRaster(120, 120, pixels);
        for (var y = 20; y <= 100; y++) for (var c = 0; c < 3; c++) { pixels[(y * 120 + 20) * 4 + c] = 0; pixels[(y * 120 + 100) * 4 + c] = 0; }
        for (var x = 20; x <= 100; x++) for (var c = 0; c < 3; c++) pixels[(60 * 120 + x) * 4 + c] = 0;
        Assert.Empty(raster.Rules()); Assert.True(raster.HasUnrecognizedInk([], raster.Rules()));
    }

    private sealed class StructureProvider : ILocalRecoveryProvider
    {
        public string Id => "windowsLanguageModel"; public bool LocalOnly => true;
        public RecoveryMetadata Metadata => new(Id, "fictional-structure", "1", "test", "3", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test");
        public int Calls;
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) => Task.FromResult(LocalProviderState.Ready);
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell prompt, CancellationToken token)
        {
            Calls++;
            RecoveryField Field(params string[] names)
            {
                var atoms = names.Select(n => prompt.Sources.Single(s => s.Text == n)).ToArray();
                var top = atoms.Min(a => a.Box!.Y); var bottom = atoms.Max(a => a.Box!.Y + a.Box.Height); var left = atoms.Max(a => a.Box!.X + a.Box.Width);
                return new(RecoveryValueState.Present, "", atoms.Select(a => a.Id).Concat(new[] {
                    prompt.StructureCuts.Last(c => c.Axis == "horizontal" && c.Position <= top).Id,
                    prompt.StructureCuts.First(c => c.Axis == "horizontal" && c.Position >= bottom).Id,
                    prompt.StructureCuts.First(c => c.Axis == "vertical" && c.Position >= left).Id }).ToArray());
            }
            return Task.FromResult<IReadOnlyList<RecoveryLesson>>([new(Field("科目:"), Field("担当教", "員:"), Field("教室:"), [], [])]);
        }
    }
    [Fact] public async Task FoldedActualLayoutNeedsProposalThenUsesOriginalValuesInFormalAnalysis()
    {
        var layout = Layout(MaterialKind.Timetable);
        var body = new[] { new PdfGlyph("科目:", 74, 85, 6, 8), new PdfGlyph("架空科目A", 88, 85, 10, 8),
            new PdfGlyph("担当教", 74, 97, 6, 8), new PdfGlyph("架空教員B", 88, 103, 10, 8), new PdfGlyph("員:", 74, 109, 4, 8),
            new PdfGlyph("教室:", 74, 125, 6, 8), new PdfGlyph("架空教室C", 88, 125, 10, 8) };
        layout = layout with { Glyphs = layout.Glyphs.Where(g => !(g.X >= 70 && g.X < 170 && g.Y >= 80 && g.Y < 140)).Concat(body).ToArray() };
        bool InkFree(int _, RecoveryBox box) => !layout.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)));
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [layout], InkFree));
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [layout], InkFree, allowStructureProposal: true);
        var p = new StructureProvider(); var structure = await RecoveryStructure.ResolveAsync(doc, "windows", 10, [p], default);
        Assert.NotNull(structure.Document); Assert.Equal(1, p.Calls);
        var run = await RecoveryEngine.RunAsync(structure.Document!, "windows", 10, true, [], _ => null); Assert.NotNull(run.Result);
        var source = new SourceRecord("fictional", MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", doc.PdfHash, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var formal = RecoveryAnalysisConverter.Convert(source, structure.Document!, run.Result!, DateTimeOffset.UtcNow);
        Assert.Equal("架空科目A", formal.Timetable!.Lessons.Single().Names.Subject); Assert.Equal("架空教員B", formal.Timetable.Lessons.Single().Names.Teacher);
        Assert.Equal(p.Metadata, run.Result!.Metadata);
    }
    [Fact] public void RoleWordsInsideUnlabelledValuesCannotBeStrippedBySubstringMatching()
    {
        var layout = Layout(MaterialKind.Timetable);
        var body = new[] { ("科目講義", 85d), ("担当甲", 105d), ("教室３", 125d) }
            .SelectMany(row => row.Item1.Select((ch, i) => new PdfGlyph(ch.ToString(), 74 + i * 2, row.Item2, 2, 8))).ToArray();
        layout = layout with { Glyphs = layout.Glyphs.Where(g => !(g.X >= 70 && g.X < 170 && g.Y >= 80 && g.Y < 140)).Concat(body).ToArray() };
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [layout], (_, _) => true));
    }

    [Theory] [InlineData(10, 15, 10.25)] [InlineData(15, 10, 10.25)] [InlineData(30, 15, 10.75)] [InlineData(15, 30, 10.75)]
    public void FractionalBlankRegionChecksAllPixelsWhoseCentersAreInside(int x, int y, double origin)
    {
        var pixels = Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray(); var raster = new RecoveryRaster(80, 80, pixels);
        var box = new RecoveryBox(origin, origin, 20, 20); Assert.True(raster.InkFree(box));
        for (var channel = 0; channel < 3; channel++) pixels[(y * 80 + x) * 4 + channel] = 254;
        Assert.False(raster.InkFree(box));
    }

}
