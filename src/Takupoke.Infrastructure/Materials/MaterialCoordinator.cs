using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Materials;

public sealed record MaterialResult(MaterialKind Kind, bool Changed, bool Parsed, string? Error = null);
public sealed partial class MaterialCoordinator(SchoolDataStore store, FileSourceReader reader, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public Task<MaterialResult> SelectAsync(MaterialKind kind, string path, int schoolYear, CancellationToken token = default) => Run(kind, path, schoolYear, true, false, token);
    public Task<MaterialResult> RefreshAsync(MaterialKind kind, int schoolYear, CancellationToken token = default) => Run(kind, null, schoolYear, false, false, token);
    public Task<MaterialResult> ReparseAsync(MaterialKind kind, int schoolYear, CancellationToken token = default) => Run(kind, null, schoolYear, false, true, token);
    private async Task<MaterialResult> Run(MaterialKind kind, string? selectedPath, int year, bool selecting, bool reparsing, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection." + kind, token);
            if (!selecting && source is null) return new(kind, false, false);
            if (reparsing)
            {
                var bytes = await store.ReadOriginalAsync(lease, source!.Id, token);
                try { return await ParseAsync(lease, source, bytes, year, false, token); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            try
            {
                using var content = await reader.ReadAsync(selectedPath ?? source!.Path, kind, selecting ? null : source!.FileIdentity, token);
                var digest = NotificationDiff.Digest(content.Bytes); var now = _clock.GetUtcNow();
                if (!selecting && digest == source!.Digest)
                {
                    source = await ClearStaleRowSkipsAsync(lease, source, year, token);
                    await store.WriteAsync(lease, "selection." + kind, source with { LastCheckedAt = now }, token);
                    await store.WriteAsync(lease, "acquisition." + kind, new MaterialAttempt(now, null, false, digest), token);
                    var analysis = await store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind, token);
                    var attempt = await store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token);
                    var version = ParserVersion(kind);
                    if (analysis?.SourceDigest == digest && analysis.ParserVersion == version
                        && (kind != MaterialKind.Changes || analysis.SchoolYear == year &&
                            RowSkipMatches(analysis, source, year))) return new(kind, false, true);
                    if (attempt?.SourceDigest == digest && attempt.Failure is not null && attempt.ParserVersion == version && (kind != MaterialKind.Changes || attempt.SchoolYear == year)) return new(kind, false, false, attempt.Failure);
                    return await ParseAsync(lease, source with { LastCheckedAt = now }, content.Bytes, year, false, token);
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
                await store.WriteAsync(lease, "acquisition." + kind, new MaterialAttempt(_clock.GetUtcNow(), failure.Message, false), token);
                return new(kind, false, false, failure.Message);
            }
        }
        finally { _gate.Release(); }
    }
    private async Task<MaterialResult> ParseAsync(SchoolLease lease, SourceRecord source, byte[] bytes, int year, bool changed, CancellationToken token)
    {
        source = await ClearStaleRowSkipsAsync(lease, source, year, token);
        var now = _clock.GetUtcNow();
        try
        {
            var parsed = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (source.Kind == MaterialKind.Changes)
                    return new MaterialAnalysis(source.Id, source.Kind, XlsxChangeReader.Version,
                        source.Digest, source.OriginalName, now, year,
                        Changes: XlsxChangeReader.Parse(bytes, year, token,
                            source.RowSkipConsent?.Rows.ToHashSet()), RowSkipConsent: source.RowSkipConsent);
                var pages = PdfPigLayoutReader.Read(bytes, source.Kind, token);
                if (source.Kind == MaterialKind.Timetable)
                {
                    var timetable = PdfScheduleParser.Timetable(pages, token);
                    return new MaterialAnalysis(source.Id, source.Kind, PdfScheduleParser.TimetableVersion, source.Digest, source.OriginalName, now, timetable.SchoolYear, Timetable: timetable);
                }
                var special = PdfScheduleParser.Special(pages, source.Kind, token);
                return new MaterialAnalysis(source.Id, source.Kind, PdfScheduleParser.SpecialVersion, source.Digest, source.OriginalName, now, special.SchoolYear, Special: special);
            }, token);
            await store.SaveAnalysisAsync(lease, parsed, token);
            await store.CollectOriginalsAsync(lease, token);
            return new(source.Kind, changed, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (PdfParseException error)
        {
            await store.WriteAsync(lease, "attempt." + source.Kind, new MaterialAttempt(now, error.Stage, true, source.Digest, year, ParserVersion: ParserVersion(source.Kind), Page: error.Page, Cell: error.Cell), token);
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
    public static int ParserVersion(MaterialKind kind) => kind == MaterialKind.Changes ? XlsxChangeReader.Version
        : kind == MaterialKind.Timetable ? PdfScheduleParser.TimetableVersion : PdfScheduleParser.SpecialVersion;
}
