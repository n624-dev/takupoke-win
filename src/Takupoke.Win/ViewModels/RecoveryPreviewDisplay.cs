using System.Collections.Immutable;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.ViewModels;

// This is derived on a worker from each freshly decoded preview. It is never
// persisted or used as an authorization to adopt; the store validates again.
public sealed record RecoveryPreviewDisplay(string SourceId, string PdfHash, string ResultHash,
    RecoveryDocumentKind Kind, int SchoolYear, string? Term, ImmutableArray<string> Classes, ImmutableArray<string> Days,
    ImmutableArray<RecoveryDisplayCell> Cells, ImmutableDictionary<string, string> Times,
    ImmutableDictionary<string, string> SpanTimes, RecoveryMetadata Metadata)
{
    public static RecoveryPreviewDisplay? Create(RecoveryPreview preview, CancellationToken token)
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
                    lesson.Room.Value, lesson.Room.State == RecoveryValueState.Empty)).ToImmutableArray()));
        }
        var display = new RecoveryPreviewDisplay(preview.SourceId, document.PdfHash,
            RecoveryValidator.Fingerprint(preview.Result), document.Kind, document.SchoolYear, document.Term,
            document.Classes.ToImmutableArray(), document.Days.ToImmutableArray(), cells.MoveToImmutable(), document.Times.ToImmutableDictionary(StringComparer.Ordinal),
            document.SpanTimes.ToImmutableDictionary(StringComparer.Ordinal), preview.Result.Metadata with { });
        token.ThrowIfCancellationRequested();
        return display;
    }
}

public sealed record RecoveryDisplayCell(ImmutableArray<RecoverySlot> Slots, RecoveryValueState State,
    ImmutableArray<RecoveryDisplayLesson> Lessons);
public sealed record RecoveryDisplayLesson(string Subject, string Teacher, bool TeacherEmpty, string Room, bool RoomEmpty);
