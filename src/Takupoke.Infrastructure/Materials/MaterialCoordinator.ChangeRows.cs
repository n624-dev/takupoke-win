using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Materials;

public sealed record ChangePreview(string SourceId, string Digest, int SchoolYear, int ParserVersion,
    IReadOnlyList<ScheduleChange> Changes, IReadOnlyList<ChangeParseException> Warnings,
    IReadOnlyList<ChangeReviewRow> ReviewRows)
{
    public bool CanSkipRows => ReviewRows.Count > 0 && Warnings.All(warning =>
        warning.Code is ChangeErrorCode.WeekdayMismatch or ChangeErrorCode.WeekdayOnly);
}

public sealed partial class MaterialCoordinator
{
    private async Task<SourceRecord> ClearStaleRowSkipsAsync(SchoolLease lease, SourceRecord source,
        int year, CancellationToken token)
    {
        if (source.RowSkipConsent is not null &&
            !source.RowSkipConsent.ValidFor(source, year, XlsxChangeReader.Version))
        {
            source = source with { RowSkipConsent = null };
            await store.WriteAsync(lease, "selection." + source.Kind, source, token);
        }
        return source;
    }

    private static bool RowSkipMatches(MaterialAnalysis analysis, SourceRecord source, int year) =>
        source.RowSkipConsent is null ? analysis.RowSkipConsent is null
        : source.RowSkipConsent.ValidFor(source, year, XlsxChangeReader.Version)
            && source.RowSkipConsent.SameAs(analysis.RowSkipConsent);

    public async Task<ChangePreview> PreviewChangesAsync(int schoolYear, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection.Changes", token);
            var attempt = await store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes", token);
            if (source is null || attempt?.SourceDigest != source.Digest || attempt.SchoolYear != schoolYear
                || attempt.ParserVersion != XlsxChangeReader.Version
                || attempt.ChangeError is not (ChangeErrorCode.FormulaCache or
                    ChangeErrorCode.WeekdayMismatch or ChangeErrorCode.WeekdayOnly))
                throw new InvalidDataException("資料または補完年度が変わっています。まず通常の解析をやり直してください。");
            var bytes = await store.ReadOriginalAsync(lease, source.Id, token);
            try
            {
                var preview = await Task.Run(() =>
                {
                    var table = XlsxChangeReader.ReadForPreview(bytes, schoolYear, token);
                    if (table.Warnings.Count == 0)
                        throw new InvalidDataException("曜日の警告がない資料です。通常の解析を行ってください。");
                    var changes = ChangeNormalizer.Parse(table.WithoutWeekdayOnlyRows(), schoolYear,
                        token, allowEmptyPreview: true);
                    return new ChangePreview(source.Id, source.Digest, schoolYear, XlsxChangeReader.Version,
                        changes, table.Warnings, table.ReviewRows);
                }, token);
                if (await store.BeginAsync(token) != lease) throw new OperationCanceledException();
                return preview;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _gate.Release(); }
    }

    public async Task ApplyRowSkipsAsync(ChangePreview preview, IReadOnlyList<int> selectedRows,
        int schoolYear, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection.Changes", token);
            if (source is null || source.Id != preview.SourceId || source.Digest != preview.Digest
                || schoolYear != preview.SchoolYear || preview.ParserVersion != XlsxChangeReader.Version
                || !preview.CanSkipRows || selectedRows.Count == 0
                || !selectedRows.SequenceEqual(selectedRows.Distinct().Order()))
                throw new InvalidDataException("資料または確認内容が変わりました。内容を確認し直してください。");
            // Re-read the selected file with its actual identity before accepting a stale preview.
            using var content = await reader.ReadAsync(source.Path, MaterialKind.Changes,
                source.FileIdentity, token);
            if (NotificationDiff.Digest(content.Bytes) != source.Digest)
                throw new InvalidDataException("ファイルが更新されました。再取得して内容を確認し直してください。");
            var consent = new ChangeRowSkipConsent(source.Id, source.Digest, schoolYear,
                XlsxChangeReader.Version, selectedRows.ToArray());
            var changes = await Task.Run(() => XlsxChangeReader.Parse(content.Bytes, schoolYear,
                token, selectedRows.ToHashSet()), token);
            var analysis = new MaterialAnalysis(source.Id, MaterialKind.Changes, XlsxChangeReader.Version,
                source.Digest, source.OriginalName, _clock.GetUtcNow(), schoolYear,
                Changes: changes, RowSkipConsent: consent);
            await store.SaveAnalysisAsync(lease, analysis, token, authorizeRowSkips: true);
            await store.CollectOriginalsAsync(lease, token);
        }
        finally { _gate.Release(); }
    }
}
