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
        public RecoveryBox Box => Glyph.InkBounds is {} b ? new(b.Left,b.Top,b.Right-b.Left,b.Bottom-b.Top) : new(Glyph.X, Glyph.Y, Glyph.Width, Glyph.Height);
    }
    private sealed record Label(string Value, int Page, RecoveryBox Box, IReadOnlyList<string> Ids, RecoveryBox? OwnershipBox = null, bool WholeOcrRow = false,
        IReadOnlyList<string>? NativeValueIds = null);
    private static RecoveryBox Bounds(IEnumerable<Atom> atoms)
    {
        var a = atoms.ToArray(); var left = a.Min(x => x.Box.X); var top = a.Min(x => x.Box.Y);
        return new(left, top, a.Max(x => x.Box.X + x.Box.Width) - left, a.Max(x => x.Box.Y + x.Box.Height) - top);
    }
    private sealed class Work(CancellationToken token)
    {
        private long _comparisons;
        public void Step(long amount = 1)
        {
            token.ThrowIfCancellationRequested();
            _comparisons += amount;
            if (_comparisons > 20_000_000) throw RecoveryWorkLimits.Exceeded("PDF復旧の位置比較数が上限を超えています。");
        }
    }
    // A merged physical cell repeats its unchanged three fields at each covered
    // Strict slot. Every slot must have exactly one independent matching lesson;
    // parallel alternatives, gaps or differing values cannot establish this binding.
    internal static LessonNames? TrustedNormalNames(TimetableAnalysis normal, IReadOnlyList<RecoverySlot> slots, Action<long> charge)
    {
        var names = TrustedNormalTuples(normal, slots, charge);
        return names is { Count: 1 } ? names[0] : null;
    }
    // Never take a tuple from only the first slot of a merged physical cell.
    internal static IReadOnlyList<LessonNames>? TrustedNormalTuples(TimetableAnalysis normal, IReadOnlyList<RecoverySlot> slots, Action<long> charge)
    {
        charge(slots.Count * 2L);
        if (slots.Count is < 1 or > 8 || slots.Distinct().Count() != slots.Count) return null;
        var first = slots[0]; LessonNames[]? names = null;
        foreach (var slot in slots)
        {
            if (slot.ClassName != first.ClassName || slot.Day != first.Day) return null;
            charge(normal.Lessons.Count);
            var matches = normal.Lessons.Where(l => l.ClassName == slot.ClassName &&
                l.Weekday.ToString(CultureInfo.InvariantCulture) == slot.Day && l.Period == slot.Period).Select(l => l.Names).ToArray();
            if (matches.Length == 2) charge(matches.Sum(n => (long)n.Subject.Length + n.Teacher.Length + n.Room.Length) * 2L);
            if (matches.Length is < 1 or > 2 || matches.Distinct().Count() != matches.Length || names is not null && !names.SequenceEqual(matches)) return null;
            names = matches;
        }
        return names;
    }
    private const string HeaderPattern = @"[1-8]時限目|[1-8][・〜-][1-8]時限連続|\d{1,2}:\d{2}[~〜～]\d{1,2}:\d{2}|(?:令和\d{1,2}|\d{4})年度|前期|後期|試験返却時間割|定期試験時間割|試験時間割|通常時間割|授業時間割|時間割";
    private const string OcrWholeLabelPattern = @"^(?:[月火水木金](?:曜(?:日)?)?|(?:(?:\d{4})年)?\d{1,2}月\d{1,2}日|(?:(?:\d{4})[-/])?\d{1,2}[-/]\d{1,2}|[1-5][_-](?:[1-3]|[A-Z]{2})|AI[_-][12]|[12]年)$";
    private static string OcrRolePrefixPattern => "(?:" + RecoveryRoleLabels.Pattern + ")[：:]|^(?:" + RecoveryRoleLabels.Pattern + ")(?=[^：:])";
    private static string OcrPublicLabelPattern => "(?:" + HeaderPattern + ")[：:]?|" + OcrRolePrefixPattern;
    private static bool CanEmitOcrLabel(IReadOnlyList<PdfGlyph> row, Action<long> charge)
    {
        var raw = string.Concat(row.Select(g => g.Text)); charge(row.Count + raw.Length);
        if (Regex.IsMatch(PdfGrid.Key(raw), OcrWholeLabelPattern)) return true;
        foreach (Match match in Regex.Matches(raw, OcrPublicLabelPattern))
        {
            // Use the same whole-atom boundaries as output extraction. A match
            // inside one atom cannot acquire evidence by pre-screening it.
            var offset = 0; var text = new System.Text.StringBuilder();
            foreach (var glyph in row)
            {
                charge(1); var end = offset + glyph.Text.Length;
                if (offset >= match.Index && end <= match.Index + match.Length) text.Append(glyph.Text);
                offset = end;
            }
            if (text.Length > 0 && text.ToString() == match.Value) return true;
        }
        return false;
    }
    private static IEnumerable<Label> Labels(IReadOnlyList<Atom> atoms, IReadOnlyList<PdfPageLayout> pages,
        IReadOnlySet<int>? ocrPages, Work work,CancellationToken token)
    {
        foreach (var row in atoms.GroupBy(a => a.Page))
        {
            var grid=ocrPages?.Contains(row.Key)==true ? null : new PdfGrid(pages[row.Key-1],token);
            var vertical=grid is null ? [] : pages[row.Key-1].Lines.Where(l=>l.Vertical).OrderBy(l=>l.X1).ToArray();
            bool Boundary(PdfGlyph previous,PdfGlyph current)
            {
                var lo=0;var hi=vertical.Length;
                while(lo<hi) {work.Step();var mid=lo+(hi-lo)/2;if(vertical[mid].X1<=previous.Cx)lo=mid+1;else hi=mid;}
                for(var i=lo;i<vertical.Length && vertical[i].X1<current.Cx;i++)
                { work.Step();if(vertical[i].Y1<=Math.Min(previous.Cy,current.Cy) && vertical[i].Y2>=Math.Max(previous.Cy,current.Cy))return true; }
                return false;
            }
            var owners=new Dictionary<PdfGlyph,PdfBox?>(ReferenceEqualityComparer.Instance);
            PdfBox? Owner(PdfGlyph glyph)
            {
                if(grid is null) return null;
                if(owners.TryGetValue(glyph,out var owner))return owner;
                work.Step(pages[row.Key-1].Lines.Count*4L);
                try { owner=grid.Box(glyph.Cx,glyph.Cy); }
                catch(PdfParseException failure) when(failure.Stage!="limit") { owner=null; }
                owners.Add(glyph,owner);return owner;
            }
            var byGlyph = new Dictionary<PdfGlyph, Atom>(ReferenceEqualityComparer.Instance);
            foreach (var atom in row)
            {
                work.Step();
                if (!byGlyph.TryAdd(atom.Glyph, atom)) throw new InvalidDataException("原文の文字位置が重複しています。");
            }
            foreach (var band in PdfGrid.Rows(row.Select(a => a.Glyph)))
            {
                var pieces = new List<List<Atom>>();
                foreach (var glyph in band)
                {
                    work.Step(); var atom = byGlyph[glyph];
                    if (pieces.Count == 0 || glyph.X - (pieces[^1][^1].Box.X + pieces[^1][^1].Box.Width) > Math.Max(2, glyph.Height * .7) ||
                        Boundary(pieces[^1][^1].Glyph,glyph) && Owner(pieces[^1][^1].Glyph) is {} previous && Owner(glyph) is {} current && previous!=current) pieces.Add([]);
                    pieces[^1].Add(atom);
                }
                foreach (var piece in pieces)
                {
                    // Retain real per-character geometry; do not invent sub-boxes for an OCR line.
                    var text = PdfGrid.Key(string.Concat(piece.Select(a => a.Glyph.Text)));
                    yield return new(text, row.Key, Bounds(piece), piece.Select(a => a.Id).ToArray());
                    var raw = string.Concat(piece.Select(a => a.Glyph.Text));
                    foreach (Match match in Regex.Matches(raw, "(?:" + HeaderPattern + ")[：:]?|(?:" + RecoveryRoleLabels.Pattern + ")[：:]"))
                    {
                        var offset = 0; var selected = new List<Atom>();
                        foreach (var atom in piece) { work.Step(); var end = offset + atom.Glyph.Text.Length; if (offset >= match.Index && end <= match.Index + match.Length) selected.Add(atom); offset = end; }
                        if (selected.Count > 0 && string.Concat(selected.Select(a => a.Glyph.Text)) == match.Value && match.Value != text) yield return new(match.Value.TrimEnd(':', '：'), row.Key, Bounds(selected), selected.Select(a => a.Id).ToArray());
                    }
                }
            }
        }
    }
    private static IEnumerable<Label> OcrLineLabels(IReadOnlyList<Atom> atoms, IReadOnlyList<PdfPageLayout> pages,
        IReadOnlySet<int> ocrPages, Work work)
    {
        foreach (var page in atoms.GroupBy(a => a.Page))
        {
            if (!ocrPages.Contains(page.Key)) continue;
            var pageAtoms = page.ToArray(); work.Step(pageAtoms.Length * 2L);
            var byGlyph = new Dictionary<PdfGlyph, Atom>(ReferenceEqualityComparer.Instance);
            foreach (var atom in pageAtoms) byGlyph.Add(atom.Glyph, atom);
            foreach (var nativeRow in PdfGrid.OcrHeaderRowsWhere(pages[page.Key - 1], pageAtoms.Select(a => a.Glyph).ToArray(), work.Step,
                row => CanEmitOcrLabel(row, work.Step)))
            {
                var line = nativeRow.Select(g => byGlyph[g]).ToArray();
                var raw = string.Concat(line.Select(a => a.Glyph.Text)); work.Step(raw.Length);
                // A complete, independently validated native line beginning
                // with exactly one explicit role prefix proves its remaining
                // ORIGINAL atoms are that field's literal continuation. CTC
                // peak gaps cannot turn a word inside it into another label.
                var rolePrefixes = Regex.Matches(raw, OcrRolePrefixPattern);
                // Accept only complete existing day/class syntax; never infer
                // a date or class from a substring or repair a native atom.
                var key = PdfGrid.Key(raw);
                if (Regex.IsMatch(key, OcrWholeLabelPattern))
                    yield return new(key, page.Key, Bounds(line), line.Select(a => a.Id).ToArray(), WholeOcrRow: true);
                // Only the existing public header/role patterns, using whole
                // original atoms. A native line is not an arbitrary label.
                foreach (Match match in Regex.Matches(raw, OcrPublicLabelPattern))
                {
                    var offset = 0; var selected = new List<Atom>();
                    foreach (var atom in line)
                    { work.Step(); var end = offset + atom.Glyph.Text.Length; if (offset >= match.Index && end <= match.Index + match.Length) selected.Add(atom); offset = end; }
                    if (selected.Count > 0 && string.Concat(selected.Select(a => a.Glyph.Text)) == match.Value)
                    {
                        // A decoded original whitespace gap between two
                        // independently present public headers belongs to the
                        // preceding literal header. Keep its source atom and
                        // native score so existing header review can confirm it.
                        // A body gap, unknown suffix or isolated space gains no
                        // header meaning from this rule.
                        if (Regex.IsMatch(match.Value, "^(?:" + HeaderPattern + ")[：:]?$"))
                        {
                            var offsetAfter = match.Index + match.Length;
                            var gap = new List<Atom>(); var offsetInLine = 0;
                            foreach (var atom in line)
                            {
                                work.Step(); var end = offsetInLine + atom.Glyph.Text.Length;
                                if (offsetInLine >= offsetAfter)
                                {
                                    if (string.IsNullOrEmpty(atom.Glyph.Text) || !string.IsNullOrWhiteSpace(atom.Glyph.Text)) break;
                                    gap.Add(atom); offsetAfter = end;
                                }
                                offsetInLine = end;
                            }
                            if (gap.Count > 0 && offsetAfter < raw.Length &&
                                Regex.IsMatch(raw[offsetAfter..], "^(?:" + HeaderPattern + ")(?:[：:]|$)"))
                                selected.AddRange(gap);
                        }
                        var valueIds = rolePrefixes.Count == 1 && rolePrefixes[0].Index == 0 && match.Index == 0 &&
                            match.Value == rolePrefixes[0].Value ? line.Skip(selected.Count).Select(a => a.Id).ToArray() : null;
                        work.Step(valueIds?.Length ?? 0);
                        yield return new(match.Value.TrimEnd(':', '：'), page.Key, Bounds(selected), selected.Select(a => a.Id).ToArray(), NativeValueIds: valueIds);
                    }
                }
            }
        }
    }
    private static IEnumerable<Label> NativeHeaderLabels(IReadOnlyList<Atom> atoms,Work work)
    {
        foreach(var group in atoms.Where(a=>a.Glyph.SourceLine is not null).GroupBy(a=>(a.Page,a.Glyph.SourceLine)))
        {
            var original=group.OrderBy(a=>a.Glyph.SourceOrder).ToArray();
            work.Step(original.Length+original.Sum(a=>(long)a.Glyph.Text.Length));
            var pieces=original.Select(a=>(Atom:a,Text:PdfGrid.Key(a.Glyph.Text))).ToArray();
            var text=string.Concat(pieces.Select(p=>p.Text));
            // Match normalized syntax while retaining complete ORIGINAL atoms.
            // Never create a sub-glyph rectangle or normalize source text.
            if(text!=PdfGrid.Key(string.Concat(original.Select(a=>a.Glyph.Text))))continue;
            foreach(System.Text.RegularExpressions.Match match in Regex.Matches(text,HeaderPattern))
            {
                var selected=new List<Atom>();var at=0;
                foreach(var piece in pieces)
                {
                    work.Step();var end=at+piece.Text.Length;
                    if(at>=match.Index && end<=match.Index+match.Length && piece.Text.Length>0)selected.Add(piece.Atom);
                    at=end;
                }
                if(selected.Count>0 && string.Concat(selected.Select(a=>PdfGrid.Key(a.Glyph.Text)))==match.Value)
                    yield return new(match.Value,group.Key.Page,Bounds(selected),selected.Select(a=>a.Id).ToArray());
            }
        }
    }
    private static RecoveryHeaderRegion? Region(Label label, RecoveryBox target)
    {
        if (label.OwnershipBox is { } ownership)
        {
            // Keep evidence bounds separate from ownership: a merged grade rail
            // supplies each class's grade evidence, while its own physical row
            // owns the body. Periods retain their original printed token bounds
            // even when several tokens share one expanded physical header cell.
            var above = ownership.Y + ownership.Height <= target.Y &&
                Math.Min(ownership.X + ownership.Width, target.X + target.Width) > Math.Max(ownership.X, target.X);
            var left = ownership.X + ownership.Width <= target.X &&
                Math.Min(ownership.Y + ownership.Height, target.Y + target.Height) > Math.Max(ownership.Y, target.Y);
            if (!above && !left) return null;
        }
        var horizontal = Math.Min(label.Box.X + label.Box.Width, target.X + target.Width) > Math.Max(label.Box.X, target.X);
        var vertical = Math.Min(label.Box.Y + label.Box.Height, target.Y + target.Height) > Math.Max(label.Box.Y, target.Y);
        if (label.Box.Y + label.Box.Height <= target.Y && horizontal) return new(label.Page, label.Box, RecoveryHeaderAxis.Above);
        if (label.Box.X + label.Box.Width <= target.X && vertical) return new(label.Page, label.Box, RecoveryHeaderAxis.Left);
        return null;
    }
    private static string? Day(string value, RecoveryDocumentKind kind, int year)
    {
        value=PdfGrid.Key(value);
        if (kind == RecoveryDocumentKind.Timetable && value.StartsWith('(') && value.EndsWith(')')) value=value[1..^1];
        if (kind == RecoveryDocumentKind.Timetable) return value switch { "月" or "月曜" or "月曜日" => "1", "火" or "火曜" or "火曜日" => "2", "水" or "水曜" or "水曜日" => "3", "木" or "木曜" or "木曜日" => "4", "金" or "金曜" or "金曜日" => "5", _ => null };
        const string weekday=@"(?:\(([日月火水木金土])(?:曜(?:日)?)?\))?$";
        var m = Regex.Match(value, @"^(?:([0-9]{4})[-/])?([0-9]{1,2})[-/]([0-9]{1,2})"+weekday);
        if (!m.Success) m = Regex.Match(value, @"^(?:([0-9]{4})年)?([0-9]{1,2})月([0-9]{1,2})日"+weekday);
        if (!m.Success) return null;
        var month = int.Parse(m.Groups[2].Value); var y = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : year + (month < 4 ? 1 : 0);
        try {
            var date=new DateOnly(y,month,int.Parse(m.Groups[3].Value));
            if(m.Groups[4].Success && m.Groups[4].Value!="日月火水木金土"[(int)date.DayOfWeek].ToString()) return null;
            return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        } catch (ArgumentOutOfRangeException) { return null; }
    }
    // Preserve the complete ORIGINAL order of an independently proved inline
    // role row. A shorter specialist crop can have a different vertical centre;
    // rounding those centres must not move its tail before the Japanese prefix.
    // Raw SourceLine metadata alone never authorizes this ordering.
    private static void PreserveProvedOcrContinuationOrder(Atom[] atoms, IEnumerable<Label> native, Work work)
    {
        work.Step(atoms.Length * 2L);
        var positions = atoms.Select((atom,index)=>(atom.Id,index)).ToDictionary(p=>p.Id,p=>p.index);
        var original = atoms.ToDictionary(a=>a.Id);
        foreach (var label in native.Where(l=>l.NativeValueIds is not null))
        {
            var ids = label.Ids.Concat(label.NativeValueIds!).ToArray();
            work.Step(ids.Length * 3L);
            var slots = ids.Select(id=>positions[id]).Order().ToArray();
            for (var i=0;i<slots.Length;i++) atoms[slots[i]]=original[ids[i]];
        }
    }

    // Diagnostic source-only replay of the SAME ordering proof used by Build.
    // No pixels, recognition, lookup values or source edits enter this helper.
    internal static IReadOnlyList<string> OriginalOcrSourceOrder(IReadOnlyList<PdfPageLayout> pages,
        CancellationToken token = default)
    {
        var work = new Work(token);
        var atoms = pages.SelectMany((p,i)=>p.Glyphs.Select((g,n)=>new Atom($"p{i+1}s{n}",i+1,g)))
            .OrderBy(a=>a.Page).ThenBy(a=>Math.Round(a.Glyph.Cy/2)).ThenBy(a=>a.Glyph.Cx).ToArray();
        if (atoms.Length>100000) throw RecoveryWorkLimits.Exceeded("OCR原文の順序比較数が上限を超えています。");
        var native = OcrLineLabels(atoms,pages,Enumerable.Range(1,pages.Count).ToHashSet(),work).ToArray();
        PreserveProvedOcrContinuationOrder(atoms,native,work);
        return atoms.Select(a=>a.Id).ToArray();
    }

    public static RecoveryDocument Build(string hash, MaterialKind materialKind, IReadOnlyList<PdfPageLayout> pages,
        Func<int, RecoveryBox, bool> inkFree, IReadOnlySet<int>? ocrPages = null, CancellationToken token = default, bool allowStructureProposal = false,bool allowNativeReferences = false)
    {
        token.ThrowIfCancellationRequested(); var work = new Work(token);
        var kind = RecoveryPolicy.Kind(materialKind) ?? throw new InvalidDataException("PDF復旧の対象外です。");
        if (pages.Count is < 1 or > 12) throw new InvalidDataException("PDFのページ数が上限を超えています。");
        var atoms = pages.SelectMany((p, i) => p.Glyphs.Select((g, n) => new Atom($"p{i + 1}s{n}", i + 1, g))).ToArray();
        if (atoms.Length > 100000 || atoms.Any(a => !a.Box.Valid)) throw new InvalidDataException("文字の位置を確認できません。");
        atoms = atoms.OrderBy(a => a.Page).ThenBy(a => Math.Round(a.Glyph.Cy / 2)).ThenBy(a => a.Glyph.Cx).ToArray();
        var labels = Labels(atoms,pages,ocrPages,work,token).ToArray();
        if(allowNativeReferences && ocrPages is not {Count:>0})
        {
            (int,string,string) Key(Label label) => (label.Page,PdfGrid.Key(label.Value),string.Join(',',label.Ids));
            var seen=labels.Select(Key).ToHashSet();
            labels=labels.Concat(NativeHeaderLabels(atoms,work).Where(label=>seen.Add(Key(label)))).ToArray();
        }
        if (ocrPages is { Count: > 0 })
        {
            (int, string, string) Key(Label label)
            { work.Step(label.Ids.Count); return (label.Page, label.Value, string.Join(',', label.Ids)); }
            var seen = labels.Select(Key).ToHashSet();
            var native = OcrLineLabels(atoms, pages, ocrPages, work).ToArray();
            PreserveProvedOcrContinuationOrder(atoms,native,work);
            var contexts = native.Where(l => l.NativeValueIds is not null).ToDictionary(Key);
            labels = labels.Select(l => contexts.TryGetValue(Key(l), out var context) ? l with { NativeValueIds = context.NativeValueIds } : l)
                .Concat(native.Where(label => seen.Add(Key(label)))).ToArray();
        }
        var years = labels.Where(l => Regex.IsMatch(PdfGrid.Key(l.Value), @"^(?:[0-9]{4}|令和[0-9]{1,2})年度$")).ToArray();
        var yearValues = years.Select(l=>PdfGrid.Key(l.Value)).Select(value => value.StartsWith("令和", StringComparison.Ordinal) ? 2018 + int.Parse(value[2..^2]) : int.Parse(value[..4])).Distinct().ToArray();
        if (yearValues.Length != 1) throw new InvalidDataException("年度の独立した見出しがありません。");
        var year = yearValues[0];
        var terms = labels.Where(l => l.Value is "前期" or "後期").ToArray();
        string? term = kind == RecoveryDocumentKind.Timetable && terms.Select(l => l.Value).Distinct().Count() == 1 ? terms[0].Value : null;
        if (kind == RecoveryDocumentKind.Timetable && term is null) throw new InvalidDataException("学期の見出しを確認できません。");
        Label Expand(Label label)
        {
            try
            {
                work.Step(pages[label.Page - 1].Lines.Count * 4L);
                var b = new PdfGrid(pages[label.Page - 1], token).Box(label.Box.X + label.Box.Width / 2, label.Box.Y + label.Box.Height / 2);
                var physical = new RecoveryBox(b.Left, b.Top, b.Right-b.Left, b.Bottom-b.Top);
                if (ocrPages?.Contains(label.Page) == true && !physical.Contains(label.Box))
                {
                    // CTC preserves original recognition context outside a
                    // physical header. Prove its fringe empty from original
                    // pixels, retaining source geometry and physical ownership.
                    var context=OcrHeaderContext.Extend(physical,label.Box,f=>{work.Step();return inkFree(label.Page,f);});
                    if(context is not null)return label with{Box=context,OwnershipBox=label.OwnershipBox??physical};
                }
                return label with { Box = physical };
            }
            catch (PdfParseException error) when (error.Stage != "limit") { return label; }
        }
        var legacyClassLabels = new List<Label>();
        foreach (var dept in labels.Where(l => Regex.IsMatch(l.Value, "^(?:[1-3]|CN|ES|IT)$")))
        {
            work.Step(atoms.Length); var page = pages[dept.Page - 1];
            var periodRows = PdfGrid.Rows(page.Glyphs).Where(r => Regex.IsMatch(PdfGrid.Key(string.Concat(r.Select(g => g.Text))), "^(?:12345678){5}$|^(?:123456){2,3}$")).ToArray();
            if (periodRows.Length != 1 || dept.Box.X >= periodRows[0].Min(g => g.X) || dept.Box.Y <= periodRows[0].Max(g => g.Y + g.Height)) continue;
            try
            {
                work.Step(page.Lines.Count * 8L + atoms.Length * 2L);
                var grid = new PdfGrid(pages[dept.Page - 1], token); var classBox = grid.Box(dept.Box.X + dept.Box.Width / 2, dept.Box.Y + dept.Box.Height / 2);
                var gradeBox = grid.Box(classBox.Left - 2, dept.Box.Y + dept.Box.Height / 2);
                var gradeAtoms = atoms.Where(a => a.Page == dept.Page && a.Glyph.Cx > gradeBox.Left && a.Glyph.Cx < gradeBox.Right && a.Glyph.Cy > gradeBox.Top && a.Glyph.Cy < gradeBox.Bottom).ToArray();
                var grade = PdfGrid.Key(string.Concat(gradeAtoms.Select(a => a.Glyph.Text))); var cls = grade + "_" + dept.Value;
                if (!ClassSelection.Candidates.Contains(cls)) continue;
                // A merged class rail can begin at a small grouping-header
                // row. Its body ownership begins at the actual period cell
                // containing this independently printed class label.
                var bodyTop=grid.Box(periodRows[0][0].Cx,dept.Box.Y+dept.Box.Height/2).Top;
                var ids = gradeAtoms.Select(a => a.Id).Concat(dept.Ids).ToHashSet();
                var box = new RecoveryBox(gradeBox.Left, Math.Min(gradeBox.Top, classBox.Top), classBox.Right - gradeBox.Left, Math.Max(gradeBox.Bottom, classBox.Bottom) - Math.Min(gradeBox.Top, classBox.Top));
                legacyClassLabels.Add(new(cls, dept.Page, box, atoms.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToArray(),
                    new(classBox.Left, Math.Max(classBox.Top,bodyTop), classBox.Right - classBox.Left, classBox.Bottom - Math.Max(classBox.Top,bodyTop))));
            }
            catch (PdfParseException error) when (error.Stage != "limit") { }
        }
        var maxPeriod = kind == RecoveryDocumentKind.Exam ? 6 : 8;
        var periodLabels = labels.Select(l => (Label: l, Match: Regex.Match(l.Value, @"^(?:第)?([1-8])(?:限|時限)?$"))).Where(p => p.Match.Success && int.Parse(p.Match.Groups[1].Value) <= maxPeriod).Select(p => Expand(p.Label) with { Value = p.Match.Groups[1].Value, OwnershipBox = p.Label.Box }).ToArray();
        var printedClasses = labels.Where(l => ClassSelection.Candidates.Contains(l.Value.Replace('-', '_'))).ToArray();
        var ambiguousClassHeaders = new HashSet<string>(StringComparer.Ordinal);
        var ambiguousDateHeaders = new HashSet<string>(StringComparer.Ordinal);
        if (kind == RecoveryDocumentKind.Exam)
        {
            foreach (var label in printedClasses.Where(l => Day(l.Value, kind, year) is not null))
            {
                var header = Expand(label);
                work.Step(periodLabels.Length * 8L + label.Ids.Count);
                // A hyphenated class and a real month-day can have identical text.
                // The class rail owns the adjacent, horizontally contained complete
                // period band; a date in the left body rail does not. Use original
                // physical boundaries and IDs, never a global textual exclusion.
                var owned = periodLabels.Where(p => p.Page == header.Page && p.Box.Y == header.Box.Y + header.Box.Height &&
                    p.Box.X >= header.Box.X && p.Box.X + p.Box.Width <= header.Box.X + header.Box.Width).ToArray();
                var isClass = owned.Select(p => p.Value).Distinct().Order().SequenceEqual(Enumerable.Range(1, maxPeriod).Select(p => p.ToString(CultureInfo.InvariantCulture))) &&
                    owned.Min(p => p.Box.X) == header.Box.X && owned.Max(p => p.Box.X + p.Box.Width) == header.Box.X + header.Box.Width;
                (isClass ? ambiguousClassHeaders : ambiguousDateHeaders).UnionWith(label.Ids);
            }
        }
        Label BindAiGrade(Label header)
        {
            // The strict Exam format prints 1年/2年. Recovery requires the
            // canonical class as well, independent of page order or count.
            if (kind != RecoveryDocumentKind.Exam || header.Value is not ("AI_1" or "AI_2")) return header;
            work.Step(labels.Length * 2L + printedClasses.Length + periodLabels.Length * 3L + atoms.Length);
            var grades = labels.Where(l => l.Page == header.Page && Regex.IsMatch(l.Value, "^[12]年$") && Expand(l).Box == header.Box).ToArray();
            var canonical = printedClasses.Where(l => l.Page == header.Page && Expand(l).Box == header.Box).ToArray();
            var owned = periodLabels.Where(p => p.Page == header.Page && p.Box.Y == header.Box.Y + header.Box.Height &&
                p.Box.X >= header.Box.X && p.Box.X + p.Box.Width <= header.Box.X + header.Box.Width).ToArray();
            var band = owned.Select(p => p.Box).Distinct().OrderBy(b => b.X).ToArray();
            if (grades.Length != 1 || grades[0].Value != header.Value[^1] + "年" || canonical.Length != 1 ||
                owned.Length != 6 || !owned.Select(p => p.Value).Order().SequenceEqual(new[] { "1", "2", "3", "4", "5", "6" }) ||
                band[0].X != header.Box.X || band[^1].X + band[^1].Width != header.Box.X + header.Box.Width ||
                band.Zip(band.Skip(1)).Any(pair => pair.First.X + pair.First.Width != pair.Second.X)) return header;
            var ids = header.Ids.Concat(grades[0].Ids).ToHashSet(StringComparer.Ordinal);
            return header with { Ids = atoms.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToArray() };
        }
        if(allowNativeReferences && kind==RecoveryDocumentKind.Exam && pages.Count==6)
        {
            var regular=printedClasses.Select(l=>l.Value.Replace('-','_')).ToHashSet();
            var expected=RecoveryValidator.SpecialClasses.Where(c=>!c.StartsWith("AI_",StringComparison.Ordinal)).ToHashSet();
            var grades=labels.Where(l=>l.Value is "1年" or "2年").ToArray();
            if(regular.SetEquals(expected) && grades.Length==2 && grades.Select(l=>l.Page).Distinct().Count()==1 && grades.Select(l=>l.Value).Distinct().Count()==2 &&
                grades.SelectMany(l=>l.Ids).All(id=>atoms.Single(a=>a.Id==id).Glyph.InkBounds is not null))
                legacyClassLabels.AddRange(grades.Select(l=>Expand(l) with {Value="AI_"+l.Value[0]}));
        }
        var classLabels = printedClasses.Where(l => !l.Ids.Any(ambiguousDateHeaders.Contains)).Select(l => BindAiGrade(Expand(l) with { Value = l.Value.Replace('-', '_') })).Concat(legacyClassLabels).ToArray();
        var dayLabels = labels.Where(l => !l.Ids.Any(ambiguousClassHeaders.Contains)).Select(l => (Label: l, Day: Day(l.Value, kind, year))).Where(p => p.Day is not null).Select(p => Expand(p.Label) with { Value = p.Day! }).ToArray();
        // A gap-split weekday such as 月 must not hide the intact 月曜日 from
        // the same validated row and physical header owner. Independent labels
        // or labels in different owners remain separate evidence candidates.
        var wholeDays = dayLabels.Where(l => l.WholeOcrRow).ToArray();
        dayLabels = dayLabels.Where(label =>
        {
            foreach (var whole in wholeDays)
            {
                work.Step((long)label.Ids.Count * whole.Ids.Count + whole.Ids.Count);
                if (label.Page == whole.Page && label.Value == whole.Value && label.Box == whole.Box
                    && label.Ids.Count < whole.Ids.Count && label.Ids.All(whole.Ids.Contains)) return false;
            }
            return true;
        }).ToArray();
        // Numeric grade/department atoms are already independently owned class
        // evidence. They cannot also become nearby left-side period alternatives.
        work.Step(classLabels.Sum(l => (long)l.Ids.Count) + periodLabels.Length);
        var classHeaderIds = classLabels.SelectMany(l => l.Ids).ToHashSet(StringComparer.Ordinal);
        periodLabels = periodLabels.Where(l => !l.Ids.Any(classHeaderIds.Contains)).ToArray();
        if (classLabels.Length == 0 || dayLabels.Length == 0 || periodLabels.Length == 0)
        {
            var failure=new InvalidDataException("クラス・日付・時限の見出しを確認できません。");
            failure.Data["RecoveryHeaderCounts"]=new { Classes=classLabels.Length,Days=dayLabels.Length,Periods=periodLabels.Length };
            throw failure;
        }
        var trustedNormal = new Dictionary<int, TimetableAnalysis>(); SpecialAnalysis? trustedSpecial = null;
        if (kind == RecoveryDocumentKind.Timetable)
        {
            foreach (var (page, index) in pages.Select((p, i) => (p, i + 1))) try { trustedNormal[index] = PdfScheduleParser.Timetable([page], token); } catch (PdfParseException error) when (error.Stage != "limit") { }
        }
        else try { trustedSpecial = PdfScheduleParser.Special(pages, materialKind, token, ocrPages,allowNativeTwoRowExam:allowNativeReferences); } catch (PdfParseException error) when (error.Stage != "limit") { }
        var sources = atoms.ToDictionary(a => a.Id, a => new RecoverySource(a.Id, "header", a.Page, a.Glyph.Text, a.Box, ocrPages?.Contains(a.Page) == true, a.Glyph.SourceLine, a.Glyph.SourceOrder) { FontEvidence = a.Glyph.FontEvidence, RequiresVisualReview = a.Glyph.RequiresVisualReview,NativeLayoutBox=a.Glyph.InkBounds is null ? null : new(a.Glyph.X,a.Glyph.Y,a.Glyph.Width,a.Glyph.Height) });
        var cells = new List<RecoveryCell>();
        // Retain the original order within each page while avoiding repeated
        // comparisons with every other page's original text and headers.
        work.Step(atoms.Length + labels.Length + classLabels.Length + dayLabels.Length + periodLabels.Length);
        var atomsByPage = atoms.GroupBy(a => a.Page).ToDictionary(g => g.Key, g => g.ToArray());
        var labelsByPage = labels.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.ToArray());
        var classesByPage = classLabels.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.ToArray());
        var daysByPage = dayLabels.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.ToArray());
        var periodsByPage = periodLabels.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.ToArray());
        var usedSlots = new HashSet<RecoverySlot>();
        var usedClass = new Dictionary<string, HashSet<string>>(); var usedDay = new Dictionary<string, HashSet<string>>(); var usedPeriod = new Dictionary<string, HashSet<string>>();
        void Register(Dictionary<string, HashSet<string>> inventory, string key, IEnumerable<string> ids) { if (!inventory.TryGetValue(key, out var set)) inventory[key] = set = []; set.UnionWith(ids); }
        foreach (var (page, pi) in pages.Select((p, i) => (p, i + 1)))
        {
            token.ThrowIfCancellationRequested();
            var grid = new PdfGrid(page, token);
            var pageAtoms = atomsByPage.GetValueOrDefault(pi, []); var pageLabels = labelsByPage.GetValueOrDefault(pi, []);
            var atomIndex = new RecoveryContainmentIndex<Atom>(pageAtoms, atom => atom.Box, work.Step);
            var labelIndex = new RecoveryContainmentIndex<Label>(pageLabels, label => label.Box, work.Step);
            var pageClasses = classesByPage.GetValueOrDefault(pi, []); var pageDays = daysByPage.GetValueOrDefault(pi, []);
            var pagePeriods = periodsByPage.GetValueOrDefault(pi, []);
            work.Step(pageClasses.Length + pageDays.Length + pagePeriods.Length);
            var headers = pageClasses.Concat(pageDays).Concat(pagePeriods).ToArray();
            var xs = page.Lines.Where(l => l.Vertical).Select(l => l.X1).Distinct().Order().ToArray();
            var ys = page.Lines.Where(l => l.Horizontal).Select(l => l.Y1).Distinct().Order().ToArray();
            if (xs.Length * (long)ys.Length > 250000) throw new InvalidDataException("表の罫線が解析上限を超えています。");
            var boxes = new HashSet<RecoveryBox>();
            foreach (var (left, right) in xs.Zip(xs.Skip(1)))
            foreach (var (top, bottom) in ys.Zip(ys.Skip(1)))
            {
                if (right - left < 4 || bottom - top < 4) continue;
                work.Step(page.Lines.Count * 4L);
                try { var b = grid.Box((left + right) / 2, (top + bottom) / 2); boxes.Add(new(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top)); }
                catch (PdfParseException error) when (error.Stage != "limit") { }
                if (boxes.Count > 20000) throw new InvalidDataException("表の候補数が上限を超えています。");
            }
            foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X))
            {
                work.Step(headers.Length);
                // Header cells are not timetable body cells.
                if (headers.Any(l => box.Contains(l.Box))) continue;
                Label? Closest(IEnumerable<Label> candidates) => candidates.Where(l => Region(l, box) is not null).OrderBy(l => Region(l, box)!.Axis == RecoveryHeaderAxis.Above ? box.Y - l.Box.Y - l.Box.Height : box.X - l.Box.X - l.Box.Width).FirstOrDefault();
                var cls = Closest(allowNativeReferences ? pageClasses.Where(l=>Region(l,box)?.Axis==(kind==RecoveryDocumentKind.Exam ? RecoveryHeaderAxis.Above : RecoveryHeaderAxis.Left)) : pageClasses);
                var day = Closest(allowNativeReferences ? pageDays.Where(l=>Region(l,box)?.Axis==(kind==RecoveryDocumentKind.Exam ? RecoveryHeaderAxis.Left : RecoveryHeaderAxis.Above)) : pageDays);
                if (cls is null || day is null) continue;
                var period = pagePeriods.Where(l => Region(l, box) is not null).GroupBy(l => l.Value).Select(g => Closest(g)!).Where(l => l is not null).ToArray();
                if (period.Length == 0) continue;
                // Multiple left headers in one column are alternatives, not a merged span.
                var chosenPeriods = period.Where(l => Region(l, box)!.Axis == RecoveryHeaderAxis.Above).ToArray();
                if (chosenPeriods.Length == 0) chosenPeriods = [Closest(period)!];
                var slots = chosenPeriods.OrderBy(l => int.Parse(l.Value)).Select(l => new RecoverySlot(cls.Value, day.Value, int.Parse(l.Value))).ToArray();
                work.Step(slots.Length * 2L); // Membership and registration, not a scan of past cells.
                RecoveryCell? priorSlice=null;
                if (slots.Any(usedSlots.Contains))
                {
                    var overlapping=cells.Where(c=>c.Slots.Any(slots.Contains)).ToArray();
                    if(allowNativeReferences && overlapping.Length==1 && overlapping[0].Page==pi && overlapping[0].Slots.SequenceEqual(slots) &&
                        overlapping[0].Box.X==box.X && overlapping[0].Box.Width==box.Width && Math.Abs(overlapping[0].Box.Y+overlapping[0].Box.Height-box.Y)<1e-7)
                        priorSlice=overlapping[0];
                    else {
                    var failure=new InvalidDataException("時間割の同じ位置に複数のセル候補があります。");
                    failure.Data["CurrentCandidateBox"]=box;
                    failure.Data["PreviousCandidateBox"]=cells.First(c=>c.Slots.Any(slots.Contains)).Box;
                    throw failure;
                    }
                }
                var id = $"p{pi}c{cells.Count}";
                var inside = atomIndex.Contained(box);
                var empty = inside.Length == 0 && inkFree(pi, box);
                if (inside.Length == 0 && !empty) throw new InvalidDataException("文字を読めなかったセルを空欄として扱えません。");
                // Original OCR body markers are unresolved structure, not an
                // inferred parallel separator or a compound-name exception.
                if (ocrPages?.Contains(pi) == true)
                {
                    work.Step(inside.Sum(a => (long)a.Glyph.Text.Length));
                    if (inside.Any(a => RecoveryValidator.OcrBodySeparatorAmbiguous(sources[a.Id])))
                    {
                        var error = new InvalidDataException("OCR原文に解釈を確定できない区切り文字があります。");
                        error.Data["RecoveryErrorKind"] = "sourceContractRefusal";
                        error.Data["RecoveryErrorCode"] = "ocrSeparatorAmbiguity";
                        throw error;
                    }
                }
                IReadOnlyList<RecoveryRoleScope> scopes = []; IReadOnlyList<RecoveryLessonBinding> fixedBindings = [];
                RecoveryOrderedRowProof? orderedProof = null;var nativeMapped=false;
                IReadOnlyDictionary<string, string>? separators = null; var fixedBlanks = new HashSet<string>();
                if (!empty)
                {
                    var insideLabels = labelIndex.Contained(box);
                    work.Step(insideLabels.Length * 3L + inside.Length * 24L);
                    try { scopes = RoleScopes(id, pi, box, inside, insideLabels, inkFree, work); }
                    catch (InvalidDataException error) when (!RecoveryWorkLimits.IsExceeded(error))
                    {
                        LessonNames? trustedNames = null; IReadOnlyList<LessonNames>? trustedTuples = null;
                        if (trustedNormal.TryGetValue(pi, out var normal)) { trustedTuples = TrustedNormalTuples(normal, slots, work.Step); trustedNames = trustedTuples is { Count: 1 } ? trustedTuples[0] : null; }
                        else if (trustedSpecial is not null) { var matched = trustedSpecial.Lessons.Where(l => l.Page == pi && l.ClassName == cls.Value && l.Date == day.Value && l.Period == slots.Min(s => s.Period)).ToArray(); if (matched.Length == 1) trustedNames = matched[0].Names; }
                        var rows = PdfGrid.Rows(inside.Select(a => a.Glyph));
                        var text = rows.Select(r => PdfGrid.Key(string.Concat(r.Select(g => g.Text)))).ToArray();
                        var nativeRows=allowNativeReferences ? grid.TimetableRows(new(box.X,box.Y,box.X+box.Width,box.Y+box.Height)) : [];
                        var nativeTexts=nativeRows.Select(row=>PdfGrid.Key(string.Concat(row.Select(g=>g.Text)))).ToArray();
                        var known=trustedTuples ?? (trustedSpecial is null ? [] : trustedSpecial.Lessons.Where(l=>l.Page==pi && l.ClassName==cls.Value && l.Date==day.Value && l.Period==slots.Min(s=>s.Period)).Select(l=>l.Names).ToArray());
                        var matching=known.Where(n=> {
                            var values=new[]{n.Subject,n.Teacher,n.Room};var roles=Enumerable.Range(0,3).Where(role=>values[role].Length>0).ToArray();
                            return roles.Length==nativeRows.Count && roles.Select((role,index)=> {
                                var raw=string.Concat(nativeRows[index].Select(g=>g.Text));
                                return PdfGrid.Key(role==2 ? PdfScheduleParser.CollapseRoomMarks(raw) : raw)==PdfGrid.Key(values[role]);
                            }).All(same=>same);
                        }).Distinct().ToArray();
                        if(allowNativeReferences && matching.Length==1 && nativeRows.SelectMany(row=>row).ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(inside.Select(a=>a.Glyph)))
                        {
                            var byGlyph=new Dictionary<PdfGlyph,string>(ReferenceEqualityComparer.Instance);
                            foreach(var atom in inside)byGlyph.Add(atom.Glyph,atom.Id);
                            var names=new[]{matching[0].Subject,matching[0].Teacher,matching[0].Room};var at=0;
                            var ids=names.Select((value,role)=> {
                                if(value.Length>0)return (IReadOnlyList<string>)nativeRows[at++].Select(g=>byGlyph[g]).ToArray();
                                if(role==0)throw new InvalidDataException("科目の原文を確認できません。");
                                fixedBlanks.Add(role==1 ? "teacher" : "room");return (IReadOnlyList<string>)Array.Empty<string>();
                            }).ToArray();
                            fixedBindings=[new(ids[0],ids[1],ids[2])];nativeMapped=true;
                            var room=string.Concat(ids[2].Select(sourceId=>sources[sourceId].Text));
                            if(room!=PdfScheduleParser.CollapseRoomMarks(room))
                                foreach(var sourceId in ids[2])sources[sourceId]=sources[sourceId] with {RequiresVisualReview=true};
                        }
                        else if (trustedNames is not null && rows.Count == 3 && text.SequenceEqual(new[] { trustedNames.Subject, trustedNames.Teacher, trustedNames.Room }.Select(PdfGrid.Key)))
                        {
                            var byGlyph = new Dictionary<PdfGlyph, string>(ReferenceEqualityComparer.Instance);
                            foreach (var a in inside) byGlyph.Add(a.Glyph, a.Id);
                            var bindings = rows.Select(r => (IReadOnlyList<string>)r.Select(g => byGlyph[g]).ToArray()).ToArray();
                            fixedBindings = [new(bindings[0], bindings[1], bindings[2])];
                        }
                        else if (kind == RecoveryDocumentKind.Timetable && slots.Length == 1 && rows.Count == 3 &&
                            rows.All(row => !RecoveryRoleLabels.HasPrefix(string.Concat(row.Select(g=>g.Text))) && !row.Any(g=>g.Text.Any(c=>"・･/／".Contains(c)))))
                        {
                            var byGlyph=new Dictionary<PdfGlyph,Atom>(ReferenceEqualityComparer.Instance);
                            foreach(var atom in inside)byGlyph.Add(atom.Glyph,atom);
                            var pieces=rows.Select(row=>(IReadOnlyList<RecoveryOrderedRowPiece>)row.Select(g=>new RecoveryOrderedRowPiece(g.Text,new(g.X,g.Y,g.Width,g.Height),g.SourceLine,g.SourceOrder)).ToArray()).ToArray();
                            var rowBoxes=rows.Select(row=>Bounds(row.Select(g=>byGlyph[g]))).ToArray();
                            if (!pieces.All(RecoveryOrderedRowProof.SingleRow) || rowBoxes.Zip(rowBoxes.Skip(1)).Any(p=>p.First.Y+p.First.Height>=p.Second.Y)) throw;
                            var ids=rows.Select(row=>(IReadOnlyList<string>)row.Select(g=>byGlyph[g].Id).ToArray()).ToArray();
                            fixedBindings=[new(ids[0],ids[1],ids[2])];
                            orderedProof=new(1,ids.SelectMany(row=>row).ToArray(),pieces);
                        }
                        else if (trustedTuples is { Count: 2 } && rows.Count == 3)
                        {
                            var proof = new Dictionary<string, string>();
                            var byGlyph = new Dictionary<PdfGlyph, string>(ReferenceEqualityComparer.Instance);
                            foreach (var a in inside) { work.Step(); byGlyph.Add(a.Glyph, a.Id); }
                            var parts = new IReadOnlyList<string>[2, 3]; var roles = new[] { "subject", "teacher", "room" };
                            for (var role = 0; role < 3; role++)
                            {
                                work.Step(rows[role].Count * 3L + rows[role].Sum(g => (long)g.Text.Length) * 3L);
                                var row = rows[role]; var cuts = row.Select((g, i) => (g, i)).Where(p => p.g.Text is "・" or "･").ToArray();
                                // Whole original atoms only; never carve an OCR line into characters.
                                if (cuts.Length != 1 || row.Any(g => g.Text is not ("・" or "･") && (g.Text.Contains('・') || g.Text.Contains('･')))) throw;
                                var cut = cuts[0]; proof.Add(roles[role], byGlyph[cut.g]);
                                for (var variant = 0; variant < 2; variant++)
                                {
                                    var original = variant == 0 ? row.Take(cut.i).ToArray() : row.Skip(cut.i + 1).ToArray();
                                    var value = string.Concat(original.Select(g => g.Text));
                                    var expected = role switch { 0 => trustedTuples[variant].Subject, 1 => trustedTuples[variant].Teacher, _ => trustedTuples[variant].Room };
                                    work.Step(value.Length + expected.Length);
                                    if (PdfGrid.Key(value) != PdfGrid.Key(expected)) throw;
                                    if (original.Length == 0)
                                    {
                                        if (role == 0) throw;
                                        var centers = rows.Select(r => r.Average(g => g.Cy)).ToArray();
                                        var top = role == 0 ? box.Y : (centers[role - 1] + centers[role]) / 2;
                                        var bottom = role == 2 ? box.Y + box.Height : (centers[role] + centers[role + 1]) / 2;
                                        var left = variant == 0 ? box.X : cut.g.X + cut.g.Width;
                                        var right = variant == 0 ? cut.g.X : box.X + box.Width;
                                        if (right <= left || bottom <= top || !inkFree(pi, new(left, top, right - left, bottom - top))) throw;
                                        fixedBlanks.Add(roles[role]);
                                    }
                                    parts[variant, role] = original.Select(g => byGlyph[g]).ToArray();
                                }
                            }
                            separators = proof;
                            fixedBindings = [new(parts[0, 0], parts[0, 1], parts[0, 2]), new(parts[1, 0], parts[1, 1], parts[1, 2])];
                        }
                        else if (!allowStructureProposal || inside.Length > 512) throw;
                    }
                    // Outside the fallback catch: ambiguity must never become
                    // a provider structure request or a guessed fixed binding.
                    RejectInlineParallelAmbiguity(scopes, inside, work);
                }
                foreach (var a in inside) sources[a.Id] = sources[a.Id] with { CellId = id };
                var blanks = scopes.Where(s => s.EmptyVerified).Select(s => s.Role.ToString().ToLowerInvariant()).Concat(fixedBlanks).Distinct().ToArray();
                var parallelCount = fixedBindings.Count > 0 ? fixedBindings.Count : empty || scopes.Count == 0 ? 1 : scopes.Select(s => s.LessonIndex).Distinct().Count();
                var cell = new RecoveryCell(id, pi, box, RecoveryInputState.Complete, slots, inside.Select(a => a.Id).ToArray(), blanks, empty, parallelCount)
                { BindingMode = empty || fixedBindings.Count > 0 ? RecoveryBindingMode.Fixed : RecoveryBindingMode.RoleProposal, RoleScopes = scopes, LessonBindings = fixedBindings, ParallelSeparators = separators, OrderedRowProof = orderedProof,NativeLayoutProof=nativeMapped ? new(1,hash) : null,
                    ClassHeaderIds = cls.Ids, DayHeaderIds = day.Ids, PeriodHeaderIds = chosenPeriods.SelectMany(l => l.Ids).ToArray(),
                    ClassRegion = Region(cls, box), DayRegion = Region(day, box), PeriodRegions = chosenPeriods.ToDictionary(l => l.Value, l => Region(l, box)!) };
                if(priorSlice is not null)
                {
                    if(cell.BindingMode!=RecoveryBindingMode.Fixed || priorSlice.BindingMode!=RecoveryBindingMode.Fixed ||
                        cell.OrderedRowProof is not null || priorSlice.OrderedRowProof is not null || cell.ParallelSeparators is not null || priorSlice.ParallelSeparators is not null ||
                        cell.LessonBindings.Count+priorSlice.LessonBindings.Count>2 || cell.ClassRegion!=priorSlice.ClassRegion || cell.DayRegion!=priorSlice.DayRegion ||
                        !cell.ClassHeaderIds.SequenceEqual(priorSlice.ClassHeaderIds) || !cell.DayHeaderIds.SequenceEqual(priorSlice.DayHeaderIds) || !cell.PeriodHeaderIds.SequenceEqual(priorSlice.PeriodHeaderIds))
                        throw new InvalidDataException("縦に分割された授業欄の対応を確認できません。");
                    var bindings=priorSlice.LessonBindings.Concat(cell.LessonBindings).ToArray();
                    var combinedIds=priorSlice.SourceIds.Concat(cell.SourceIds).ToArray();
                    foreach(var sourceId in combinedIds)sources[sourceId]=sources[sourceId] with {CellId=priorSlice.Id};
                    cell=priorSlice with {Box=priorSlice.Box with {Height=box.Y+box.Height-priorSlice.Box.Y},SourceIds=combinedIds,
                        ConfirmedEmpty=bindings.Length==0,ParallelCount=Math.Max(1,bindings.Length),LessonBindings=bindings,
                        BlankFields=priorSlice.BlankFields.Concat(cell.BlankFields).Distinct().ToArray(),
                        NativeLayoutProof=(priorSlice.ConfirmedEmpty || priorSlice.NativeLayoutProof is not null) && (cell.ConfirmedEmpty || cell.NativeLayoutProof is not null) ? new(1,hash) : null};
                    cells.Remove(priorSlice);
                }
                cells.Add(cell); usedSlots.UnionWith(slots); Register(usedClass, cls.Value, cls.Ids); Register(usedDay, day.Value, day.Ids);
                foreach (var p in chosenPeriods) Register(usedPeriod, p.Value, p.Ids);
            }
        }
        var classes = usedClass.Keys.Order().ToArray(); var days = usedDay.Keys.Order().ToArray();
        var required = (from c in classes from d in days from p in Enumerable.Range(1, maxPeriod) select new RecoverySlot(c, d, p)).ToArray();
        if (cells.Any(c=>c.OrderedRowProof is not null) && (!classes.ToHashSet().SetEquals(RecoveryValidator.SpecialClasses) ||
            !days.ToHashSet().SetEquals(new[]{"1","2","3","4","5"}) || required.Length!=680))
            throw new InvalidDataException("三行時間割の完全なクラス・曜日・時限を確認できません。");
        var titleLabels = kind switch { RecoveryDocumentKind.Timetable => new[] { "時間割", "通常時間割", "授業時間割" }, RecoveryDocumentKind.Exam => ["試験時間割", "定期試験時間割"], _ => ["試験返却時間割"] };
        // Geometry labels and complete native-line labels are discovered in
        // separate passes. Their discovery order is not the document's source
        // order, particularly when only some repeated headers need the latter.
        // Keep exactly the selected original IDs, ordered like Sources below.
        IReadOnlyList<string> HeaderEvidence(IEnumerable<Label> selected)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var label in selected) { work.Step(label.Ids.Count); ids.UnionWith(label.Ids); }
            work.Step(atoms.Length);
            return atoms.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToArray();
        }
        var title = HeaderEvidence(labels.Where(l => titleLabels.Contains(l.Value)));
        IReadOnlyDictionary<string, IReadOnlyList<string>> Evidence(Dictionary<string, HashSet<string>> set) => set.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)atoms.Where(a => p.Value.Contains(a.Id)).Select(a => a.Id).ToArray());
        var document = new RecoveryDocument(hash, kind, year, term, classes, days, required, cells, atoms.Select(a => sources[a.Id]).ToArray(), true,
            HeaderEvidence(years), term is null ? [] : HeaderEvidence(terms), Evidence(usedDay), Evidence(usedClass), Evidence(usedPeriod), new Dictionary<string, string>(), [], []) { DocumentTitleEvidence = title };
        document=AddTimes(document,labels,work);
        if(allowNativeReferences && ocrPages is not {Count:>0})
        {
            var semantic=RecoverySourceCoverage.SemanticIds(document);
            var extra=atoms.Where(a=>!semantic.Contains(a.Id) && !string.IsNullOrWhiteSpace(a.Glyph.Text)).ToArray();
            var notes=new List<RecoveryReferenceNote>();
            var noteGroups=extra.GroupBy(a=>(a.Page,Line:a.Glyph.SourceLine?.ToString(CultureInfo.InvariantCulture) ?? a.Id)).SelectMany(group=> {
                var original=group.OrderBy(a=>a.Glyph.SourceOrder).ThenBy(a=>a.Glyph.X).ToArray();
                // One native drawing line can repeat a weekday in several
                // separate gutters. Its enclosing box is not a body source;
                // retain each original atom rather than bridge class columns.
                return RecoverySourceCoverage.OutsideSchedule(document,group.Key.Page,Bounds(original))
                    ? new[]{original} : original.Select(a=>new[]{a}).ToArray();
            });
            foreach(var original in noteGroups)
            {
                var notePage=original[0].Page;
                work.Step(original.Length+document.Cells.Count);
                var box=Bounds(original);
                if(!RecoverySourceCoverage.OutsideSchedule(document,notePage,box))
                {
                    var failure=new InvalidDataException("表の内部に役割を確認できない印字があります。");
                    failure.Data["UnassignedNativeSourceIds"]=original.Select(a=>a.Id).ToArray();
                    failure.Data["UnassignedNativeBox"]=box;
                    throw failure;
                }
                notes.Add(new(notePage,box,original.Select(a=>a.Id).ToArray()));
                if(notes.Count>512)throw new InvalidDataException("注記の件数が上限を超えています。");
            }
            if(notes.Count>0)document=document with {ReferenceNotes=notes};
        }
        return document;
    }
    private static IReadOnlyList<RecoveryRoleScope> RoleScopes(string cellId, int page, RecoveryBox box, Atom[] body, Label[] labels, Func<int, RecoveryBox, bool> inkFree, Work work)
    {
        var result = new List<RecoveryRoleScope>(); double[]? columnAnchors = null;
        var shiftedSingleLabels = false;
        work.Step(body.Length); var sourceAtoms = body.ToDictionary(a => a.Id);
        var nativeValues = labels.Where(l => l.Page == page && box.Contains(l.Box) && l.NativeValueIds is { Count: > 0 }).ToArray();
        var physicalRoles = labels.Where(l => l.Page == page && box.Contains(l.Box) &&
            Enum.GetValues<RecoveryFieldRole>().Any(role => RecoveryRoleLabels.For(role).Contains(l.Value.TrimEnd(':', '：')))).ToArray();
        bool LiteralContinuation(Label label)
        {
            work.Step(label.Ids.Count);
            var raw = string.Concat(label.Ids.Select(id => sourceAtoms.TryGetValue(id, out var a) ? a.Glyph.Text : ""));
            if (raw.Contains(':') || raw.Contains('：')) return false; // Another explicit prefix remains a label.
            // Preserve the existing physical-column proof for mixed colon/bare
            // parallel fields. A corresponding label in another role row is
            // competing structural evidence, even inside one native OCR line.
            foreach (var other in physicalRoles)
            {
                work.Step();
                if (Enum.GetValues<RecoveryFieldRole>().Any(role =>
                    RecoveryRoleLabels.For(role).Contains(label.Value.TrimEnd(':', '：')) &&
                    RecoveryRoleLabels.For(role).Contains(other.Value.TrimEnd(':', '：')))) continue;
                if (Math.Abs(label.Box.X - other.Box.X) <= 1) return false;
            }
            foreach (var context in nativeValues)
            {
                var ids = context.NativeValueIds!; work.Step(ids.Count + label.Ids.Count);
                var start = ids.ToList().IndexOf(label.Ids[0]);
                if (start >= 0 && ids.Skip(start).Take(label.Ids.Count).SequenceEqual(label.Ids)) return true;
            }
            return false;
        }
        foreach (var role in Enum.GetValues<RecoveryFieldRole>())
        {
            var names = RecoveryRoleLabels.For(role);
            var printed = labels.Where(l => l.Page == page && box.Contains(l.Box) && names.Contains(l.Value.TrimEnd(':', '：')))
                .Where(l => !LiteralContinuation(l)).DistinctBy(l => string.Join(",", l.Ids)).ToArray();
            // At most four physical labels, each possibly observed both as its
            // word prefix and with the SAME original atoms plus a colon. The
            // latter is one fuller label, not a second parallel lesson.
            if (printed.Length > 8) throw new InvalidDataException("科目・教員・教室の独立した原文ラベルを確認できません。");
            var candidates = printed.Where(label => !printed.Any(other =>
            {
                work.Step(1L + label.Ids.Count + other.Ids.Count);
                return other.Value.TrimEnd(':', '：') == label.Value.TrimEnd(':', '：') &&
                    other.Ids.Count == label.Ids.Count + 1 && other.Ids.Take(label.Ids.Count).SequenceEqual(label.Ids) &&
                    sourceAtoms.TryGetValue(other.Ids[^1], out var suffix) && suffix.Glyph.Text is ":" or "：";
            })).OrderBy(l => l.Box.X).ToArray();
            if (candidates.Length is < 1 or > 4 || columnAnchors is not null && columnAnchors.Length != candidates.Length)
                throw new InvalidDataException("科目・教員・教室の独立した原文ラベルを確認できません。");
            if (columnAnchors is not null && columnAnchors.Where((x, i) => Math.Abs(x - candidates[i].Box.X) > 1).Any())
            {
                // Parallel lessons still need corresponding physical columns.
                // One unique explicit label per role has no tuple to choose;
                // use the separate, nonoverlapping row proof below instead.
                if (candidates.Length != 1) throw new InvalidDataException("科目・教員・教室の独立した原文ラベルを確認できません。");
                shiftedSingleLabels = true;
            }
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
        if (shiftedSingleLabels)
        {
            var ordered = result.OrderBy(s => s.Box.Y).ToArray();
            var labelIds = result.SelectMany(s => s.LabelSourceIds).ToArray();
            if (result.Count != Enum.GetValues<RecoveryFieldRole>().Length || result.Any(s => s.LessonIndex != 0) ||
                labelIds.Distinct().Count() != labelIds.Length ||
                ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Box.Y + pair.First.Box.Height >= pair.Second.Box.Y))
                throw new InvalidDataException("科目・教員・教室の独立した原文ラベルを確認できません。");
        }
        return result;
    }
    private static void RejectInlineParallelAmbiguity(IReadOnlyList<RecoveryRoleScope> scopes, Atom[] body, Work work)
    {
        work.Step(scopes.Count);
        foreach (var lesson in scopes.GroupBy(s => s.LessonIndex))
        {
            var counts = lesson.Select(scope =>
            {
                work.Step(body.Length);
                var values = body.Where(a => scope.Box.Contains(a.Box)).ToArray();
                work.Step(values.Sum(a => (long)a.Glyph.Text.Length) * 3L);
                return string.Concat(values.Select(a => a.Glyph.Text)).Replace('･', '・').Split('・').Length;
            }).ToArray();
            // A single inline label for each role proves one lesson scope.
            // Multiple paired values need independent lesson labels; do not
            // collapse them to one compound lesson or guess missing values.
            if (counts.Count(count => count > 1) >= 2)
                throw new InvalidDataException("並記された各授業の独立した原文ラベルを確認できません。");
        }
    }
    private static RecoveryDocument AddTimes(RecoveryDocument document, Label[] labels, Work work)
    {
        if (document.Kind == RecoveryDocumentKind.Timetable) return document;
        var times = new Dictionary<string, string>(); var spans = new Dictionary<string, string>();
        var evidence = new Dictionary<string, IReadOnlyList<string>>(); var bindings = new Dictionary<string, RecoveryClockBinding>();
        var replicas = new Dictionary<string, IReadOnlyList<RecoveryClockBinding>>();
        var commonIds = new List<string>(); var commonRegions = new Dictionary<string, RecoveryHeaderRegion>();
        var normalNote = labels.Where(l => l.Value.Contains("日は通常の授業日どおりの授業時間", StringComparison.Ordinal)).ToArray();
        var firstNote = labels.Where(l => Regex.IsMatch(l.Value, @"^\d{1,2}月\d{1,2}日の時間割は以下のとおり(?:です)?。?$" )).ToArray();
        var firstNoteKeys = firstNote.Select(l => string.Join(",", l.Ids)).ToHashSet(StringComparer.Ordinal);
        var noteIdSet = firstNote.SelectMany(l => l.Ids).Concat(normalNote.SelectMany(l => l.Ids)).ToHashSet();
        var normalIds = document.Sources.Where(s => noteIdSet.Contains(s.Id)).Select(s => s.Id).ToArray();
        var sourceById=document.Sources.ToDictionary(s=>s.Id);
        bool SameNativeClockRow(Label label,Label clock)
        {
            if(label.Ids.Count==0 || clock.Ids.Count==0 || sourceById[clock.Ids[0]].SourceLine is not int line)return false;
            work.Step(label.Ids.Count+clock.Ids.Count);
            return label.Ids.Concat(clock.Ids).All(id=>sourceById[id] is {FromOcr:false,NativeLayoutBox:not null} s && s.SourceLine==line);
        }
        foreach (var clock in labels.Where(l => Regex.IsMatch(l.Value, @"^\d{1,2}:\d{2}[~〜～]\d{1,2}:\d{2}$")))
        {
            work.Step(labels.Length * 10L + document.Cells.Count * 8L);
            var explicitDays = labels.Where(l => l.Page == clock.Page && document.Days.Contains(Day(l.Value, document.Kind, document.SchoolYear) ?? "") && Region(l, clock.Box) is not null).OrderBy(l => clock.Box.Y - l.Box.Y + clock.Box.X - l.Box.X).ToArray();
            var periods = labels.Where(l => l.Page == clock.Page && Regex.IsMatch(l.Value, @"^[1-8](?:限|時限|時限目)?$|^[1-8][・〜-][1-8](?:時限連続|時限|限)$") && Region(l, clock.Box) is not null)
                .OrderByDescending(l=>SameNativeClockRow(l,clock)).ThenBy(l => clock.Box.Y - l.Box.Y + clock.Box.X - l.Box.X).ToArray();
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
                    (document.Kind == RecoveryDocumentKind.Exam ? l.Value is "試験時間割" or "定期試験時間割" : firstNoteKeys.Contains(string.Join(",", l.Ids))))
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
        // Table headers and later clock-chart headers can interleave across pages.
        // Preserve their original source-array order, rather than append order.
        IReadOnlyList<string> OriginalOrder(IReadOnlyList<string> ids)
        {
            work.Step(ids.Count + document.Sources.Count);
            var wanted = ids.ToHashSet(StringComparer.Ordinal);
            var ordered = document.Sources.Where(s => wanted.Contains(s.Id)).Select(s => s.Id).ToArray();
            if (ordered.Length != wanted.Count) throw new InvalidDataException("時刻見出しの原文IDを確認できません。");
            return ordered;
        }
        dayEvidence = dayEvidence.ToDictionary(p => p.Key, p => OriginalOrder(p.Value));
        periodEvidence = periodEvidence.ToDictionary(p => p.Key, p => OriginalOrder(p.Value));
        return document with { Times = times, SpanTimes = spans, TimeEvidence = bindings.Keys.SelectMany(k => evidence[k]).Distinct().ToArray(), ClockEvidence = evidence, ClockBindings = bindings, ClockReplicas = replicas,
            DayEvidence = dayEvidence, PeriodEvidence = periodEvidence, CommonClockEvidence = commonIds.Distinct().ToArray(), CommonClockRegions = commonRegions, NormalTimeNoteEvidence = normalIds };
    }
}
