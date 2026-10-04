using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

// Invented layouts exercise the real Builder, Rules, Validator and formal converter.
// PDF Reader/OCR and representative real school quality are separate unverified tiers.
internal static class ProductionRuleBench
{
    internal static async Task<IReadOnlyList<object>> RunAsync()
    {
        var observations = new List<object>();
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var layout = Layout(kind); var provider = new NoModelProvider();
            bool InkFree(int _, RecoveryBox box) => !layout.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)));
            var doc = RecoveryDocumentBuilder.Build(new string('a', 64), kind, [layout], InkFree, allowStructureProposal: true);
            var prepared = await RecoveryStructure.ResolveAsync(doc, "windows", 10, [provider], default);
            if (prepared.Document is null) throw new InvalidDataException("Rule layout preparation failed.");
            var run = await RecoveryEngine.RunAsync(prepared.Document, "windows", 10, true, [provider], _ => null);
            if (run.Result is null || !RecoveryValidator.Validate(prepared.Document, run.Result).CanAdopt) throw new InvalidDataException("Rule layout validation failed.");
            var source = new SourceRecord("fictional", kind, "fictional.pdf", "fictional", "fictional.pdf", doc.PdfHash, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
            var formal = RecoveryAnalysisConverter.Convert(source, prepared.Document, run.Result, DateTimeOffset.UnixEpoch);
            var names = formal.Timetable?.Lessons.Single().Names ?? formal.Special!.Lessons.Single().Names;
            var exact = names.Subject == "架空科目A" && names.Teacher == "架空教員B" && names.Room == "架空教室C";
            var expectedSlots = kind == MaterialKind.Timetable ? 40 : kind == MaterialKind.Exam ? 510 : 680;
            if (!exact || doc.RequiredSlots.Count != expectedSlots || provider.Calls != 0 || provider.AvailabilityCalls != 0 || run.Result.Metadata.Provider != "rule") throw new InvalidDataException("Rule control oracle mismatch.");
            if (formal.Special is { } special && special.PeriodTime(new(2026, 10, 2), 1)?.Start != "08:50") throw new InvalidDataException("Special clock preservation failed.");
            observations.Add(new { kind = kind.ToString(), sourceHash = RecoveryValidator.Fingerprint(doc), requiredSlots = doc.RequiredSlots.Count, classes = doc.Classes.Count, days = doc.Days.Count,
                exactFormalValues = exact, providerAvailabilityCalls = provider.AvailabilityCalls, providerCalls = provider.Calls, metadata = run.Result.Metadata, scope = "Invented PdfPageLayout→productionBuilder/Rules/Engine/Validator/formalConverter; Reader/OCR not exercised" });
        }
        return observations;
    }
    private sealed class NoModelProvider : ILocalRecoveryProvider
    {
        public int Calls, AvailabilityCalls; public string Id => "foundryLocal"; public bool LocalOnly => true;
        public RecoveryMetadata Metadata => new(Id, "must-not-load", "1", "test", "3", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test");
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) { AvailabilityCalls++; throw new IOException("Rule control attempted model preparation."); }
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token) { Calls++; throw new InvalidRecoveryOutputException(); }
    }
    private static PdfPageLayout Layout(MaterialKind kind)
    {
        var special = kind != MaterialKind.Timetable; var max = kind == MaterialKind.Exam ? 6 : 8;
        var classes = special ? RecoveryValidator.SpecialClasses : new[] { "3_CN" }; var width = 70 + max * 5 * 100; var bottom = 80 + classes.Count * 60;
        var glyphs = new List<PdfGlyph>(); var lines = new List<PdfRule>();
        void Text(string value, double x, double y) => glyphs.Add(new(value, x, y, Math.Max(2, value.Length * 2), 8));
        Text("2026年度", 2, 10); if (!special) Text("前期", 50, 10);
        Text(kind switch { MaterialKind.Exam => "試験時間割", MaterialKind.ExamReturn => "試験返却時間割", _ => "時間割" }, 100, 10);
        lines.Add(new(0, 40, width, 40)); lines.Add(new(0, 60, width, 60)); lines.Add(new(0, 80, width, 80)); lines.Add(new(0, 40, 0, bottom)); lines.Add(new(70, 40, 70, bottom));
        for (var day = 0; day < 5; day++)
        {
            Text(special ? $"10月{day + 1}日" : new[] { "月", "火", "水", "木", "金" }[day], 70 + day * max * 100 + 10, 45);
            lines.Add(new(70 + (day + 1) * max * 100, 40, 70 + (day + 1) * max * 100, bottom));
            for (var period = 0; period < max; period++) { var x = 70 + (day * max + period) * 100; Text((period + 1).ToString(), x + 10, 65); lines.Add(new(x, 60, x, bottom)); }
        }
        foreach (var (cls, index) in classes.Select((c, i) => (c, i))) { Text(cls, 5, 80 + index * 60 + 20); lines.Add(new(0, 80 + (index + 1) * 60, width, 80 + (index + 1) * 60)); }
        glyphs.AddRange([new("科目:", 74, 85, 6, 8), new("架空科目A", 88, 85, 10, 8), new("担当教", 74, 97, 6, 8), new("架空教員B", 88, 103, 10, 8), new("員:", 74, 109, 4, 8), new("教室:", 74, 125, 6, 8), new("架空教室C", 88, 125, 10, 8)]);
        if (special)
        {
            if (kind == MaterialKind.ExamReturn) { Text("10月1日の時間割は以下のとおり", 0, bottom + 20); Text("10月2日〜5日は通常の授業日どおりの授業時間", 0, bottom + 35); }
            else Text("試験時間割", 0, bottom + 20);
            var normal = new[] { "08:50〜09:35", "09:35〜10:20", "10:30〜11:15", "11:15〜12:00", "12:50〜13:35", "13:35〜14:20", "14:30〜15:15", "15:15〜16:00" };
            for (var period = 0; period < max; period++) { Text((period + 1).ToString(), 100 + period * 100, bottom + 60); Text(normal[period], 100 + period * 100, bottom + 90); }
        }
        return new(width + 20, bottom + 150, glyphs, lines);
    }
}
