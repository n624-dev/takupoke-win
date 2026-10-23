using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfVisibilityTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static TimetableAnalysis Parse(byte[] bytes) => PdfScheduleParser.Timetable(PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable));
    [Fact]
    public void RealFontControlUsesItsActualCidMappingAndProducesOneFictionalLesson()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "fictional-noto.json")));
        Assert.Equal(metadata.RootElement.GetProperty("subsetSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Fixtures, "fictional-noto.cff")))));
        var result = Parse(FictionalTimetablePdf.Make(Fixtures));
        var lesson = Assert.Single(result.Lessons); Assert.Equal("架空科目A", lesson.Names.Subject); Assert.Equal("1_CN", lesson.ClassName);
        Assert.Equal(2032, result.SchoolYear);
    }
    [Theory]
    [InlineData("/DeviceGray cs 1 sc")]
    [InlineData("/DeviceRGB cs 1 1 1 scn")]
    [InlineData("/DeviceCMYK cs 0 0 0 0 sc")]
    [InlineData("/DeviceGray CS 1 SC 1 Tr")]
    [InlineData("/DeviceRGB CS 1 1 1 SCN 1 Tr")]
    public void DeviceColourSpaceWhiteInkStillRejectsTheWholeInput(string paint)
        => Assert.Equal("P01", Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, paint))).Stage);
    [Theory]
    [InlineData("/DeviceRGB cs q /DeviceGray cs 1 sc Q 0 0 0 scn")]
    [InlineData("/DeviceRGB cs 1 1 1 scn 0 g 0 sc")]
    [InlineData("/DeviceGray CS 0 SC 1 Tr")]
    [InlineData("/DeviceRGB CS 0 0 0 SCN 1 Tr")]
    [InlineData("/DeviceCMYK cs")]
    public void DeviceColourSpacesTrackRestorationImplicitSelectionAndInitialBlack(string paint)
        => Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons);
    [Fact]
    public void UnverifiedSpacesAndIncorrectComponentCountsAreRejected()
    {
        var paint = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => paint.SelectColourSpace(false, "Pattern"));
        Assert.Throws<PdfParseException>(() => paint.SelectColourSpace(false, "ICCBased"));
        paint.SelectColourSpace(false, "DeviceRGB");
        Assert.Throws<PdfParseException>(() => paint.Operation("sc", [0]));
        Assert.Throws<PdfParseException>(() => paint.Operation("scn", [0, 0, 2]));
        paint.Operation("q", []); paint.SelectColourSpace(false, "DeviceGray"); paint.Operation("Q", []);
        Assert.Throws<PdfParseException>(() => paint.Operation("sc", [0]));
    }
    [Theory]
    [InlineData("/DeviceGray cs 0 sc")]
    [InlineData("/DeviceRGB cs 0 0 0 scn")]
    [InlineData("/DeviceCMYK cs 0 0 0 1 sc")]
    public void StandardDeviceColourSpaceOperatorsProduceTheSameVerifiedBlackLesson(string paint)
        => Assert.Equal("架空科目A", Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons).Names.Subject);
    [Theory]
    [InlineData("0 g")]
    [InlineData("0 0 0 rg")]
    [InlineData("0 0 0 1 k")]
    [InlineData("q 1 g Q")]
    [InlineData("q 1 g q 0 g Q Q")]
    public void VisibleBlackDeviceColoursAndRestoredStatesKeepTheLesson(string paint)
        => Assert.Equal("架空科目A", Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons).Names.Subject);
    [Theory]
    [InlineData("1 g")]
    [InlineData("1 1 1 rg")]
    [InlineData("0 0 0 0 k")]
    public void WhiteTextOnTheWhitePageRejectsTheWholeInput(string paint)
        => Assert.Equal("P01", Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, paint))).Stage);
    [Theory]
    [InlineData("1 g", "0 g", true)]
    [InlineData("0 g", "0 g", false)]
    [InlineData("0.5 g", "0.5 g", false)]
    [InlineData("0 g", "0.5 g", true)]
    [InlineData("1 1 1 rg", "0 0 1 rg", true)]
    [InlineData("0 0 0 0 k", "0 0 0 1 k", true)]
    public void OpaqueColouredBackgroundsAreComparedRatherThanDroppingWhiteLetters(string text, string background, bool visible)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, text, background);
        if (visible) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Theory]
    [InlineData("1 g 0 G 1 Tr", true)]
    [InlineData("0 g 1 G 1 Tr", false)]
    [InlineData("1 g 0 G 2 Tr", true)]
    [InlineData("0 g 1 G 2 Tr", false)]
    [InlineData("0 g 3 Tr", false)]
    public void StrokeAndFillModesRequireVerifiedVisibleInk(string paint, bool visible)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, paint);
        if (visible) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Theory]
    [InlineData("/ca 0.5", false)]
    [InlineData("/CA 0", false)]
    [InlineData("/BM /Multiply", false)]
    [InlineData("/ca 1 /CA 1 /BM /Normal", true)]
    public void UnprovedTransparencyAndBlendModesRejectInsteadOfPretendingOpaque(string state, bool accepted)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, "/GS0 gs 0 g", graphicsState: state);
        if (accepted) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Fact]
    public void LaterPaintAndMixedInvisibleCellsCannotBecomePartialSuccess()
    {
        Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, "0 g", "1 g", backgroundAfterText: true)));
        Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, extraWhiteCell: true)));
    }
    [Fact]
    public void PartialBackgroundsAndOverlappingFillSubpathsRemainUnverified()
    {
        static PdfGlyph Glyph() => new("架空科目A", 10, 10, 20, 10, 0, 0);
        var partial = new PdfPaintVisibility();
        partial.Paths(new([new (double, double)[] { (10, 10), (20, 10), (20, 20), (10, 20), (10, 10) }], true, false, 1, false));
        partial.Glyph(Glyph()); Assert.Throws<PdfParseException>(partial.Validate);
        // The same rectangle traversed twice can cancel under even-odd fill;
        // its bounding box is not proof of an opaque background.
        var retraced = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => retraced.Paths(new([
            new (double, double)[] { (0, 0), (50, 0), (50, 50), (0, 50), (0, 0), (50, 0), (50, 50), (0, 50), (0, 0) }], true, false, 1, false)));
        var holes = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => holes.Paths(new([
            new (double, double)[] { (0, 0), (50, 0), (50, 50), (0, 50), (0, 0) },
            new (double, double)[] { (10, 10), (30, 10), (30, 30), (10, 30), (10, 10) }], true, false, 1, false)));
    }
    [Fact]
    public void ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpaces()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PdfPigLayoutReader.Read(FictionalTimetablePdf.Make(Fixtures), MaterialKind.Timetable, cancellation.Token));
        foreach (var command in new[] { "2 g", "-1 0 0 rg", "/Pattern cs /FictionalPattern scn" })
            Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, command)));
    }
    [Fact]
    public async Task InvisibleReplacementKeepsThePreviousAnalysisAndBothOriginalsAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-pdf-visibility-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            var file = Path.Combine(root, "fictional.pdf"); var black = FictionalTimetablePdf.Make(Fixtures); var white = FictionalTimetablePdf.Make(Fixtures, "1 g");
            var oldId = ""; var currentId = "";
            await using (var store = new SchoolDataStore(root, protector))
            {
                var coordinator = new MaterialCoordinator(store, new(new Identity()));
                await File.WriteAllBytesAsync(file, black); Assert.True((await coordinator.SelectAsync(MaterialKind.Timetable, file, 2032)).Parsed);
                var lease = await store.BeginAsync(); oldId = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.OriginalId;
                await File.WriteAllBytesAsync(file, white); var failed = await coordinator.RefreshAsync(MaterialKind.Timetable, 2032);
                Assert.False(failed.Parsed); Assert.NotNull(failed.Error);
                currentId = (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id; Assert.NotEqual(oldId, currentId);
            }
            await using var reopened = new SchoolDataStore(root, protector); var reopenedLease = await reopened.BeginAsync();
            var analysis = (await reopened.ReadAsync<MaterialAnalysis>(reopenedLease, "analysis.Timetable"))!;
            Assert.Equal(oldId, analysis.OriginalId); Assert.Equal("架空科目A", Assert.Single(analysis.Timetable!.Lessons).Names.Subject);
            Assert.Equal("P01", (await reopened.ReadAsync<MaterialAttempt>(reopenedLease, "attempt.Timetable"))!.Failure);
            Assert.Equal(black, await reopened.ReadOriginalAsync(reopenedLease, oldId)); Assert.Equal(white, await reopened.ReadOriginalAsync(reopenedLease, currentId));
            var source = (await reopened.ReadAsync<SourceRecord>(reopenedLease, "selection.Timetable"))!;
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis, source, MaterialKind.Timetable, 2032));
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis with { ParserVersion = 8 }, source with { Digest = analysis.SourceDigest }, MaterialKind.Timetable, 2032));
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis with { ParserVersion = 9 }, source with { Digest = analysis.SourceDigest }, MaterialKind.Timetable, 2032));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private sealed class Identity : IFileIdentityProvider
    { string IFileIdentityProvider.Identity(FileStream stream) => Path.GetFullPath(stream.Name); }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] bytes) => _cipher.Encrypt(bytes, "fictional-pdf-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-pdf-key");
        public void Dispose() => _cipher.Dispose();
    }
}
