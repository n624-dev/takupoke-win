using System.Globalization;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryDateHeaderOwnershipTests
{
    [Fact]
    public void HyphenatedClassRailDoesNotBecomeJanuaryDateInCompleteExam()
    {
        var pages = ExamPages();
        var strict = PdfScheduleParser.Special(pages, MaterialKind.Exam);
        Assert.Equal(480, strict.Lessons.Count);
        Assert.Equal(17, strict.CoveredClasses.Count);
        var document = Build(pages);
        Assert.Equal(510, document.RequiredSlots.Count);
        Assert.Equal(30, document.Cells.Count(c => c.ConfirmedEmpty));
        Assert.Equal(Enumerable.Range(1, 5).Select(d => $"2035-10-{d:00}"), document.Days);
        Assert.All(document.DayEvidence.Values.SelectMany(ids => ids), id =>
            Assert.DoesNotContain(id, document.ClassEvidence.Values.SelectMany(ids => ids)));
        Assert.Equal(480, document.Cells.Sum(c => c.LessonBindings.Count * c.Slots.Count));
        var merged = Assert.Single(document.Cells, c => c.Slots.Count == 2);
        Assert.Equal(new[] { 1, 2 }, merged.Slots.Select(s => s.Period));
        Assert.Equal("1_1", merged.Slots[0].ClassName);
        Assert.Equal("2035-10-01", merged.Slots[0].Day);
        var sources = document.Sources.ToDictionary(s => s.Id);
        foreach (var cell in document.Cells.Where(c => !c.ConfirmedEmpty))
        {
            var binding = Assert.Single(cell.LessonBindings);
            var names = new LessonNames(string.Concat(binding.Subject.Select(id => sources[id].Text)),
                string.Concat(binding.Teacher.Select(id => sources[id].Text)), string.Concat(binding.Room.Select(id => sources[id].Text)));
            foreach (var slot in cell.Slots)
            {
                var lesson = Assert.Single(strict.Lessons, l => l.ClassName == slot.ClassName && l.Date == slot.Day && l.Period == slot.Period);
                Assert.Equal(lesson.Names, names);
            }
        }
    }

    [Fact]
    public void RealHyphenatedJanuaryDatesRemainDatesWithIndependentInlineRoles()
    {
        var document = Build(ExamPages(january: true, inlineRoles: true));
        Assert.Equal(510, document.RequiredSlots.Count);
        Assert.Equal(17, document.Classes.Count);
        Assert.Equal(Enumerable.Range(1, 5).Select(d => $"2036-01-{d:00}"), document.Days);
        Assert.All(document.DayEvidence.Values.SelectMany(ids => ids), id =>
            Assert.DoesNotContain(id, document.ClassEvidence.Values.SelectMany(ids => ids)));
        Assert.Equal(480, document.Cells.Where(c => c.RoleScopes.Count == 3).Sum(c => c.Slots.Count));
        // The existing public Validator does not accept short month-day hyphens
        // (it accepts full ISO dates). Header recognition must still preserve them;
        // this builder change does not authorize previously rejected evidence.
        Assert.Contains("dayEvidence", RecoveryValidator.InputErrors(document));
    }

    [Fact]
    public void ForeignHyphenatedHeaderDoesNotSupplyTrustedUnlabeledBodyRoles()
    {
        var pages = ExamPages();
        pages[0] = pages[0] with { Glyphs = pages[0].Glyphs.Append(new PdfGlyph("1-1", 130, 58, 18, 12)).ToArray() };
        Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(pages, MaterialKind.Exam));
        Assert.Throws<InvalidDataException>(() => Build(pages));
    }

    [Fact]
    public void UnlabeledJanuaryBodyDoesNotGainRolesFromUnsupportedStrictDateSyntax()
    {
        var pages = ExamPages(january: true);
        Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(pages, MaterialKind.Exam));
        Assert.Throws<InvalidDataException>(() => Build(pages));
    }

    [Fact]
    public async Task MergedGradeEvidenceOwnsOnlyEachPhysicalClassRowInCompleteReturn()
    {
        var page = ReturnPage();
        var strict = PdfScheduleParser.Special([page], MaterialKind.ExamReturn);
        Assert.Equal(660, strict.Lessons.Count);
        Assert.Equal(17, strict.CoveredClasses.Count);
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.ExamReturn, [page],
            (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));
        Assert.Equal(680, document.RequiredSlots.Count);
        Assert.Equal(680, document.Cells.Count);
        Assert.Equal(20, document.Cells.Count(c => c.ConfirmedEmpty));
        Assert.Equal(660, document.Cells.Sum(c => c.LessonBindings.Count));
        Assert.Equal(680, document.Cells.SelectMany(c => c.Slots).Distinct().Count());
        Assert.Empty(RecoveryValidator.InputErrors(document));
        var gradeIds = document.Classes.Take(3).Select(cls => document.ClassEvidence[cls]
            .Single(id => document.Sources.Single(s => s.Id == id).Box.X == 40)).ToArray();
        Assert.Single(gradeIds.Distinct());
        var originalGrade = document.Sources.Single(s => s.Id == gradeIds[0]);
        foreach (var cell in document.Cells.Where(c => c.Slots[0].ClassName == "1_1"))
        {
            Assert.True(cell.ClassRegion!.Box.Contains(originalGrade.Box));
            Assert.Equal(194, cell.Box.Y);
        }
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.NotNull(run.Result);
        Assert.True(RecoveryValidator.Validate(document, run.Result!).CanAdopt);
    }

    [Fact]
    public void ChangingPrintedDepartmentKeepsOwnershipOnItsOriginalPhysicalRow()
    {
        var page = ReturnPage();
        page = page with { Glyphs = page.Glyphs.Select(g => g.X == 90 && g.Y == 234 ? g with { Text = "3" } :
            g.X == 90 && g.Y == 426 ? g with { Text = "1" } : g).ToArray() };
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.ExamReturn, [page],
            (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));
        Assert.Equal(680, document.RequiredSlots.Count);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        Assert.All(document.Cells.Where(c => c.Slots[0].ClassName == "1_3"), c => Assert.Equal(194, c.Box.Y));
        Assert.All(document.Cells.Where(c => c.Slots[0].ClassName == "1_1"), c => Assert.Equal(386, c.Box.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingPrintedAiGradeAndCanonicalClassKeepAllOriginalEvidence(bool separateGradeAtoms)
    {
        var pages = ExamPages();
        if (separateGradeAtoms) pages[5] = pages[5] with { Glyphs = pages[5].Glyphs.SelectMany(g => g.Text is "1年" or "2年"
            ? new[] { g with { Text = g.Text[..1], Width = 6 }, g with { Text = "年", X = g.X + 6, Width = 6 } } : new[] { g }).ToArray() };
        var document = Build(pages);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        foreach (var cls in new[] { "AI_1", "AI_2" })
        {
            var ids = document.ClassEvidence[cls];
            Assert.Equal(cls[^1] + "年" + cls, string.Concat(ids.Select(id => document.Sources.Single(s => s.Id == id).Text)));
            Assert.All(document.Cells.Where(c => c.Slots[0].ClassName == cls), c => Assert.Equal(ids, c.ClassHeaderIds));
        }
        foreach (var (page, pi) in pages.Select((p, i) => (p, i + 1)))
        foreach (var (glyph, gi) in page.Glyphs.Select((g, i) => (g, i)))
        {
            var source = Assert.Single(document.Sources, s => s.Id == $"p{pi}s{gi}");
            Assert.Equal(glyph.Text, source.Text);
            Assert.Equal(new RecoveryBox(glyph.X, glyph.Y, glyph.Width, glyph.Height), source.Box);
        }
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.True(RecoveryValidator.Validate(document, run.Result!).CanAdopt);
        Assert.Equal(RecoveryValidator.Version, run.Result!.Metadata.ValidatorVersion);
        Assert.Contains("versions", RecoveryValidator.Validate(document, run.Result with
            { Metadata = run.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version - 1 } }).Errors);
        var oldStructure = run.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version - 1 };
        Assert.Contains("structureMetadata", RecoveryValidator.Validate(document with { StructureMetadata = oldStructure }, run.Result).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AiHeaderProofIsIndependentOfPageOrderAndCount(bool additionalPage)
    {
        var pages = ExamPages(inlineRoles: true).Reverse().ToList();
        if (additionalPage) pages.Insert(2, new PdfPageLayout(400, 400, [], []));
        var document = Build(pages.ToArray());
        Assert.Empty(RecoveryValidator.InputErrors(document));
        Assert.All(document.Cells.Where(c => c.Slots[0].ClassName.StartsWith("AI_", StringComparison.Ordinal)), c => Assert.Equal(1, c.Page));
        Assert.Equal(510, document.RequiredSlots.Count);
    }

    [Fact]
    public void CanonicalOnlyAiHeaderDoesNotInventAnUnprintedGrade()
    {
        var pages = ExamPages(inlineRoles: true);
        pages[5] = pages[5] with { Glyphs = pages[5].Glyphs.Where(g => g.Text is not ("1年" or "2年")).ToArray() };
        var document = Build(pages);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        foreach (var cls in new[] { "AI_1", "AI_2" })
            Assert.Equal(cls, string.Concat(document.ClassEvidence[cls].Select(id => document.Sources.Single(s => s.Id == id).Text)));
    }

    [Theory]
    [InlineData("contradictory")]
    [InlineData("duplicate")]
    [InlineData("unowned")]
    [InlineData("missingCanonical")]
    [InlineData("missingGradeSuffix")]
    [InlineData("crossClass")]
    public void InvalidPrintedGradeNeverGetsIgnoredOrUsedAsAnAiClass(string mutation)
    {
        var pages = ExamPages(inlineRoles: true);
        var glyphs = pages[5].Glyphs.ToList();
        var grade = glyphs.FindIndex(g => g.Text == "1年");
        var canonical = glyphs.FindIndex(g => g.Text == "AI_1");
        switch (mutation)
        {
            case "contradictory": glyphs[grade] = glyphs[grade] with { Text = "2年" }; break;
            case "duplicate": glyphs.Add(glyphs[grade] with { X = 200 }); break;
            case "unowned": glyphs[grade] = glyphs[grade] with { Y = 58 }; break;
            case "missingCanonical": glyphs.RemoveAt(canonical); break;
            case "missingGradeSuffix": glyphs[grade] = glyphs[grade] with { Text = "1" }; break;
            case "crossClass": glyphs[grade] = glyphs[grade] with { X = glyphs[grade].X + 588 }; break;
        }
        pages[5] = pages[5] with { Glyphs = glyphs };
        try { Assert.NotEmpty(RecoveryValidator.InputErrors(Build(pages))); }
        catch (InvalidDataException) { }
    }

    [Theory]
    [InlineData("contradictory")]
    [InlineData("duplicate")]
    [InlineData("unowned")]
    [InlineData("crossClass")]
    [InlineData("missingPeriod")]
    [InlineData("missingGradeSuffix")]
    [InlineData("reorderedEvidence")]
    [InlineData("duplicateEvidence")]
    [InlineData("periodGap")]
    [InlineData("conflictingPeriod")]
    [InlineData("wrongPage")]
    public void ValidatorIndependentlyRejectsForgedAiCompositeProof(string mutation)
    {
        var document = Build(ExamPages());
        var cell = document.Cells.First(c => c.Slots[0].ClassName == "AI_1");
        var sources = document.Sources.ToArray();
        var grade = Array.FindIndex(sources, s => s.Text == "1年");
        switch (mutation)
        {
            case "contradictory": sources[grade] = sources[grade] with { Text = "2年" }; break;
            case "duplicate": sources[grade] = sources[grade] with { Text = "1年1年" }; break;
            case "unowned": sources[grade] = sources[grade] with { Box = sources[grade].Box with { Y = 58 } }; break;
            case "crossClass": sources[grade] = sources[grade] with { Box = sources[grade].Box with { X = sources[grade].Box.X + 588 } }; break;
            case "wrongPage": sources[grade] = sources[grade] with { Page = 5 }; break;
            case "missingGradeSuffix": sources[grade] = sources[grade] with { Text = "1" }; break;
            case "reorderedEvidence":
            case "duplicateEvidence":
                document = document with { ClassEvidence = document.ClassEvidence.ToDictionary(p => p.Key,
                    p => p.Key == "AI_1" ? (IReadOnlyList<string>)(mutation == "reorderedEvidence" ? p.Value.Reverse().ToArray() : p.Value.Concat(p.Value).ToArray()) : p.Value) };
                Assert.Contains("classEvidence", RecoveryValidator.InputErrors(document));
                return;
            case "periodGap":
            case "conflictingPeriod":
                document = document with { Cells = document.Cells.Select(c => c.Slots[0].ClassName == "AI_1" && c.Slots[0].Period == 6 &&
                    (mutation == "periodGap" || c.Slots[0].Day == "2035-10-01")
                    ? c with { PeriodRegions = c.PeriodRegions.ToDictionary(p => p.Key,
                        p => p.Value with { Box = p.Value.Box with { X = p.Value.Box.X + 1 } }) } : c).ToArray() }; break;
            case "missingPeriod":
                document = document with { Cells = document.Cells.Select(c => c.Slots[0].ClassName == "AI_1" && c.Slots[0].Period == 6
                    ? c with { PeriodRegions = new Dictionary<string, RecoveryHeaderRegion>() } : c).ToArray() }; break;
        }
        Assert.Contains("classBinding", RecoveryValidator.InputErrors(document with { Sources = sources }));
        Assert.Contains("classEvidence", RecoveryValidator.InputErrors(document with { Sources = sources }));
    }

    private static RecoveryDocument Build(PdfPageLayout[] pages) => RecoveryDocumentBuilder.Build(
        new string('a', 64), MaterialKind.Exam, pages,
        (page, box) => !pages[page - 1].Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));

    // Independently declared fictional drawing primitives. Coordinates describe
    // the source's physical rails and original atoms, never parser output or gold.
    // Six pages contain all 17 classes, five dates and six periods; the final
    // period of each page's final class is blank on every date (30 of 510 slots).
    private static PdfPageLayout[] ExamPages(bool january = false, bool inlineRoles = false)
    {
        var classes = new[] { "1_1", "1_2", "1_3", "2_CN", "2_ES", "2_IT", "3_CN", "3_ES", "3_IT", "4_CN", "4_ES", "4_IT", "5_CN", "5_ES", "5_IT", "AI_1", "AI_2" };
        var clocks = new[] { "08:40〜09:25", "09:35〜10:20", "10:30〜11:15", "11:25〜12:10", "12:50〜13:35", "13:45〜14:30" };
        var pages = new List<PdfPageLayout>();
        for (var pageNumber = 0; pageNumber < 6; pageNumber++)
        {
            var group = classes.Skip(pageNumber * 3).Take(3).ToArray();
            const int left = 120, cellWidth = 98, bodyTop = 194, rowHeight = 96;
            var right = left + group.Length * 6 * cellWidth;
            var bottom = bodyTop + 5 * rowHeight;
            var glyphs = new List<PdfGlyph>(); var rules = new List<PdfRule>();
            void Text(string text, double x, double y) => glyphs.Add(new(text, x, y, text.Length * 6, 12));
            Text("令和17年度", 20, 20); Text("試験時間割", 300, 20);
            foreach (var y in new[] { 88, 122, bodyTop }.Concat(Enumerable.Range(1, 5).Select(i => bodyTop + i * rowHeight)))
                rules.Add(new(20, y, right, y));
            rules.Add(new(20, 88, 20, bottom)); rules.Add(new(left, 88, left, bottom));
            for (var column = 0; column < group.Length; column++)
            {
                var x = left + column * 6 * cellWidth;
                if (pageNumber == 5)
                {
                    Text(group[column][^1] + "年", x + 40, 98);
                    Text(group[column], x + 160, 98);
                }
                else Text(group[column].Replace('_', '-'), x + 80, 98);
                rules.Add(new(x + 6 * cellWidth, 88, x + 6 * cellWidth, bottom));
                for (var period = 1; period <= 6; period++)
                {
                    var sx = x + (period - 1) * cellWidth;
                    Text(period.ToString(CultureInfo.InvariantCulture), sx + 40, 150);
                    if (period > 1) rules.Add(new(sx, pageNumber == 0 && column == 0 && period == 2 ? bodyTop + rowHeight : 122, sx, bottom));
                }
            }
            for (var day = 1; day <= 5; day++)
            {
                var y = bodyTop + (day - 1) * rowHeight;
                Text(january ? $"1-{day}" : $"10月{day}日", 30, y + 40);
                for (var column = 0; column < group.Length; column++)
                for (var period = 1; period <= 6; period++)
                {
                    if (column == group.Length - 1 && period == 6) continue;
                    if (pageNumber == 0 && column == 0 && day == 1 && period == 2) continue;
                    var x = left + (column * 6 + period - 1) * cellWidth;
                    var values = new[] { "架空科", "架空師", "架空室" };
                    var labels = new[] { "科目：", "教員：", "教室：" };
                    for (var role = 0; role < 3; role++)
                    {
                        if (inlineRoles) Text(labels[role], x + 8, y + 8 + role * 30);
                        Text(values[role], x + (inlineRoles ? 30 : 8), y + 8 + role * 30);
                    }
                }
            }
            for (var period = 1; period <= 6; period++)
            {
                Text($"{period}時限目", 20, bottom + 40 + (period - 1) * 40);
                Text(clocks[period - 1], right - 150, bottom + 40 + (period - 1) * 40);
            }
            Text("1・2時限連続", 20, bottom + 300);
            Text("08:40〜10:20", right - 150, bottom + 300);
            pages.Add(new(right + 20, 1160, glyphs, rules));
        }
        return pages.ToArray();
    }

    private static PdfPageLayout ReturnPage()
    {
        const int left = 120, right = 4040, bodyTop = 194, bottom = bodyTop + 17 * 96;
        var glyphs = new List<PdfGlyph>(); var rules = new List<PdfRule>();
        void Text(string text, double x, double y) => glyphs.Add(new(text, x, y, text.Length * 6, 12));
        Text("令和17年度", 20, 20); Text("試験返却時間割", 300, 20);
        foreach (var y in new[] { 88, 122, bodyTop }) rules.Add(new(20, y, right, y));
        rules.Add(new(20, 88, 20, bottom)); rules.Add(new(70, bodyTop, 70, bottom)); rules.Add(new(left, 88, left, bottom));
        for (var day = 1; day <= 5; day++)
        {
            var x = left + (day - 1) * 8 * 98;
            Text($"10/{day}", x + 250, 98);
            rules.Add(new(x + 8 * 98, 88, x + 8 * 98, bottom));
            for (var period = 1; period <= 8; period++)
            {
                var sx = x + (period - 1) * 98;
                Text(period.ToString(CultureInfo.InvariantCulture), sx + 40, 150);
                if (period > 1) rules.Add(new(sx, 122, sx, bottom));
            }
        }
        var grades = new[] { "1", "2", "3", "4", "5", "AI" };
        for (var grade = 0; grade < grades.Length; grade++)
        {
            var count = grade == 5 ? 2 : 3; var firstRow = grade * 3;
            Text(grades[grade], 40, bodyTop + firstRow * 96 + count * 48 - 6);
            rules.Add(new(20, bodyTop + firstRow * 96, 70, bodyTop + firstRow * 96));
            for (var index = 0; index < count; index++)
            {
                var row = firstRow + index; var y = bodyTop + row * 96;
                var department = grade == 0 || grade == 5 ? (index + 1).ToString(CultureInfo.InvariantCulture) : new[] { "CN", "ES", "IT" }[index];
                Text(department, 90, y + 40);
                rules.Add(new(70, y + 96, right, y + 96));
                for (var day = 1; day <= 5; day++)
                for (var period = 1; period <= 8; period++)
                {
                    if (row >= 13 && period == 8) continue;
                    var x = left + ((day - 1) * 8 + period - 1) * 98;
                    Text("架空科", x + 24, y + 8); Text("架空師", x + 24, y + 38); Text("架空室", x + 24, y + 68);
                }
            }
        }
        rules.Add(new(20, bottom, 70, bottom));
        Text("10月1日の時間割は以下のとおり", 1100, bottom + 40);
        Text("10月2日〜5日は通常の授業日どおりの授業時間", 2000, bottom + 40);
        var clocks = new[] { "08:40〜09:25", "09:35〜10:20", "10:30〜11:15", "11:25〜12:10", "12:50〜13:35", "13:45〜14:30", "14:40〜15:25", "15:35〜16:20" };
        for (var period = 1; period <= 8; period++)
        {
            Text($"{period}時限目", 20, bottom + 110 + (period - 1) * 40);
            Text(clocks[period - 1], 44, bottom + 110 + (period - 1) * 40);
        }
        return new(4060, 2800, glyphs, rules);
    }
}
