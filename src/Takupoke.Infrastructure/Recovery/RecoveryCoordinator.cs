using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

public sealed record RecoveryPreparation(RecoveryJobState State, RecoveryPreview? Preview, string Message, bool ReusedAcceptance = false);
public sealed class RecoveryCoordinator(SchoolDataStore store, MaterialCoordinator materials,
    Func<byte[], MaterialKind, string, RecoveryReadCapture, CancellationToken, Task<RecoveryDocument>> buildDocument,
    Func<CancellationToken, Task<IReadOnlyList<ILocalRecoveryProvider>>> providers, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public async Task<RecoveryPreparation> PrepareAsync(MaterialKind kind, int schoolYear, CancellationToken token = default)
    {
        if (RecoveryPolicy.Kind(kind) is null) throw new InvalidOperationException("この資料はPDF復旧の対象外です。");
        await _gate.WaitAsync(token);
        try
        {
            // Explicit foreground requests still give Strict Parser the first opportunity.
            var strict = await materials.ReparseAsync(kind, schoolYear, token);
            if (strict.Parsed) return new(RecoveryJobState.Adopted, null, "通常の方法で解析できました。");
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
            var job = pendingJob with { State = RecoveryJobState.Preparing, ResultHash = null };
            await store.SaveRecoveryProgressAsync(lease, source, job, null, token);
            var bytes = await store.ReadOriginalAsync(lease, source.Id, token);
            try
            {
                if (NotificationDiff.Digest(bytes) != source.Digest) throw new InvalidDataException("保存した原本のハッシュが一致しません。");
                var capture = new RecoveryReadCapture();
                try { await Task.Run(() => PdfPigLayoutReader.Read(bytes, kind, token, capture), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (PdfParseException failure) when (RecoveryPolicy.Eligible(kind, failure.Stage)) { }
                var document = await buildDocument(bytes, kind, source.Digest, capture, token);
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
    public async Task AdoptAsync(MaterialKind kind, RecoveryPreview preview, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
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
            await store.SaveRecoveryAsync(lease, source, new(preview.Document, preview.Result, acceptance), acceptance.AcceptedAt, token);
            await store.CollectOriginalsAsync(lease, token);
        }
        finally { _gate.Release(); }
    }
}
