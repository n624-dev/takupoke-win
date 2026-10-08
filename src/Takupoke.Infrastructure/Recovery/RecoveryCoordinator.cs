using System.Security.Cryptography;
using System.Collections.Concurrent;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

public sealed record RecoveryPreparation(RecoveryJobState State, RecoveryPreview? Preview, string Message, bool ReusedAcceptance = false)
{ public RecoveryManualSession? ManualSession { get; init; } }
public sealed class RecoveryCoordinator(SchoolDataStore store, MaterialCoordinator materials,
    Func<byte[], MaterialKind, string, RecoveryReadCapture, CancellationToken, Task<RecoveryDocument>> buildDocument,
    Func<CancellationToken, Task<IReadOnlyList<ILocalRecoveryProvider>>> providers, TimeProvider? clock = null, AiFeaturePermission? aiPermission = null)
{
    private readonly ConcurrentDictionary<string, long> _previewPermissions = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    // Restore existing rule-only reviews without allowing an OFF/ON change to
    // rebind a review already observed in this process. AI reviews need preparation.
    public void TrackRestoredRulePreview(RecoveryPreview preview)
    {
        if (aiPermission is not null && !AiFeaturePermission.UsesAi(preview.Document, preview.Result))
            _previewPermissions.TryAdd(preview.SourceId, aiPermission.Capture().Generation);
    }
    public void TrackRestoredRuleManual(RecoveryManualSession session)
    {
        if (aiPermission is not null && session.Plan.Document.StructureMetadata is not { Provider: not "rule" })
            _previewPermissions.TryAdd(session.SourceId, aiPermission.Capture().Generation);
    }
    public bool CanReview(string sourceId)
    {
        if (aiPermission is null) return true;
        var permission = aiPermission.Capture();
        try { aiPermission.Check(permission.Generation); }
        catch (OperationCanceledException) { return false; }
        return _previewPermissions.TryGetValue(sourceId, out var prepared) && prepared == permission.Generation;
    }
    public async Task<RecoveryPreparation> PrepareAsync(MaterialKind kind, int schoolYear, CancellationToken token = default)
    {
        var permission = aiPermission?.Capture();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, permission?.Token ?? CancellationToken.None);
        token = operation.Token;
        if (RecoveryPolicy.Kind(kind) is null) throw new InvalidOperationException("この資料はPDF復旧の対象外です。");
        await _gate.WaitAsync(token);
        try
        {
            // Explicit foreground requests still give Strict Parser the first opportunity.
            var strict = await materials.ReparseAsync(kind, schoolYear, token);
            if (strict.Parsed) return new(RecoveryJobState.Adopted, null, "通常の方法で解析できました。");
            if (aiPermission?.Enabled == false) return new(RecoveryJobState.Failed, null, "復旧はOFFです。");
            if (permission is { } strictCompleted) aiPermission!.Check(strictCompleted.Generation);
            var lease = await store.BeginAsync(token);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection." + kind, token) ?? throw new InvalidDataException("選択したPDFがありません。");
            var acquisition = await store.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, token);
            if (acquisition?.Failure is not null)
                return new(RecoveryJobState.Failed, null, acquisition.Failure + " 前回の正常結果を保持しています。");
            var attempt = await store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token);
            if (attempt?.SourceDigest != source.Digest || attempt.Failure is null || !RecoveryPolicy.Eligible(kind, attempt.Failure))
                return new(RecoveryJobState.Failed, null, "原本の破損・保護・入力上限など、この失敗は端末内復旧の対象外です。前回の正常結果を保持しています。");
            var cached = await store.ReadAsync<RecoveryAudit>(lease, "recovery.accepted." + kind + "." + source.Digest, token);
            RecoveryAudit? reusable = null;
            if (cached is not null && RecoveryPolicy.MatchesPeriod(cached.Document, lease.Period))
                reusable = await Task.Run(() => RecoveryAuditCertification.Reusable(cached, token), token).ConfigureAwait(false);
            if (reusable is not null)
            {
                await store.SaveRecoveryAsync(lease, source, reusable, _clock.GetUtcNow(), token, reuseAccepted: true);
                return new(RecoveryJobState.Adopted, null, "以前に確認した同じPDFの復旧結果を使用しました。", true);
            }
            var pendingJob = await store.ReadAsync<RecoveryJob>(lease, "recovery." + kind, token);
            if (pendingJob is null || pendingJob.PdfHash != source.Digest || pendingJob.Kind != RecoveryPolicy.Kind(kind))
                throw new OperationCanceledException("復旧待ちの資料が更新されました。");
            if (permission is { } initial) aiPermission!.Check(initial.Generation);
            var job = pendingJob with { State = RecoveryJobState.Preparing, ResultHash = null, ManualPlan = null };
            await store.SaveRecoveryProgressAsync(lease, source, job, null, token);
            var bytes = await store.ReadOriginalAsync(lease, source.Id, token);
            try
            {
                if (NotificationDiff.Digest(bytes) != source.Digest) throw new InvalidDataException("保存した原本のハッシュが一致しません。");
                var capture = new RecoveryReadCapture();
                try { await Task.Run(() => PdfPigLayoutReader.Read(bytes, kind, token, capture), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (PdfParseException failure) when (RecoveryPolicy.Eligible(kind, failure.Stage)) { }
                await Task.Run(() => PdfPigLayoutReader.ImproveMissingUnicodeCapture(bytes, kind, capture, token), token).ConfigureAwait(false);
                var document = await buildDocument(bytes, kind, source.Digest, capture, token);
                token.ThrowIfCancellationRequested();
                if (permission is { } captured) aiPermission!.Check(captured.Generation);
                if (!RecoveryPolicy.MatchesPeriod(document, lease.Period)) throw new InvalidDataException("PDFの年度・学期が現在の保存期間と一致しません。");
                var localProviders = await providers(token); RecoveryRun run;
                try
                {
                    var structure = await Task.Run(() => RecoveryStructure.ResolveAsync(document, "windows", Environment.OSVersion.Version.Major, localProviders, token), token).ConfigureAwait(false);
                    if (structure.Document is null) run = new(structure.State, null, structure.Errors);
                    else
                    {
                        document = structure.Document;
                        run = await Task.Run(() => RecoveryEngine.RunAsync(document, "windows", Environment.OSVersion.Version.Major, true, localProviders, _ => null, token), token).ConfigureAwait(false);
                    }
                }
                finally { foreach (var provider in localProviders.OfType<IAsyncDisposable>()) await provider.DisposeAsync(); }
                if (permission is { } completed)
                {
                    aiPermission!.Check(completed.Generation);
                    _previewPermissions[source.Id] = completed.Generation;
                }
                if (run.Result is null && document.Sources.Any(s => s.NativeConfidence is < .8))
                {
                    var manual = await Task.Run(() => RecoveryManualAssistance.Prepare(document, token), token).ConfigureAwait(false);
                    if (manual is not null)
                    {
                        var session = new RecoveryManualSession(source.Id, lease, manual, job.CreatedAt);
                        await store.SaveRecoveryProgressAsync(lease, source, job with { State = RecoveryJobState.AwaitingManualCorrection, ManualPlan = manual }, null, token);
                        return new(RecoveryJobState.AwaitingManualCorrection, null, $"原本と読み取り文字を確認して、資料全体の未確定{manual.Targets.Count}項目を補正できます。補正後に全体を確認してから採用してください。") { ManualSession = session };
                    }
                }
                var preview = run.Result is not null ? new RecoveryPreview(source.Id, lease, document, run.Result, _clock.GetUtcNow()) : null;
                await store.SaveRecoveryProgressAsync(lease, source, job with { State = run.State, ResultHash = run.Result is null ? null : RecoveryValidator.Fingerprint(run.Result) }, preview, token);
                return new(run.State, preview, run.State switch {
                    RecoveryJobState.AwaitingConfirmation => "原本と読み取り結果を確認し、使用する場合は採用してください。",
                    RecoveryJobState.AwaitingModel => "利用できる端末内モデルがありません。AIモデルの準備を確認してください。",
                    _ => "内容の完全性を確認できませんでした。前回の正常結果を保持しています。" });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure) when (failure is InvalidDataException or PdfParseException or InvalidRecoveryOutputException)
            {
                await store.SaveRecoveryProgressAsync(lease, source, job with { State = RecoveryJobState.Failed }, null, token);
                return new(RecoveryJobState.Failed, null, "復旧できませんでした。" + failure.Message + " 前回の正常結果を保持しています。");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _gate.Release(); }
    }
    public async Task<RecoveryPreview> CompleteManualAsync(MaterialKind kind, RecoveryManualSession session,
        IReadOnlyDictionary<string, string> values, CancellationToken token = default)
    {
        var permission = aiPermission?.Capture();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, permission?.Token ?? CancellationToken.None); token = operation.Token;
        await _gate.WaitAsync(token);
        try
        {
            if (permission is { } current)
            {
                aiPermission!.Check(current.Generation, requireEnabled: true);
                if (!_previewPermissions.TryGetValue(session.SourceId, out var preparedGeneration) || preparedGeneration != current.Generation)
                    throw new OperationCanceledException("確認結果は無効になっています。復旧をやり直してください。");
            }
            if (await store.BeginAsync(token) != session.Lease) throw new OperationCanceledException("保存期間または利用状態が変わりました。");
            await materials.RefreshAsync(kind, session.Lease.Period.SchoolYear, token);
            if (await store.BeginAsync(token) != session.Lease) throw new OperationCanceledException("保存期間または利用状態が変わりました。");
            var acquisition = await store.ReadAsync<MaterialAttempt>(session.Lease, "acquisition." + kind, token);
            if (acquisition?.Failure is not null) throw new InvalidDataException(acquisition.Failure);
            var source = await store.ReadAsync<SourceRecord>(session.Lease, "selection." + kind, token);
            var job = await store.ReadAsync<RecoveryJob>(session.Lease, "recovery." + kind, token);
            if (source?.Id != session.SourceId || source.Digest != session.Plan.Document.PdfHash ||
                job?.State != RecoveryJobState.AwaitingManualCorrection || job.CreatedAt != session.CreatedAt || job.ManualPlan is null ||
                RecoveryValidator.Fingerprint(job.ManualPlan) != RecoveryValidator.Fingerprint(session.Plan))
                throw new OperationCanceledException("確認中にPDFまたは補正対象が更新されました。現在の資料を読み直してください。");
            var plan = job.ManualPlan;
            if (values.Count != plan.Targets.Count || !values.Keys.ToHashSet().SetEquals(plan.Targets.Select(t => t.Target.Key)))
                throw new InvalidRecoveryOutputException();
            var now = _clock.GetUtcNow();
            var corrections = plan.Targets.Select(t => new RecoveryHumanCorrection(t.Target, source.Digest,
                RecoveryValidator.Fingerprint(plan.Document.Capture), plan.DocumentSnapshot, t.OriginalParentIds, t.Page, t.Crop,
                values[t.Target.Key], false, now)).ToArray();
            var result = await Task.Run(() => RecoveryManualAssistance.Complete(plan, corrections, "windows:" + Environment.OSVersion.Version.Major, token), token).ConfigureAwait(false);
            var preview = new RecoveryPreview(source.Id, session.Lease, plan.Document, result, now);
            if (permission is { } final) aiPermission!.Check(final.Generation);
            await store.SaveRecoveryProgressAsync(session.Lease, source, job with { State = RecoveryJobState.AwaitingConfirmation, ManualPlan = null, ResultHash = RecoveryValidator.Fingerprint(result) }, preview, token);
            return preview;
        }
        finally { _gate.Release(); }
    }
    public async Task CancelManualAsync(MaterialKind kind, RecoveryManualSession session, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (await store.BeginAsync(token) != session.Lease) throw new OperationCanceledException();
            var source = await store.ReadAsync<SourceRecord>(session.Lease, "selection." + kind, token);
            var job = await store.ReadAsync<RecoveryJob>(session.Lease, "recovery." + kind, token);
            if (source?.Id != session.SourceId || source.Digest != session.Plan.Document.PdfHash || job?.CreatedAt != session.CreatedAt ||
                job.ManualPlan is null || RecoveryValidator.Fingerprint(job.ManualPlan) != RecoveryValidator.Fingerprint(session.Plan)) throw new OperationCanceledException();
            await store.SaveRecoveryProgressAsync(session.Lease, source, job with { State = RecoveryJobState.Failed, ManualPlan = null }, null, token);
        }
        finally { _gate.Release(); }
    }
    public async Task AdoptAsync(MaterialKind kind, RecoveryPreview preview, CancellationToken token = default)
    {
        var permission = aiPermission?.Capture();
        if (permission is { } initial) aiPermission!.Check(initial.Generation);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, permission?.Token ?? CancellationToken.None);
        token = operation.Token;
        await _gate.WaitAsync(token);
        try
        {
            if (permission is { } current &&
                (!_previewPermissions.TryGetValue(preview.SourceId, out var preparedGeneration) || preparedGeneration != current.Generation))
                throw new OperationCanceledException("確認結果は無効になっています。復旧をやり直してください。");
            var lease = await store.BeginAsync(token);
            if (lease != preview.Lease) throw new OperationCanceledException("保存期間または学校データの利用状態が変わりました。");
            // Confirmation must still refer to the selected path's latest readable version.
            await materials.RefreshAsync(kind, lease.Period.SchoolYear, token);
            lease = await store.BeginAsync(token);
            if (lease != preview.Lease) throw new OperationCanceledException("保存期間または学校データの利用状態が変わりました。");
            var acquisition = await store.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, token);
            if (acquisition?.Failure is not null) throw new InvalidDataException(acquisition.Failure);
            var source = await store.ReadAsync<SourceRecord>(lease, "selection." + kind, token) ?? throw new OperationCanceledException("資料の選択が変わりました。");
            if (source.Id != preview.SourceId || source.Digest != preview.Document.PdfHash) throw new OperationCanceledException("確認中にPDFが更新されました。新しいPDFを確認してください。");
            var persisted = await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview." + kind, token);
            if (persisted is null || persisted.SourceId != preview.SourceId || RecoveryValidator.Fingerprint(persisted.Document) != RecoveryValidator.Fingerprint(preview.Document) || RecoveryValidator.Fingerprint(persisted.Result) != RecoveryValidator.Fingerprint(preview.Result))
                throw new InvalidDataException("現在の確認用結果との対応を確認できません。");
            var acceptance = new RecoveryAcceptance(source.Digest, RecoveryValidator.Fingerprint(preview.Result), RecoveryValidator.Fingerprint(preview.Document), preview.Result.Metadata, _clock.GetUtcNow());
            if (permission is { } final) aiPermission!.Check(final.Generation);
            await store.SaveRecoveryAsync(lease, source, new(preview.Document, preview.Result, acceptance), acceptance.AcceptedAt, token);
            await store.CollectOriginalsAsync(lease, token);
        }
        finally { _gate.Release(); }
    }
}
