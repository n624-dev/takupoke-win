using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed class RecoveryPipelineTests
{
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

}
