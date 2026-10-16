using System.Collections;
using System.Reflection;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// Reflects the real private label producer on closed source records, without
// changing evidence or invoking a model. It is not a recovery implementation.
internal static class RoleLabelInspect
{
    internal static void Run(string path)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var builder = typeof(RecoveryDocumentBuilder);
        var atomType = builder.GetNestedType("Atom", BindingFlags.NonPublic)!;
        var labelType = builder.GetNestedType("Label", BindingFlags.NonPublic)!;
        var workType = builder.GetNestedType("Work", BindingFlags.NonPublic)!;
        var legacy = builder.GetMethod("Labels", BindingFlags.NonPublic | BindingFlags.Static)!;
        var native = builder.GetMethod("OcrLineLabels", BindingFlags.NonPublic | BindingFlags.Static)!;
        var scopes = builder.GetMethod("RoleScopes", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var raw in File.ReadLines(path))
        {
            using var observation = JsonDocument.Parse(raw); var o = observation.RootElement;
            if (o.GetProperty("condition").GetString() != "ink-input-frame" || !o.GetProperty("id").GetString()!.EndsWith("-positive")) continue;
            var doc = JsonSerializer.Deserialize<RecoveryDocument>(o.GetProperty("document").GetRawText())!;
            var pages = doc.Capture!.Pages.Select(p => new PdfPageLayout(p.Width, p.Height,
                doc.Sources.Where(s => s.Page == p.Page).OrderBy(s => int.Parse(s.Id[(s.Id.IndexOf('s') + 1)..]))
                    .Select(s => new PdfGlyph(s.Text, s.Box.X, s.Box.Y, s.Box.Width, s.Box.Height, s.SourceLine, s.SourceOrder)).ToArray(),
                [] )).ToArray();
            // Closed replay sources preserve glyphs but not original rules;
            // infer no replacement rules here. The full original raw must be
            // supplied as sibling observations for the exact native grid.
            var originalPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "observations.jsonl");
            using var original = File.ReadLines(originalPath).Select(l => JsonDocument.Parse(l)).First(d =>
                d.RootElement.GetProperty("id").GetString() == o.GetProperty("id").GetString() &&
                d.RootElement.GetProperty("condition").GetString() == "ink-input-frame");
            foreach (var p in original.RootElement.GetProperty("pageReports").EnumerateArray())
            {
                var index = p.GetProperty("number").GetInt32() - 1;
                pages[index] = pages[index] with { Lines = JsonSerializer.Deserialize<PdfRule[]>(p.GetProperty("rules").GetRawText())! };
            }
            var byId = new Dictionary<string, object>(); var atoms = new List<(int Page, PdfGlyph Glyph, object Atom)>();
            foreach (var (page, pi) in pages.Select((p, i) => (p, i + 1)))
            foreach (var (glyph, gi) in page.Glyphs.Select((g, i) => (g, i)))
            {
                var id = $"p{pi}s{gi}"; var atom = Activator.CreateInstance(atomType, id, pi, glyph)!;
                byId.Add(id, atom); atoms.Add((pi, glyph, atom));
            }
            Array Typed(Type type, IEnumerable<object> values)
            {
                var list = values.ToArray(); var array = Array.CreateInstance(type, list.Length);
                for (var i = 0; i < list.Length; i++) array.SetValue(list[i], i);
                return array;
            }
            var all = Typed(atomType, atoms.OrderBy(a => a.Page).ThenBy(a => Math.Round(a.Glyph.Cy / 2)).ThenBy(a => a.Glyph.Cx).Select(a => a.Atom));
            var work = Activator.CreateInstance(workType, lifetime.Token)!;
            object Property(object label, string name) => labelType.GetProperty(name)!.GetValue(label)!;
            IReadOnlyList<string> Ids(object label) => (IReadOnlyList<string>)Property(label, "Ids");
            string Key(object label) => $"{Property(label, "Page")}|{Property(label, "Value")}|{string.Join(',', Ids(label))}";
            var labels = ((IEnumerable)legacy.Invoke(null, [all, work])!).Cast<object>().ToList();
            var seen = labels.Select(Key).ToHashSet();
            labels.AddRange(((IEnumerable)native.Invoke(null, [all, pages, Enumerable.Range(1, pages.Length).ToHashSet(), work])!).Cast<object>().Where(l => seen.Add(Key(l))));
            var reports = new List<object>();
            foreach (var cell in doc.Cells.Where(c => !c.ConfirmedEmpty && c.RoleScopes.Count == 0).Take(12))
            {
                var inside = labels.Where(l => (int)Property(l, "Page") == cell.Page && cell.Box.Contains((RecoveryBox)Property(l, "Box"))).ToArray();
                object? result = null; string? failure = null;
                try { result = scopes.Invoke(null, [cell.Id, cell.Page, cell.Box, Typed(atomType, cell.SourceIds.Select(id => byId[id])),
                    Typed(labelType, inside), (Func<int, RecoveryBox, bool>)((_, _) => false), work]); }
                catch (TargetInvocationException e) { failure = e.InnerException!.Message; }
                reports.Add(new { cell.Id, failure, result, labels = inside.Select(l => new {
                    value = Property(l, "Value"), box = Property(l, "Box"), ids = Ids(l), rawText = string.Concat(Ids(l).Select(id => doc.Sources.Single(s => s.Id == id).Text)) }) });
            }
            Console.WriteLine(JsonSerializer.Serialize(new { id = o.GetProperty("id").GetString(), reports, nativeCalls = 0,
                scope = "Read-only inspection of actual private source-label producer on closed original evidence" }));
        }
    }
}
