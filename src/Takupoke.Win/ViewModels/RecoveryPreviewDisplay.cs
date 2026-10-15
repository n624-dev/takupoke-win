using System.Collections.Immutable;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Win.ViewModels;

// This is derived on a worker from each freshly decoded preview. It is never
// persisted or used as an authorization to adopt; the store validates again.
public sealed record RecoveryPreviewDisplay(string SourceId, string PdfHash, string ResultHash,
    RecoveryDocumentKind Kind, int SchoolYear, string? Term, ImmutableArray<string> Classes, ImmutableArray<string> Days,
    ImmutableArray<RecoveryDisplayCell> Cells, ImmutableDictionary<string, string> Times,
    ImmutableDictionary<string, string> SpanTimes, RecoveryMetadata Metadata)
{
    public ImmutableArray<RecoveryDisplayReferenceNote> ReferenceNotes { get; init; } = [];
    public ImmutableArray<RecoveryDisplayCorrection> Corrections { get; init; } = [];
    public ImmutableArray<RecoveryDisplayChange> PreviousChanges { get; init; } = [];
    public bool PreviousComparable { get; init; }
    public int ReviewedFieldCount { get; init; }
    public ImmutableArray<RecoveryFieldTarget> ReviewedTargets { get; init; } = [];

    public bool SelectionFits(IEnumerable<RecoveryFieldTarget> targets) =>
        ReviewedTargets.Concat(targets).Distinct().Count() <= RecoveryManualAssistance.MaximumReviewFields;

    public ImmutableArray<RecoveryFieldTarget> RoomTargets(string className) => Cells
        .Where(c => c.Slots.Any(s => s.ClassName == className))
        .SelectMany(c => c.Lessons.Select((lesson, index) => (lesson, index))
            .Where(x => x.lesson.EditableRoles.Contains(RecoveryFieldRole.Room))
            .Select(x => new RecoveryFieldTarget(c.CellId, x.index, RecoveryFieldRole.Room)))
        .Distinct().ToImmutableArray();

    public bool SelectRooms(string className, ISet<RecoveryFieldTarget> selected)
    {
        var targets = RoomTargets(className);
        if (targets.Length == 0 || !SelectionFits(selected.Concat(targets))) return false;
        foreach (var target in targets) selected.Add(target);
        return true;
    }

    public static RecoveryPreviewDisplay? Create(RecoveryPreview preview, CancellationToken token,
        MaterialAnalysis? previous = null)
    {
        token.ThrowIfCancellationRequested();
        if (!RecoveryValidator.Validate(preview.Document, preview.Result, token).CanAdopt) return null;
        var document = preview.Document;
        var ocrIds = document.Sources.Where(s => { token.ThrowIfCancellationRequested(); return s.FromOcr || s.RequiresVisualReview; })
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var recovered = preview.Result.Cells.ToDictionary(cell => { token.ThrowIfCancellationRequested(); return cell.CellId; }, StringComparer.Ordinal);
        var reviewed = (preview.Result.HumanCorrections ?? []).Where(c => c.HeaderProof is null)
            .Select(c => c.Target).ToHashSet();
        var cells = ImmutableArray.CreateBuilder<RecoveryDisplayCell>(document.Cells.Count);
        foreach (var cell in document.Cells)
        {
            token.ThrowIfCancellationRequested();
            var result = recovered[cell.Id];
            cells.Add(new(cell.Slots.Select(slot => new RecoverySlot(slot.ClassName, slot.Day, slot.Period)).ToImmutableArray(),
                result.State, result.Lessons.Select((lesson, index) => {
                    var fields = new[] { (RecoveryFieldRole.Subject, lesson.Subject),
                        (RecoveryFieldRole.Teacher, lesson.Teacher), (RecoveryFieldRole.Room, lesson.Room) };
                    var editable = document.Capture is not null && cell.BindingMode is RecoveryBindingMode.Fixed or RecoveryBindingMode.RoleProposal
                        ? fields.Where(pair => pair.Item2.Evidence.Any(ocrIds.Contains)).Select(pair => pair.Item1).ToImmutableArray() : [];
                    return new RecoveryDisplayLesson(lesson.Subject.Value,
                    lesson.Teacher.Value, lesson.Teacher.State == RecoveryValueState.Empty,
                    lesson.Room.Value, lesson.Room.State == RecoveryValueState.Empty) {
                        EditableRoles = editable,
                        CodeReviewRoles = fields.Where(pair => editable.Contains(pair.Item1) &&
                            !reviewed.Contains(new(cell.Id, index, pair.Item1)) && RecoveryCodeReview.Describe(pair.Item2.Value) is not null)
                            .Select(pair => pair.Item1).ToImmutableArray()
                    };
                }).ToImmutableArray()) { CellId = cell.Id });
        }
        var display = new RecoveryPreviewDisplay(preview.SourceId, document.PdfHash,
            RecoveryValidator.Fingerprint(preview.Result), document.Kind, document.SchoolYear, document.Term,
            document.Classes.ToImmutableArray(), document.Days.ToImmutableArray(), cells.MoveToImmutable(), document.Times.ToImmutableDictionary(StringComparer.Ordinal),
            document.SpanTimes.ToImmutableDictionary(StringComparer.Ordinal), preview.Result.Metadata with { });
        var sources = document.Sources.ToDictionary(s => { token.ThrowIfCancellationRequested(); return s.Id; }, StringComparer.Ordinal);
        var cellsById = display.Cells.ToDictionary(c => c.CellId, StringComparer.Ordinal);
        var corrections = (preview.Result.HumanCorrections ?? []).Where(c => c.HeaderProof is null).Select(correction => {
            token.ThrowIfCancellationRequested();
            return new RecoveryDisplayCorrection(correction.Target.CellId, correction.Target.LessonIndex,
                correction.Target.Role, cellsById[correction.Target.CellId].Slots,
                string.Concat(correction.OriginalParentIds.Select(id => { token.ThrowIfCancellationRequested(); return sources[id].Text; })),
                correction.CorrectedText, correction.Provenance);
        }).Where(c => document.Capture?.ManualSchemaVersion is not (2 or 3 or 4) ||
            !string.Equals(c.OriginalOcr, c.UserText, StringComparison.Ordinal)).ToImmutableArray();
        // Only an actual certified formal projection of this identical source
        // document permits cell/parallel-position comparison. Updated PDFs,
        // Strict-only projections and unmatched topology are not guessed.
        var comparable = previous is { Recovery: { } audit } && previous.OriginalId == preview.SourceId &&
            previous.SourceDigest == document.PdfHash && RecoveryAnalysisConverter.MayDisplay(previous, token) &&
            RecoveryValidator.Fingerprint(audit.Document) == RecoveryValidator.Fingerprint(document);
        var changes = ImmutableArray.CreateBuilder<RecoveryDisplayChange>();
        if (comparable)
        {
            var priorCells = previous!.Recovery!.Result.Cells.ToDictionary(c => c.CellId, StringComparer.Ordinal);
            foreach (var cell in display.Cells)
            {
                token.ThrowIfCancellationRequested();
                var prior = priorCells[cell.CellId];
                for (var index = 0; index < cell.Lessons.Length; index++)
                {
                    var lesson = cell.Lessons[index]; var old = prior.Lessons[index];
                    Compare(RecoveryFieldRole.Subject, old.Subject, lesson.Subject, false);
                    Compare(RecoveryFieldRole.Teacher, old.Teacher, lesson.Teacher, lesson.TeacherEmpty);
                    Compare(RecoveryFieldRole.Room, old.Room, lesson.Room, lesson.RoomEmpty);
                    void Compare(RecoveryFieldRole role, RecoveryField before, string after, bool empty)
                    {
                        token.ThrowIfCancellationRequested();
                        if (before.Value != after || (before.State == RecoveryValueState.Empty) != empty)
                            changes.Add(new(cell.CellId, index, role, cell.Slots, before.Value, after));
                    }
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return display with { ReferenceNotes=(document.ReferenceNotes ?? []).Select(n=>new RecoveryDisplayReferenceNote(n.Page,string.Concat(n.SourceIds.Select(id=>sources[id].Text)))).ToImmutableArray(),Corrections = corrections, ReviewedFieldCount = preview.Result.HumanCorrections?.Count ?? 0,
            ReviewedTargets = (preview.Result.HumanCorrections ?? []).Select(c => c.Target).ToImmutableArray(),
            PreviousComparable = comparable, PreviousChanges = changes.ToImmutable() };
    }
}

public sealed record RecoveryDisplayCell(ImmutableArray<RecoverySlot> Slots, RecoveryValueState State,
    ImmutableArray<RecoveryDisplayLesson> Lessons)
{
    public string CellId { get; init; } = "";
}
public sealed record RecoveryDisplayLesson(string Subject, string Teacher, bool TeacherEmpty, string Room, bool RoomEmpty)
{
    public ImmutableArray<RecoveryFieldRole> EditableRoles { get; init; } = [];
    public ImmutableArray<RecoveryFieldRole> CodeReviewRoles { get; init; } = [];
}

// Display advice only. It neither changes OCR/native scores nor authorizes adoption.
public static class RecoveryCodeReview
{
    public static string? Describe(string value)
    {
        var found = new HashSet<char>();
        for (var start = 0; start < value.Length;)
        {
            if (!Code(value[start])) { start++; continue; }
            var end = start + 1;
            while (end < value.Length && Code(value[end])) end++;
            var part = value.AsSpan(start, end - start);
            var letter = false; var digit = false;
            foreach (var c in part) { letter |= char.IsAsciiLetter(c); digit |= char.IsAsciiDigit(c); }
            if (letter && digit) foreach (var c in part) if (c is 'I' or 'l' or '1' or 'O' or '0') found.Add(c);
            start = end;
        }
        var names = new[] { ('I', "I（大文字アイ）"), ('l', "l（小文字エル）"), ('1', "1（数字のいち）"),
            ('O', "O（大文字オー）"), ('0', "0（数字のゼロ）") }.Where(p => found.Contains(p.Item1)).Select(p => p.Item2);
        return found.Count == 0 ? null : "文字種を原本で確認: " + string.Join("、", names);
        static bool Code(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';
    }
}

public sealed record RecoveryDisplayCorrection(string CellId, int LessonIndex, RecoveryFieldRole Role,
    ImmutableArray<RecoverySlot> Slots, string OriginalOcr, string UserText, string Provenance);
public sealed record RecoveryDisplayChange(string CellId, int LessonIndex, RecoveryFieldRole Role,
    ImmutableArray<RecoverySlot> Slots, string PreviousText, string CurrentText);

public sealed record RecoveryDisplayReferenceNote(int Page,string Text);
