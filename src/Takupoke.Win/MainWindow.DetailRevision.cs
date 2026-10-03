using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private Func<string>? _scheduleDetailRevision;
    private string? _scheduleDetailSnapshot;

    private void DismissChangedScheduleDetail()
    {
        if (_activeDialog is not { } dialog || _scheduleDetailRevision is not { } revision ||
            string.Equals(_scheduleDetailSnapshot, revision(), StringComparison.Ordinal)) return;
        // The immutable panel belongs to an older accepted result or original.
        // General settings and confirmation dialogs never register this callback.
        _scheduleDetailRevision = null; _scheduleDetailSnapshot = null;
        dialog.Content = null; dialog.Hide();
    }

    private string ScheduleDetailRevision(MaterialKind? analysisKind = null, bool names = true)
    {
        var schedule = analysisKind is null;
        MaterialKind[] kinds = schedule ? Enum.GetValues<MaterialKind>() : [analysisKind.GetValueOrDefault()];
        var materials = kinds.Select(kind =>
        {
            var material = _model.Materials.GetValueOrDefault(kind);
            var source = material?.Source; var analysis = material?.Analysis;
            return new { Kind = kind, source?.Id, source?.Digest, source?.Path, source?.OriginalName,
                analysis?.OriginalId, analysis?.ParserVersion, analysis?.SourceDigest, analysis?.ParsedAt,
                analysis?.Timetable, analysis?.Changes, analysis?.Special };
        }).ToArray();
        var scope = schedule ? new { _model.Today, _model.WeekStart, _model.Preferences.SelectedClasses,
            _model.Preferences.International, _model.Preferences.IncludesChanges, _model.Preferences.DefaultSchoolYear } : null;
        // Fetch/check times, status, busy state and ordinary clock ticks are not revisions.
        var snapshot = new { Materials = materials, Mappings = names ? _model.Mappings : null,
            MappingRevision = names ? _model.MappingRecord?.Revision : null,
            Times = schedule ? _model.TimesRecord?.Data : null,
            TimesRevision = schedule ? _model.TimesRecord?.Revision : null,
            Events = schedule ? _model.Data.Events : null, Scope = scope };
        return Convert.ToHexString(SHA256.HashData(DataCodec.Encode(snapshot)));
    }
}
