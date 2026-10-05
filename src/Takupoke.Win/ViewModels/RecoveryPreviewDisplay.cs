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
    public ImmutableArray<RecoveryDisplayCorrection> Corrections { get; init; } = [];
    public ImmutableArray<RecoveryDisplayChange> PreviousChanges { get; init; } = [];
    public bool PreviousComparable { get; init; }

    public static RecoveryPreviewDisplay? Create(RecoveryPreview preview, CancellationToken token,
        MaterialAnalysis? previous = null)
    {
        token.ThrowIfCancellationRequested();
        if (!RecoveryValidator.Validate(preview.Document, preview.Result, token).CanAdopt) return null;
        var document = preview.Document;
        var recovered = preview.Result.Cells.ToDictionary(cell => { token.ThrowIfCancellationRequested(); return cell.CellId; }, StringComparer.Ordinal);
        var cells = ImmutableArray.CreateBuilder<RecoveryDisplayCell>(document.Cells.Count);
        foreach (var cell in document.Cells)
        {
            token.ThrowIfCancellationRequested();
            var result = recovered[cell.Id];
            cells.Add(new(cell.Slots.Select(slot => new RecoverySlot(slot.ClassName, slot.Day, slot.Period)).ToImmutableArray(),
                result.State, result.Lessons.Select(lesson => new RecoveryDisplayLesson(lesson.Subject.Value,
                    lesson.Teacher.Value, lesson.Teacher.State == RecoveryValueState.Empty,
                    lesson.Room.Value, lesson.Room.State == RecoveryValueState.Empty)).ToImmutableArray()) { CellId = cell.Id });
        }
        var display = new RecoveryPreviewDisplay(preview.SourceId, document.PdfHash,
            RecoveryValidator.Fingerprint(preview.Result), document.Kind, document.SchoolYear, document.Term,
            document.Classes.ToImmutableArray(), document.Days.ToImmutableArray(), cells.MoveToImmutable(), document.Times.ToImmutableDictionary(StringComparer.Ordinal),
            document.SpanTimes.ToImmutableDictionary(StringComparer.Ordinal), preview.Result.Metadata with { });
        var sources = document.Sources.ToDictionary(s => { token.ThrowIfCancellationRequested(); return s.Id; }, StringComparer.Ordinal);
        var cellsById = display.Cells.ToDictionary(c => c.CellId, StringComparer.Ordinal);
        var corrections = (preview.Result.HumanCorrections ?? []).Select(correction => {
            token.ThrowIfCancellationRequested();
            return new RecoveryDisplayCorrection(correction.Target.CellId, correction.Target.LessonIndex,
                correction.Target.Role, cellsById[correction.Target.CellId].Slots,
                string.Concat(correction.OriginalParentIds.Select(id => { token.ThrowIfCancellationRequested(); return sources[id].Text; })),
                correction.CorrectedText, correction.Provenance);
        }).ToImmutableArray();
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
        return display with { Corrections = corrections, PreviousComparable = comparable, PreviousChanges = changes.ToImmutable() };
    }
}

public sealed record RecoveryDisplayCell(ImmutableArray<RecoverySlot> Slots, RecoveryValueState State,
    ImmutableArray<RecoveryDisplayLesson> Lessons)
{
    public string CellId { get; init; } = "";
}
public sealed record RecoveryDisplayLesson(string Subject, string Teacher, bool TeacherEmpty, string Room, bool RoomEmpty);

public sealed record RecoveryDisplayCorrection(string CellId, int LessonIndex, RecoveryFieldRole Role,
    ImmutableArray<RecoverySlot> Slots, string OriginalOcr, string UserText, string Provenance);
public sealed record RecoveryDisplayChange(string CellId, int LessonIndex, RecoveryFieldRole Role,
    ImmutableArray<RecoverySlot> Slots, string PreviousText, string CurrentText);
