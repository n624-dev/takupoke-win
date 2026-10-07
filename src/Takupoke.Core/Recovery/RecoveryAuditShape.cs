namespace Takupoke.Core.Recovery;

// Traverses the complete serialized audit shape before a fingerprint allocates
// its JSON. Collection counts, references, text and crop bytes share a finite
// budget. Reference/geometry charges reserve fixed JSON overhead. Repeated
// references are charged for each serialized occurrence.
internal static class RecoveryAuditShape
{
    internal static void Check(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous,
        RecoverySemanticCertification? certificate, RecoverySemanticCertification? predecessor,
        CancellationToken token, IReadOnlyList<RecoverySemanticCertification>? history = null)
    {
        var work = new RecoveryWorkBudget(token);
        void Text(string? value) { work.Step(1L + (value?.Length ?? 0)); }
        void List<T>(IReadOnlyList<T> values, Action<T> check)
        {
            work.Step(values.Count);
            foreach (var value in values) { work.Step(32); check(value); }
        }
        void Map<T>(IReadOnlyDictionary<string, T> values, Action<T> check)
        {
            work.Step(values.Count);
            foreach (var pair in values) { work.Step(32); Text(pair.Key); check(pair.Value); }
        }
        void Texts(IReadOnlyList<string> values) => List(values, Text);
        void Box(RecoveryBox? box) { work.Step(64); }
        void Region(RecoveryHeaderRegion? region) { work.Step(); if (region is not null) Box(region.Box); }
        void Slot(RecoverySlot slot) { work.Step(); Text(slot.ClassName); Text(slot.Day); }
        void Metadata(RecoveryMetadata? metadata)
        {
            work.Step(); if (metadata is null) return;
            Text(metadata.Provider); Text(metadata.ModelId); Text(metadata.ModelVersion); Text(metadata.RuntimeVersion);
            Text(metadata.PromptVersion); Text(metadata.OsVersion); Text(metadata.RecoveryVersion);
        }
        void Target(RecoveryFieldTarget target)
        {
            work.Step(); Text(target.CellId);
            // Key is serialized too; its enum/integer suffix has bounded size.
            work.Step(1L + target.CellId.Length + 64);
        }
        void Clock(RecoveryClockBinding clock)
        {
            work.Step(); Box(clock.Box); Text(clock.Day); Texts(clock.DayHeaderIds); Region(clock.DayRegion);
            Texts(clock.PeriodHeaderIds); Region(clock.PeriodRegion);
        }
        void Field(RecoveryField field) { work.Step(); Text(field.Value); Texts(field.Evidence); }
        void Acceptance(RecoveryAcceptance? receipt)
        {
            work.Step(); if (receipt is null) return;
            Text(receipt.PdfHash); Text(receipt.ResultHash); Text(receipt.ScopeHash); Metadata(receipt.Metadata);
        }
        void Certificate(RecoverySemanticCertification? receipt)
        {
            work.Step(); if (receipt is null) return;
            Text(receipt.ScopeHash); Text(receipt.ResultHash); Text(receipt.AcceptanceHash);
            Text(receipt.PreviousAcceptanceHash); Text(receipt.PreviousCertificationHash);
        }
        work.Step(); Text(doc.PdfHash); Text(doc.Term); Texts(doc.Classes); Texts(doc.Days); List(doc.RequiredSlots, Slot);
        List(doc.Sources, source => { Text(source.Id); Text(source.CellId); Text(source.Text); Box(source.Box); });
        List(doc.Cells, cell =>
        {
            Text(cell.Id); Box(cell.Box); List(cell.Slots, Slot); Texts(cell.SourceIds); Texts(cell.BlankFields);
            if(cell.OrderedRowProof is { } ordered)
            {
                if(ordered.Rows.Count!=3 || ordered.Rows.Any(row=>row.Count>256)) throw new RecoveryWorkLimitException();
                Texts(ordered.SourceIds);
                List(ordered.Rows,row=>List(row,piece=>{Text(piece.Text);Box(piece.Box);}));
            }
            if (cell.ParallelSeparators is { } separators) Map(separators, Text);
            List(cell.RoleScopes, scope => { Box(scope.Box); Texts(scope.LabelSourceIds); Region(scope.LabelRegion); });
            Texts(cell.ClassHeaderIds); Texts(cell.DayHeaderIds); Texts(cell.PeriodHeaderIds);
            List(cell.LessonBindings, binding => { Texts(binding.Subject); Texts(binding.Teacher); Texts(binding.Room); });
            Region(cell.ClassRegion); Region(cell.DayRegion); Map(cell.PeriodRegions, Region);
        });
        Texts(doc.YearEvidence); Texts(doc.TermEvidence); Map(doc.DayEvidence, Texts); Map(doc.ClassEvidence, Texts);
        Map(doc.PeriodEvidence, Texts); Map(doc.Times, Text); Texts(doc.TimeEvidence); Texts(doc.NormalTimeNoteEvidence);
        Metadata(doc.StructureMetadata); Texts(doc.CommonClockEvidence); Map(doc.CommonClockRegions, Region);
        Texts(doc.DocumentTitleEvidence); Map(doc.ClockBindings, Clock); Map(doc.ClockReplicas, clocks => List(clocks, Clock));
        Map(doc.SpanTimes, Text); Map(doc.ClockEvidence, Texts);
        if (doc.Capture is { } capture)
        {
            work.Step(); Text(capture.PdfHash); Text(capture.SourceSnapshot);
            List(capture.Pages, page => Text(page.RasterHash));
            if (capture.OriginalCrops is { } crops) List(crops, crop =>
            { Target(crop.Target); Text(crop.OriginalRasterHash); work.Step(crop.Bgra.LongLength); Text(crop.Sha256); });
        }
        Text(result.PdfHash); Text(result.Term); Metadata(result.Metadata);
        List(result.Cells, cell =>
        {
            Text(cell.CellId); List(cell.Lessons, lesson =>
            { Field(lesson.Subject); Field(lesson.Teacher); Field(lesson.Room); Texts(lesson.DateEvidence); Texts(lesson.PeriodEvidence); });
        });
        if (result.HumanCorrections is { } corrections) List(corrections, correction =>
        {
            Target(correction.Target); Text(correction.PdfHash); Text(correction.AcquisitionHash); Text(correction.DocumentSnapshot);
            Texts(correction.OriginalParentIds); Box(correction.Crop); Text(correction.CorrectedText); Text(correction.Provenance);
        });
        Acceptance(acceptance); Acceptance(previous); Certificate(certificate); Certificate(predecessor);
        if(history is not null) { if(history.Count!=1)throw new RecoveryWorkLimitException();List(history,Certificate); }
    }
}
