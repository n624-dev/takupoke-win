using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Materials;
using Takupoke.Win.Platform;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static async Task SeedAsync(string root)
    {
        if (Path.GetFullPath(root) != Path.GetFullPath(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? ""))
            throw new InvalidOperationException("The seed root must match the isolated app root.");
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\n% Entirely synthetic accepted-store UI fixture.\n");
        // A real fictional file keeps the accepted digest current even when
        // native activation checks it. The scenario deletes it after checking
        // the completed status, then verifies retained results after failure.
        var sourcePath = Path.Combine(root, "fake-available-original.pdf");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(sourcePath, MaterialKind.Timetable, null);
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Timetable, sourcePath, content.Identity, "fake-timetable.pdf",
            NotificationDiff.Digest(bytes), bytes.Length, now, now, content.ModifiedAt);
        await store.SaveOriginalAsync(lease, source, bytes);
        var lessons = Enumerable.Range(1, 5).Select(day => new NormalLesson("3_IT", day, 1,
            new("架空科目甲", "架空教員甲", "架空教室甲", "架空科目甲（正式名称）"), "完全に架空の授業", 1)).ToArray();
        await store.SaveAnalysisAsync(lease, new(source.Id, source.Kind, PdfScheduleParser.TimetableVersion, source.Digest, source.OriginalName, now, lease.Period.SchoolYear,
            Timetable: new(lease.Period.SchoolYear, lease.Period.Half == 1 ? "前期" : "後期", lessons)));
        await store.WriteAsync(lease, "acquisition.Timetable", new MaterialAttempt(now, null, false, source.Digest));
        await store.WriteAsync(lease, "attempt.Timetable", new MaterialAttempt(now, null, true, source.Digest, lease.Period.SchoolYear, ParserVersion: PdfScheduleParser.TimetableVersion));
        var link = new LinkItem("fake-study", "fake-category", "架空学習リンク", "https://example.invalid/", "blue", true, 1, true, 1, [], "架空学習リンク|かくうがくしゅうりんく|kakuugakushuurinku");
        await store.WriteAsync(lease, "api.links", new SavedLinks(new("v1", "sha256-" + new string('a', 64), [new("fake-category", "架空カテゴリ", 1, [link, link with { Id = "fake-second", Label = "架空の別リンク", SortOrder = 0, SearchTerms = "別リンク" }])]),
            "\"fake-etag\"", now, new string('A', 43)));
        // Each installer run starts with three old fictional revisions, while
        // personal preferences are deliberately left intact for reinstallation.
        await store.WriteAsync(lease, "api.mapping", new SavedMapping(new string('A', 43), "fake-old", 1, "\"fake-old-mapping\"", new string('a', 64), "2032-04-01T00:00:00Z", now, new([], [], [])));
        await store.WriteAsync(lease, "api.times", new SavedTimes(new string('A', 43), now, new(1, [])));
    }
}
