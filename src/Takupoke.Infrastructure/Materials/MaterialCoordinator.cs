using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Materials;

public sealed record MaterialResult(MaterialKind Kind, bool Changed, bool Parsed, string? Error = null);
public sealed record ChangePreview(IReadOnlyList<ScheduleChange> Changes, IReadOnlyList<ChangeParseException> Warnings)
{
    public string? SourceId { get; init; }
    public string? SourceDigest { get; init; }
    public int SchoolYear { get; init; }
    public bool CanCorrectWeekdays => SourceId is not null && SourceDigest is not null && Warnings.Count > 0 && Warnings.All(w => w.CanCorrectWeekday);
}
public sealed class MaterialCoordinator(SchoolDataStore store, FileSourceReader reader, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public Task<MaterialResult> SelectAsync(MaterialKind kind, string path, int schoolYear, CancellationToken token = default) => Run(kind, path, schoolYear, true, false, token);
    public Task<MaterialResult> RefreshAsync(MaterialKind kind, int schoolYear, CancellationToken token = default) => Run(kind, null, schoolYear, false, false, token);
    public Task<MaterialResult> ReparseAsync(MaterialKind kind, int schoolYear, CancellationToken token = default) => Run(kind, null, schoolYear, false, true, token);
    private async Task<MaterialResult> Run(MaterialKind kind, string? selectedPath, int year, bool selecting, bool reparsing, CancellationToken token, ChangePreview? consent = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection." + kind, token);
            if (!selecting && source is null) return new(kind, false, false);
            try
            {
                // Selection follows this path's latest version, including atomic replacements
                // by sync providers. The reader still rejects changes during this read.
                using var content = await reader.ReadAsync(selectedPath ?? source!.Path, kind, null, token);
                var digest = NotificationDiff.Digest(content.Bytes); var now = _clock.GetUtcNow();
                if (consent is not null && (source?.Id != consent.SourceId || digest != consent.SourceDigest || year != consent.SchoolYear))
                {
                    if (source is not null && digest != source.Digest)
                    {
                        var changedSource = new SourceRecord(Guid.NewGuid().ToString("N"), kind, source.Path, content.Identity,
                            source.OriginalName, digest, content.Bytes.Length, now, now, content.ModifiedAt);
                        await store.SaveOriginalAsync(lease, changedSource, content.Bytes, token);
                    }
                    throw new OperationCanceledException("確認中に資料または補完年度が変わりました。最新の資料を解析してください。");
                }
                if (!selecting && digest == source!.Digest)
                {
                    if (kind == MaterialKind.Changes && source.WeekdayConsent is not null && !source.WeekdayConsent.Matches(source, year, ParserVersion(kind)))
                        source = source with { WeekdayConsent = null };
                    source = source with { FileIdentity = content.Identity, LastCheckedAt = now, SourceModifiedAt = content.ModifiedAt };
                    await store.WriteAsync(lease, "selection." + kind, source, token);
                    await store.WriteAsync(lease, "acquisition." + kind, new MaterialAttempt(now, null, false, digest), token);
                    var analysis = await store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind, token);
                    var attempt = await store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token);
                    var version = ParserVersion(kind);
                    if (!reparsing && analysis?.DateDerivedWeekdays == (source.WeekdayConsent is not null) && analysis?.SourceDigest == digest && analysis.ParserVersion == version && (kind != MaterialKind.Changes || analysis.SchoolYear == year)) return new(kind, false, true);
                    if (!reparsing && attempt?.SourceDigest == digest && attempt.Failure is not null && attempt.ParserVersion == version && (kind != MaterialKind.Changes || attempt.SchoolYear == year)) return new(kind, false, false, attempt.Failure);
                    return await ParseAsync(lease, source, content.Bytes, year, false, token, consent is not null);
                }
                var next = new SourceRecord(Guid.NewGuid().ToString("N"), kind, Path.GetFullPath(selectedPath ?? source!.Path), content.Identity,
                    Path.GetFileName(selectedPath ?? source!.Path), digest, content.Bytes.Length, now, now, content.ModifiedAt);
                await store.SaveOriginalAsync(lease, next, content.Bytes, token);
                await store.WriteAsync(lease, "acquisition." + kind, new MaterialAttempt(now, null, false, digest), token);
                return await ParseAsync(lease, next, content.Bytes, year, true, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (SourceException failure)
            {
                await store.SaveAcquisitionFailureAsync(lease, kind, new MaterialAttempt(_clock.GetUtcNow(), failure.Message, false), token);
                return new(kind, false, false, failure.Message);
            }
        }
        finally { _gate.Release(); }
    }
    private async Task<MaterialResult> ParseAsync(SchoolLease lease, SourceRecord source, byte[] bytes, int year, bool changed, CancellationToken token, bool authorizeWeekdayCorrection = false)
    {
        var now = _clock.GetUtcNow();
        try
        {
            var parsed = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (source.Kind == MaterialKind.Changes)
                {
                    var useDate = authorizeWeekdayCorrection || source.WeekdayConsent?.Matches(source, year, XlsxChangeReader.Version) == true;
                    return new MaterialAnalysis(source.Id, source.Kind, XlsxChangeReader.Version, source.Digest, source.OriginalName, now, year, Changes: XlsxChangeReader.Parse(bytes, year, token, useDate)) { DateDerivedWeekdays = useDate };
                }
                var pages = PdfPigLayoutReader.Read(bytes, source.Kind, token);
                if (source.Kind == MaterialKind.Timetable)
                {
                    var timetable = PdfScheduleParser.Timetable(pages, token);
                    return new MaterialAnalysis(source.Id, source.Kind, PdfScheduleParser.TimetableVersion, source.Digest, source.OriginalName, now, timetable.SchoolYear, Timetable: timetable);
                }
                var special = PdfScheduleParser.Special(pages, source.Kind, token);
                return new MaterialAnalysis(source.Id, source.Kind, PdfScheduleParser.SpecialVersion, source.Digest, source.OriginalName, now, special.SchoolYear, Special: special);
            }, token);
            await store.SaveAnalysisAsync(lease, parsed, token, authorizeWeekdayCorrection);
            await store.CollectOriginalsAsync(lease, token);
            return new(source.Kind, changed, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (PdfParseException error)
        {
            var eligible = RecoveryPolicy.Eligible(source.Kind, error.Stage);
            var attempt = new MaterialAttempt(now, error.Stage, true, source.Digest, year, ParserVersion: ParserVersion(source.Kind), Page: error.Page, Cell: error.Cell, RecoveryPending: eligible);
            var job = eligible ? new RecoveryJob(source.Digest, RecoveryPolicy.Kind(source.Kind)!.Value, RecoveryJobState.Pending, now) : null;
            await store.SavePdfFailureAsync(lease, source, attempt, job, token);
            await store.CollectOriginalsAsync(lease, token);
            return new(source.Kind, changed, false, error.Message);
        }
        catch (ChangeParseException error)
        {
            await store.WriteAsync(lease, "attempt." + source.Kind, new MaterialAttempt(now, error.Message, true, source.Digest, year, error.Code, ParserVersion(source.Kind)), token);
            await store.CollectOriginalsAsync(lease, token);
            return new(source.Kind, changed, false, error.Message);
        }
    }
    public Task<MaterialResult> CorrectWeekdaysAsync(ChangePreview preview, int schoolYear, CancellationToken token = default)
    {
        if (!preview.CanCorrectWeekdays) throw new InvalidDataException("日付と曜日の不一致だけを補正できます。その他の警告は補正できません。");
        return Run(MaterialKind.Changes, null, schoolYear, false, true, token, preview);
    }
    public static int ParserVersion(MaterialKind kind) => kind == MaterialKind.Changes ? XlsxChangeReader.Version
        : kind == MaterialKind.Timetable ? PdfScheduleParser.TimetableVersion : PdfScheduleParser.SpecialVersion;
    public async Task<ChangePreview> PreviewChangesAsync(int schoolYear, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection.Changes", token);
            var attempt = await store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes", token);
            if (source is null || attempt?.SourceDigest != source.Digest || attempt.SchoolYear != schoolYear
                || attempt.ChangeError is not (ChangeErrorCode.FormulaCache or ChangeErrorCode.WeekdayMismatch))
                throw new InvalidDataException("資料または補完年度が変わっています。まず通常の解析をやり直してください。");
            var bytes = await store.ReadOriginalAsync(lease, source.Id, token);
            try
            {
                var preview = await Task.Run(() =>
                {
                    var table = XlsxChangeReader.ReadForPreview(bytes, schoolYear, token);
                    if (table.Warnings.Count == 0) throw new InvalidDataException("曜日の警告がない資料です。通常の解析を行ってください。");
                    return new ChangePreview(ChangeNormalizer.Parse(table.Rows, schoolYear, token), table.Warnings) { SourceId = source.Id, SourceDigest = source.Digest, SchoolYear = schoolYear };
                }, token);
                if (await store.BeginAsync(token) != lease) throw new OperationCanceledException();
                return preview;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _gate.Release(); }
    }
}
