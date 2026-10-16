namespace Takupoke.Core.Recovery;

// Original source IDs used by semantic schedule proofs. Reference notes stay
// separate so they cannot supply a missing subject, header, clock or owner.
public static class RecoverySourceCoverage
{
    public static HashSet<string> SemanticIds(RecoveryDocument doc)
    {
        var classified = doc.Cells.SelectMany(c => c.SourceIds).Concat(doc.CommonClockEvidence).Concat(doc.YearEvidence).Concat(doc.DocumentTitleEvidence).Concat(doc.Term is null ? [] : doc.TermEvidence).ToHashSet();
        foreach (var cls in doc.Classes) classified.UnionWith(doc.ClassEvidence.GetValueOrDefault(cls) ?? []);
        foreach (var day in doc.Days) classified.UnionWith(doc.DayEvidence.GetValueOrDefault(day) ?? []);
        var maxPeriod=doc.Kind==RecoveryDocumentKind.Exam ? 6 : 8;
        foreach (var period in Enumerable.Range(1, maxPeriod)) classified.UnionWith(doc.PeriodEvidence.GetValueOrDefault(period.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? []);
        if (doc.Kind != RecoveryDocumentKind.Timetable)
        {
            foreach (var key in doc.Times.Keys.Concat(doc.SpanTimes.Keys).Distinct())
            {
                classified.UnionWith(doc.ClockEvidence.GetValueOrDefault(key) ?? []);
                classified.UnionWith(doc.ClockBindings.GetValueOrDefault(key)?.PeriodHeaderIds ?? []);
                classified.UnionWith((doc.ClockReplicas.GetValueOrDefault(key) ?? []).SelectMany(b => b.PeriodHeaderIds));
            }
            if (doc.Kind == RecoveryDocumentKind.Return) classified.UnionWith(doc.NormalTimeNoteEvidence);
        }
        classified.UnionWith(doc.Cells.Where(c => c.BindingMode == RecoveryBindingMode.RoleProposal).SelectMany(c => c.RoleScopes).SelectMany(s => s.LabelSourceIds));
        return classified;
    }
    public static RecoveryBox? BodyBounds(RecoveryDocument doc,int page)
    {
        var cells=doc.Cells.Where(c=>c.Page==page).ToArray();
        if(cells.Length==0)return null;
        var left=cells.Min(c=>c.Box.X);var top=cells.Min(c=>c.Box.Y);
        return new(left,top,cells.Max(c=>c.Box.X+c.Box.Width)-left,cells.Max(c=>c.Box.Y+c.Box.Height)-top);
    }
    public static bool OutsideSchedule(RecoveryDocument doc,int page,RecoveryBox box)
    {
        var body=BodyBounds(doc,page);if(body is null)return false;
        if(!Intersects(body,box))return true;
        if(doc.Kind!=RecoveryDocumentKind.Exam || box.Y<body.Y || box.Y+box.Height>body.Y+body.Height)return false;
        var rails=doc.Cells.Where(c=>c.Page==page && c.ClassRegion is {Axis:RecoveryHeaderAxis.Above}).Select(c=>c.ClassRegion!.Box).Distinct().ToArray();
        return rails.Length>=2 && box.X>=rails.Min(r=>r.X) && box.X+box.Width<=rails.Max(r=>r.X+r.Width) &&
            rails.All(r=>Math.Min(box.X+box.Width,r.X+r.Width)<=Math.Max(box.X,r.X)) &&
            !doc.Cells.Any(c=>c.Page==page && Intersects(c.Box,box));
    }
    public static bool Intersects(RecoveryBox first,RecoveryBox second) =>
        first.X<second.X+second.Width && second.X<first.X+first.Width && first.Y<second.Y+second.Height && second.Y<first.Y+first.Height;
}
