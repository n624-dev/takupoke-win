using System.Globalization;
using System.Text.RegularExpressions;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

/// Builds candidates from ruled tables and independently printed headers. Unknown
/// role layouts, missing headers and ink with no recognized text fail closed.
public static class RecoveryDocumentBuilder
{
    private sealed record Atom(string Id, int Page, PdfGlyph Glyph)
    {
        public RecoveryBox Box => new(Glyph.X, Glyph.Y, Glyph.Width, Glyph.Height);
    }
    private sealed record Label(string Value, int Page, RecoveryBox Box, IReadOnlyList<string> Ids);
    private static RecoveryBox Bounds(IEnumerable<Atom> atoms)
    {
        var a = atoms.ToArray(); var left = a.Min(x => x.Box.X); var top = a.Min(x => x.Box.Y);
        return new(left, top, a.Max(x => x.Box.X + x.Box.Width) - left, a.Max(x => x.Box.Y + x.Box.Height) - top);
    }
    private static IEnumerable<Label> Labels(IReadOnlyList<Atom> atoms)
    {
        foreach (var row in atoms.GroupBy(a => a.Page))
        foreach (var band in PdfGrid.Rows(row.Select(a => a.Glyph)))
        {
            var pieces = new List<List<Atom>>();
            foreach (var glyph in band)
            {
                var atom = row.Single(a => ReferenceEquals(a.Glyph, glyph));
                if (pieces.Count == 0 || glyph.X - (pieces[^1][^1].Box.X + pieces[^1][^1].Box.Width) > Math.Max(2, glyph.Height * .7)) pieces.Add([]);
                pieces[^1].Add(atom);
            }
            foreach (var piece in pieces)
            {
                // Retain real per-character geometry; do not invent sub-boxes for an OCR line.
                var text = PdfGrid.Key(string.Concat(piece.Select(a => a.Glyph.Text)));
                yield return new(text, row.Key, Bounds(piece), piece.Select(a => a.Id).ToArray());
                var pattern = @"[1-8]時限目|[1-8][・〜-][1-8]時限連続|\d{1,2}:\d{2}[~〜～]\d{1,2}:\d{2}|(?:令和\d{1,2}|\d{4})年度|前期|後期|試験返却時間割|定期試験時間割|試験時間割|通常時間割|授業時間割|時間割";
                var raw = string.Concat(piece.Select(a => a.Glyph.Text));
                foreach (Match match in Regex.Matches(raw, "(?:" + pattern + ")[：:]?|(?:" + RecoveryRoleLabels.Pattern + ")[：:]"))
                {
                    var offset = 0; var selected = new List<Atom>();
                    foreach (var atom in piece) { var end = offset + atom.Glyph.Text.Length; if (offset >= match.Index && end <= match.Index + match.Length) selected.Add(atom); offset = end; }
                    if (selected.Count > 0 && string.Concat(selected.Select(a => a.Glyph.Text)) == match.Value && match.Value != text) yield return new(match.Value.TrimEnd(':', '：'), row.Key, Bounds(selected), selected.Select(a => a.Id).ToArray());
                }
            }
        }
    }
    private static RecoveryHeaderRegion? Region(Label label, RecoveryBox target)
    {
        var horizontal = Math.Min(label.Box.X + label.Box.Width, target.X + target.Width) > Math.Max(label.Box.X, target.X);
        var vertical = Math.Min(label.Box.Y + label.Box.Height, target.Y + target.Height) > Math.Max(label.Box.Y, target.Y);
        if (label.Box.Y + label.Box.Height <= target.Y && horizontal) return new(label.Page, label.Box, RecoveryHeaderAxis.Above);
        if (label.Box.X + label.Box.Width <= target.X && vertical) return new(label.Page, label.Box, RecoveryHeaderAxis.Left);
        return null;
    }
    private static string? Day(string value, RecoveryDocumentKind kind, int year)
    {
        if (kind == RecoveryDocumentKind.Timetable) return value switch { "月" or "月曜" or "月曜日" => "1", "火" or "火曜" or "火曜日" => "2", "水" or "水曜" or "水曜日" => "3", "木" or "木曜" or "木曜日" => "4", "金" or "金曜" or "金曜日" => "5", _ => null };
        var m = Regex.Match(value, @"^(?:(\d{4})[-/])?(\d{1,2})[-/](\d{1,2})$");
        if (!m.Success) m = Regex.Match(value, @"^(?:(\d{4})年)?(\d{1,2})月(\d{1,2})日$");
        if (!m.Success) return null;
        var month = int.Parse(m.Groups[2].Value); var y = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : year + (month < 4 ? 1 : 0);
        try { return new DateOnly(y, month, int.Parse(m.Groups[3].Value)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } catch (ArgumentOutOfRangeException) { return null; }
    }
    public static RecoveryDocument Build(string hash, MaterialKind materialKind, IReadOnlyList<PdfPageLayout> pages,
        Func<int, RecoveryBox, bool> inkFree, IReadOnlySet<int>? ocrPages = null, CancellationToken token = default, bool allowStructureProposal = false)
    {
        var kind = RecoveryPolicy.Kind(materialKind) ?? throw new InvalidDataException("PDF復旧の対象外です。");
        if (pages.Count is < 1 or > 12) throw new InvalidDataException("PDFのページ数が上限を超えています。");
        var atoms = pages.SelectMany((p, i) => p.Glyphs.Select((g, n) => new Atom($"p{i + 1}s{n}", i + 1, g))).ToArray();
        if (atoms.Length > 100000 || atoms.Any(a => !a.Box.Valid)) throw new InvalidDataException("文字の位置を確認できません。");
        atoms = atoms.OrderBy(a => a.Page).ThenBy(a => Math.Round(a.Glyph.Cy / 2)).ThenBy(a => a.Glyph.Cx).ToArray();
        var labels = Labels(atoms).ToArray();
        var years = labels.Where(l => Regex.IsMatch(l.Value, @"^(?:\d{4}|令和\d{1,2})年度$")).ToArray();
        var yearValues = years.Select(l => l.Value.StartsWith("令和", StringComparison.Ordinal) ? 2018 + int.Parse(l.Value[2..^2]) : int.Parse(l.Value[..4])).Distinct().ToArray();
        if (yearValues.Length != 1) throw new InvalidDataException("年度の独立した見出しがありません。");
        var year = yearValues[0];
        var terms = labels.Where(l => l.Value is "前期" or "後期").ToArray();
        string? term = kind == RecoveryDocumentKind.Timetable && terms.Select(l => l.Value).Distinct().Count() == 1 ? terms[0].Value : null;
        if (kind == RecoveryDocumentKind.Timetable && term is null) throw new InvalidDataException("学期の見出しを確認できません。");
        Label Expand(Label label)
        {
            try { var b = new PdfGrid(pages[label.Page - 1]).Box(label.Box.X + label.Box.Width / 2, label.Box.Y + label.Box.Height / 2); return label with { Box = new(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top) }; }
            catch (PdfParseException) { return label; }
        }
        var legacyClassLabels = new List<Label>();
        foreach (var dept in labels.Where(l => Regex.IsMatch(l.Value, "^(?:[1-3]|CN|ES|IT)$")))
        {
            var page = pages[dept.Page - 1];
            var periodRows = PdfGrid.Rows(page.Glyphs).Where(r => Regex.IsMatch(PdfGrid.Key(string.Concat(r.Select(g => g.Text))), "^(?:12345678){5}$|^(?:123456){2,3}$")).ToArray();
            if (periodRows.Length != 1 || dept.Box.X >= periodRows[0].Min(g => g.X) || dept.Box.Y <= periodRows[0].Max(g => g.Y + g.Height)) continue;
            try
            {
                var grid = new PdfGrid(pages[dept.Page - 1]); var classBox = grid.Box(dept.Box.X + dept.Box.Width / 2, dept.Box.Y + dept.Box.Height / 2);
                var gradeBox = grid.Box(classBox.Left - 2, dept.Box.Y + dept.Box.Height / 2);
                var gradeAtoms = atoms.Where(a => a.Page == dept.Page && a.Glyph.Cx > gradeBox.Left && a.Glyph.Cx < gradeBox.Right && a.Glyph.Cy > gradeBox.Top && a.Glyph.Cy < gradeBox.Bottom).ToArray();
                var grade = PdfGrid.Key(string.Concat(gradeAtoms.Select(a => a.Glyph.Text))); var cls = grade + "_" + dept.Value;
                if (!ClassSelection.Candidates.Contains(cls)) continue;
                var ids = gradeAtoms.Select(a => a.Id).Concat(dept.Ids).ToHashSet();
                var box = new RecoveryBox(gradeBox.Left, Math.Min(gradeBox.Top, classBox.Top), classBox.Right - gradeBox.Left, Math.Max(gradeBox.Bottom, classBox.Bottom) - Math.Min(gradeBox.Top, classBox.Top));
                legacyClassLabels.Add(new(cls, dept.Page, box, atoms.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToArray()));
            }
            catch (PdfParseException) { }
        }
        var maxPeriod = kind == RecoveryDocumentKind.Exam ? 6 : 8;
        var classLabels = labels.Where(l => ClassSelection.Candidates.Contains(l.Value.Replace('-', '_'))).Select(l => Expand(l) with { Value = l.Value.Replace('-', '_') }).Concat(legacyClassLabels).ToArray();
        var dayLabels = labels.Select(l => (Label: l, Day: Day(l.Value, kind, year))).Where(p => p.Day is not null).Select(p => Expand(p.Label) with { Value = p.Day! }).ToArray();
        var periodLabels = labels.Select(l => (Label: l, Match: Regex.Match(l.Value, @"^(?:第)?([1-8])(?:限|時限)?$"))).Where(p => p.Match.Success && int.Parse(p.Match.Groups[1].Value) <= maxPeriod).Select(p => Expand(p.Label) with { Value = p.Match.Groups[1].Value }).ToArray();
        if (classLabels.Length == 0 || dayLabels.Length == 0 || periodLabels.Length == 0) throw new InvalidDataException("クラス・日付・時限の見出しを確認できません。");
        var trustedNormal = new Dictionary<int, TimetableAnalysis>(); SpecialAnalysis? trustedSpecial = null;
        if (kind == RecoveryDocumentKind.Timetable)
        {
            foreach (var (page, index) in pages.Select((p, i) => (p, i + 1))) try { trustedNormal[index] = PdfScheduleParser.Timetable([page], token); } catch (PdfParseException) { }
        }
        else try { trustedSpecial = PdfScheduleParser.Special(pages, materialKind, token); } catch (PdfParseException) { }
        var sources = atoms.ToDictionary(a => a.Id, a => new RecoverySource(a.Id, "header", a.Page, a.Glyph.Text, a.Box, ocrPages?.Contains(a.Page) == true, a.Glyph.SourceLine, a.Glyph.SourceOrder));
        var cells = new List<RecoveryCell>();
        var usedClass = new Dictionary<string, HashSet<string>>(); var usedDay = new Dictionary<string, HashSet<string>>(); var usedPeriod = new Dictionary<string, HashSet<string>>();
        void Register(Dictionary<string, HashSet<string>> inventory, string key, IEnumerable<string> ids) { if (!inventory.TryGetValue(key, out var set)) inventory[key] = set = []; set.UnionWith(ids); }
        foreach (var (page, pi) in pages.Select((p, i) => (p, i + 1)))
        {
            token.ThrowIfCancellationRequested();
            var grid = new PdfGrid(page);
            var headers = classLabels.Where(l => l.Page == pi).Concat(dayLabels.Where(l => l.Page == pi)).Concat(periodLabels.Where(l => l.Page == pi)).ToArray();
            var xs = page.Lines.Where(l => l.Vertical).Select(l => l.X1).Distinct().Order().ToArray();
            var ys = page.Lines.Where(l => l.Horizontal).Select(l => l.Y1).Distinct().Order().ToArray();
            if (xs.Length * (long)ys.Length > 250000) throw new InvalidDataException("表の罫線が解析上限を超えています。");
            var boxes = new HashSet<RecoveryBox>();
            foreach (var (left, right) in xs.Zip(xs.Skip(1)))
            foreach (var (top, bottom) in ys.Zip(ys.Skip(1)))
            {
                if (right - left < 4 || bottom - top < 4) continue;
                try { var b = grid.Box((left + right) / 2, (top + bottom) / 2); boxes.Add(new(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top)); }
                catch (PdfParseException) { }
                if (boxes.Count > 20000) throw new InvalidDataException("表の候補数が上限を超えています。");
            }
            foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X))
            {
                // Header cells are not timetable body cells.
                if (headers.Any(l => box.Contains(l.Box))) continue;
                Label? Closest(IEnumerable<Label> candidates) => candidates.Where(l => Region(l, box) is not null).OrderBy(l => Region(l, box)!.Axis == RecoveryHeaderAxis.Above ? box.Y - l.Box.Y - l.Box.Height : box.X - l.Box.X - l.Box.Width).FirstOrDefault();
                var cls = Closest(classLabels.Where(l => l.Page == pi)); var day = Closest(dayLabels.Where(l => l.Page == pi));
                if (cls is null || day is null) continue;
                var period = periodLabels.Where(l => l.Page == pi && Region(l, box) is not null).GroupBy(l => l.Value).Select(g => Closest(g)!).Where(l => l is not null).ToArray();
                if (period.Length == 0) continue;
                // Multiple left headers in one column are alternatives, not a merged span.
                var chosenPeriods = period.Where(l => Region(l, box)!.Axis == RecoveryHeaderAxis.Above).ToArray();
                if (chosenPeriods.Length == 0) chosenPeriods = [Closest(period)!];
                var slots = chosenPeriods.OrderBy(l => int.Parse(l.Value)).Select(l => new RecoverySlot(cls.Value, day.Value, int.Parse(l.Value))).ToArray();
                if (cells.Any(c => c.Slots.Any(slots.Contains))) throw new InvalidDataException("時間割の同じ位置に複数のセル候補があります。");
                var id = $"p{pi}c{cells.Count}";
                var inside = atoms.Where(a => a.Page == pi && box.Contains(a.Box)).ToArray();
                var empty = inside.Length == 0 && inkFree(pi, box);
                if (inside.Length == 0 && !empty) throw new InvalidDataException("文字を読めなかったセルを空欄として扱えません。");
                IReadOnlyList<RecoveryRoleScope> scopes = []; IReadOnlyList<RecoveryLessonBinding> fixedBindings = [];
                if (!empty)
                {
                    try { scopes = RoleScopes(id, pi, box, inside, labels, inkFree); }
                    catch (InvalidDataException)
                    {
                        LessonNames? trustedNames = null;
                        if (trustedNormal.TryGetValue(pi, out var normal)) { var matched = normal.Lessons.Where(l => l.ClassName == cls.Value && l.Weekday.ToString() == day.Value && slots.All(s => s.Period == l.Period)).ToArray(); if (matched.Length == 1) trustedNames = matched[0].Names; }
                        else if (trustedSpecial is not null) { var matched = trustedSpecial.Lessons.Where(l => l.Page == pi && l.ClassName == cls.Value && l.Date == day.Value && l.Period == slots.Min(s => s.Period)).ToArray(); if (matched.Length == 1) trustedNames = matched[0].Names; }
                        var rows = PdfGrid.Rows(inside.Select(a => a.Glyph));
                        var text = rows.Select(r => PdfGrid.Key(string.Concat(r.Select(g => g.Text)))).ToArray();
                        if (trustedNames is not null && rows.Count == 3 && text.SequenceEqual(new[] { trustedNames.Subject, trustedNames.Teacher, trustedNames.Room }.Select(PdfGrid.Key)))
                        {
                            var bindings = rows.Select(r => (IReadOnlyList<string>)r.Select(g => inside.Single(a => ReferenceEquals(a.Glyph, g)).Id).ToArray()).ToArray();
                            fixedBindings = [new(bindings[0], bindings[1], bindings[2])];
                        }
                        else if (!allowStructureProposal || inside.Length > 512) throw;
                    }
                }
                foreach (var a in inside) sources[a.Id] = sources[a.Id] with { CellId = id };
                var blanks = scopes.Where(s => s.EmptyVerified).Select(s => s.Role.ToString().ToLowerInvariant()).ToArray();
                var parallelCount = empty || fixedBindings.Count > 0 || scopes.Count == 0 ? 1 : scopes.Select(s => s.LessonIndex).Distinct().Count();
                var cell = new RecoveryCell(id, pi, box, RecoveryInputState.Complete, slots, inside.Select(a => a.Id).ToArray(), blanks, empty, parallelCount)
                { BindingMode = empty || fixedBindings.Count > 0 ? RecoveryBindingMode.Fixed : RecoveryBindingMode.RoleProposal, RoleScopes = scopes, LessonBindings = fixedBindings,
                    ClassHeaderIds = cls.Ids, DayHeaderIds = day.Ids, PeriodHeaderIds = chosenPeriods.SelectMany(l => l.Ids).ToArray(),
                    ClassRegion = Region(cls, box), DayRegion = Region(day, box), PeriodRegions = chosenPeriods.ToDictionary(l => l.Value, l => Region(l, box)!) };
                cells.Add(cell); Register(usedClass, cls.Value, cls.Ids); Register(usedDay, day.Value, day.Ids);
                foreach (var p in chosenPeriods) Register(usedPeriod, p.Value, p.Ids);
            }
        }
        var classes = usedClass.Keys.Order().ToArray(); var days = usedDay.Keys.Order().ToArray();
        var required = (from c in classes from d in days from p in Enumerable.Range(1, maxPeriod) select new RecoverySlot(c, d, p)).ToArray();
        var titleLabels = kind switch { RecoveryDocumentKind.Timetable => new[] { "時間割", "通常時間割", "授業時間割" }, RecoveryDocumentKind.Exam => ["試験時間割", "定期試験時間割"], _ => ["試験返却時間割"] };
        var title = labels.Where(l => titleLabels.Contains(l.Value)).SelectMany(l => l.Ids).ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<string>> Evidence(Dictionary<string, HashSet<string>> set) => set.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)atoms.Where(a => p.Value.Contains(a.Id)).Select(a => a.Id).ToArray());
        var document = new RecoveryDocument(hash, kind, year, term, classes, days, required, cells, atoms.Select(a => sources[a.Id]).ToArray(), true,
            years.SelectMany(l => l.Ids).Distinct().ToArray(), term is null ? [] : terms.SelectMany(l => l.Ids).Distinct().ToArray(), Evidence(usedDay), Evidence(usedClass), Evidence(usedPeriod), new Dictionary<string, string>(), [], []) { DocumentTitleEvidence = title };
        return AddTimes(document, labels);
    }
    private static IReadOnlyList<RecoveryRoleScope> RoleScopes(string cellId, int page, RecoveryBox box, Atom[] body, Label[] labels, Func<int, RecoveryBox, bool> inkFree)
    {
        var result = new List<RecoveryRoleScope>(); double[]? columnAnchors = null;
        foreach (var role in Enum.GetValues<RecoveryFieldRole>())
        {
            var names = RecoveryRoleLabels.For(role);
            var candidates = labels.Where(l => l.Page == page && box.Contains(l.Box) && names.Contains(l.Value.TrimEnd(':', '：'))).DistinctBy(l => string.Join(",", l.Ids)).OrderBy(l => l.Box.X).ToArray();
            if (candidates.Length is < 1 or > 4 || columnAnchors is not null && (columnAnchors.Length != candidates.Length || columnAnchors.Where((x, i) => Math.Abs(x - candidates[i].Box.X) > 1).Any())) throw new InvalidDataException("科目・教員・教室の独立した原文ラベルを確認できません。");
            columnAnchors ??= candidates.Select(l => l.Box.X).ToArray();
            foreach (var (label, lessonIndex) in candidates.Select((l, i) => (l, i)))
            {
                var sameRow = body.Where(a => Math.Abs(a.Glyph.Cy - (label.Box.Y + label.Box.Height / 2)) <= label.Box.Height / 2).ToArray();
                var top = sameRow.Min(a => a.Box.Y); var bottom = sameRow.Max(a => a.Box.Y + a.Box.Height);
                var right = lessonIndex + 1 < candidates.Length ? candidates[lessonIndex + 1].Box.X : box.X + box.Width;
                var scope = new RecoveryBox(label.Box.X + label.Box.Width, top, right - label.Box.X - label.Box.Width, bottom - top);
                var values = body.Where(a => scope.Contains(a.Box)).ToArray();
                result.Add(new(lessonIndex, role, page, scope, label.Ids, new(page, label.Box, RecoveryHeaderAxis.Left), RecoveryRoleProof.InlineLabel,
                    role != RecoveryFieldRole.Subject && values.Length == 0 && inkFree(page, scope)));
            }
        }
        return result;
    }
    private static RecoveryDocument AddTimes(RecoveryDocument document, Label[] labels)
    {
        if (document.Kind == RecoveryDocumentKind.Timetable) return document;
        var times = new Dictionary<string, string>(); var spans = new Dictionary<string, string>();
        var evidence = new Dictionary<string, IReadOnlyList<string>>(); var bindings = new Dictionary<string, RecoveryClockBinding>();
        var replicas = new Dictionary<string, IReadOnlyList<RecoveryClockBinding>>();
        var commonIds = new List<string>(); var commonRegions = new Dictionary<string, RecoveryHeaderRegion>();
        var normalNote = labels.Where(l => l.Value.Contains("日は通常の授業日どおりの授業時間", StringComparison.Ordinal)).ToArray();
        var firstNote = labels.Where(l => Regex.IsMatch(l.Value, @"^\d{1,2}月\d{1,2}日の時間割は以下のとおり(?:です)?。?$" )).ToArray();
        var noteIdSet = firstNote.SelectMany(l => l.Ids).Concat(normalNote.SelectMany(l => l.Ids)).ToHashSet();
        var normalIds = document.Sources.Where(s => noteIdSet.Contains(s.Id)).Select(s => s.Id).ToArray();
        foreach (var clock in labels.Where(l => Regex.IsMatch(l.Value, @"^\d{1,2}:\d{2}[~〜～]\d{1,2}:\d{2}$")))
        {
            var explicitDays = labels.Where(l => l.Page == clock.Page && document.Days.Contains(Day(l.Value, document.Kind, document.SchoolYear) ?? "") && Region(l, clock.Box) is not null).OrderBy(l => clock.Box.Y - l.Box.Y + clock.Box.X - l.Box.X).ToArray();
            var periods = labels.Where(l => l.Page == clock.Page && Regex.IsMatch(l.Value, @"^[1-8](?:限|時限|時限目)?$|^[1-8][・〜-][1-8](?:時限連続|時限|限)$") && Region(l, clock.Box) is not null).OrderBy(l => clock.Box.Y - l.Box.Y + clock.Box.X - l.Box.X).ToArray();
            if (periods.Length == 0) continue;
            var periodLabel = periods[0]; var range = Regex.Match(periodLabel.Value, @"^([1-8])(?:[・〜-]([1-8]))?");
            var start = int.Parse(range.Groups[1].Value); var end = range.Groups[2].Success ? int.Parse(range.Groups[2].Value) : start;
            var clockParts = clock.Value.Replace('~', '〜').Replace('～', '〜').Split('〜');
            var printed = string.Join('〜', clockParts.Select(p => TimeOnly.TryParseExact(p, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : throw new InvalidDataException("時刻を確認できません。")));
            string[] applicableDays; Label? common = null;
            if (explicitDays.Length > 0) applicableDays = [Day(explicitDays[0].Value, document.Kind, document.SchoolYear)!];
            else
            {
                common = labels.Where(l => l.Page == clock.Page && l.Box.Y + l.Box.Height <= periodLabel.Box.Y &&
                    (document.Kind == RecoveryDocumentKind.Exam ? l.Value is "試験時間割" or "定期試験時間割" : firstNote.Any(n => n.Ids.SequenceEqual(l.Ids))))
                    .OrderByDescending(l => l.Box.Y).FirstOrDefault();
                if (common is null) continue;
                applicableDays = document.Kind == RecoveryDocumentKind.Exam ? document.Days.ToArray() : [document.Days.Order().First()];
                var left = Math.Min(common.Box.X, labels.Where(l => l.Page == clock.Page).Min(l => l.Box.X));
                var right = labels.Where(l => l.Page == clock.Page).Max(l => l.Box.X + l.Box.Width);
                var region = new RecoveryHeaderRegion(clock.Page, new(left, common.Box.Y, right - left, common.Box.Height), RecoveryHeaderAxis.Above);
                if (commonRegions.TryGetValue(clock.Page.ToString(), out var existing) && existing != region) throw new InvalidDataException("共通の時刻表の見出しが一意ではありません。");
                commonRegions[clock.Page.ToString()] = region; commonIds.AddRange(common.Ids);
            }
            foreach (var day in applicableDays)
            {
                var suffix = start == end ? start.ToString() : $"{start}-{end}"; var key = day + ":" + suffix;
                if (start != end && !document.Cells.Any(c => c.Slots.Count > 1 && c.Slots[0].Day == day && c.Slots.Min(s => s.Period) == start && c.Slots.Max(s => s.Period) == end)) continue;
                var dictionary = start == end ? times : spans;
                var binding = new RecoveryClockBinding(clock.Page, clock.Box, day, start, end, common is null ? explicitDays[0].Ids : [], common is null ? Region(explicitDays[0], clock.Box) : null, periodLabel.Ids, Region(periodLabel, clock.Box)!) { CommonScope = common is not null };
                if (dictionary.TryGetValue(key, out var previous)) { if (previous != printed || bindings[key].Page == clock.Page) throw new InvalidDataException("複数のページで時刻が一致しません。"); replicas[key] = (replicas.GetValueOrDefault(key) ?? []).Append(binding).ToArray(); evidence[key] = evidence[key].Concat(clock.Ids).ToArray(); }
                else { dictionary[key] = printed; evidence[key] = clock.Ids; bindings[key] = binding; }
            }
        }
        if (document.Kind == RecoveryDocumentKind.Return && normalIds.Length > 0)
        {
            var normal = new[] { "08:50〜09:35", "09:35〜10:20", "10:30〜11:15", "11:15〜12:00", "12:50〜13:35", "13:35〜14:20", "14:30〜15:15", "15:15〜16:00" };
            foreach (var day in document.Days.Order().Skip(1)) foreach (var p in Enumerable.Range(1, 8)) { var key = day + ":" + p; if (!times.ContainsKey(key)) { times[key] = normal[p - 1]; evidence[key] = normalIds; } }
            foreach (var cell in document.Cells.Where(c => c.Slots.Count > 1 && c.Slots[0].Day != document.Days.Order().First()))
            {
                var start = cell.Slots.Min(s => s.Period); var end = cell.Slots.Max(s => s.Period); var key = cell.Slots[0].Day + $":{start}-{end}";
                if (start < 1 || end > 8) throw new InvalidDataException("連続時限の範囲を確認できません。");
                if (!spans.ContainsKey(key)) { spans[key] = normal[start - 1].Split('〜')[0] + "〜" + normal[end - 1].Split('〜')[1]; evidence[key] = normalIds; }
            }
        }
        var dayEvidence = document.DayEvidence.ToDictionary(p => p.Key, p => p.Value);
        var periodEvidence = document.PeriodEvidence.ToDictionary(p => p.Key, p => p.Value);
        foreach (var binding in bindings.Values.Concat(replicas.Values.SelectMany(b => b)))
        {
            if (!binding.CommonScope) dayEvidence[binding.Day] = dayEvidence.GetValueOrDefault(binding.Day, []).Concat(binding.DayHeaderIds).Distinct().ToArray();
            if (binding.SpanStart == binding.SpanEnd) { var key = binding.SpanStart.ToString(); periodEvidence[key] = periodEvidence.GetValueOrDefault(key, []).Concat(binding.PeriodHeaderIds).Distinct().ToArray(); }
        }
        return document with { Times = times, SpanTimes = spans, TimeEvidence = bindings.Keys.SelectMany(k => evidence[k]).Distinct().ToArray(), ClockEvidence = evidence, ClockBindings = bindings, ClockReplicas = replicas,
            DayEvidence = dayEvidence, PeriodEvidence = periodEvidence, CommonClockEvidence = commonIds.Distinct().ToArray(), CommonClockRegions = commonRegions, NormalTimeNoteEvidence = normalIds };
    }
}
