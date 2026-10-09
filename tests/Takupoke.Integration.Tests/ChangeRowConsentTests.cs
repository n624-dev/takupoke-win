using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;
using Takupoke.Testing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class ChangeRowConsentTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-row-consent-" + Guid.NewGuid());
    private readonly Protector _protector = new();
    private readonly FakeIdentity _identity = new();
    private SchoolDataStore _store = null!;
    private MaterialCoordinator _coordinator = null!;
    private string FilePath => Path.Combine(_root, "完全架空変更.xlsx");
    private string DataRoot => Path.Combine(_root, "data");
    private MaterialAnalysis _previous = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _store = new(DataRoot, _protector);
        _coordinator = new(_store, new(_identity));
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Valid());
        Assert.True((await _coordinator.SelectAsync(MaterialKind.Changes, FilePath, 2032)).Parsed);
        _previous = (await Analysis())!;
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Create());
        Assert.False((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
    }

    [Fact]
    public async Task ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart()
    {
        var preview = await _coordinator.PreviewChangesAsync(2032);
        Assert.Equal([3, 4], preview.ReviewRows.Select(row => row.Row));
        Assert.Null((await Source()).RowSkipConsent);
        Assert.Equal(_previous.OriginalId, (await Analysis())!.OriginalId);
        await _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032);
        var accepted = (await Analysis())!;
        Assert.Equal(["架空科目A", "架空科目C"], accepted.Changes!.Select(change => change.AfterSubject));
        Assert.Equal([3, 4], (await Source()).RowSkipConsent!.Rows);
        Assert.Equal((await Source()).Id, accepted.OriginalId);
        Assert.True(accepted.RowSkipConsent!.SameAs((await Source()).RowSkipConsent));
        Assert.Null((await _store.ReadAsync<MaterialAttempt>(await _store.BeginAsync(), "attempt.Changes"))!.Failure);
        Assert.True((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.True((await _coordinator.ReparseAsync(MaterialKind.Changes, 2032)).Parsed);
        await _store.DisposeAsync();
        _store = new(DataRoot, _protector);
        _coordinator = new(_store, new(_identity));
        Assert.True((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.True((await _coordinator.ReparseAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.Equal([3, 4], (await Source()).RowSkipConsent!.Rows);
    }

    [Fact]
    public async Task ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection()
    {
        await Approve();
        var bytes = FictionalChangeWorkbook.Create();
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Create(sheet =>
            sheet.Descendants(FictionalChangeWorkbook.Namespace + "t").First(text => text.Value == "架空科目C")
                .Value = "架空更新科目D"));
        Assert.False((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.Null((await Source()).RowSkipConsent);
        await File.WriteAllBytesAsync(FilePath, bytes);
        Assert.False((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.Null((await Source()).RowSkipConsent);
        await Approve();
        Assert.False((await _coordinator.ReparseAsync(MaterialKind.Changes, 2033)).Parsed);
        Assert.Null((await Source()).RowSkipConsent);
        Assert.False((await _coordinator.ReparseAsync(MaterialKind.Changes, 2032)).Parsed);
        await Approve();
        Assert.False((await _coordinator.SelectAsync(MaterialKind.Changes, FilePath, 2032)).Parsed);
        Assert.Null((await Source()).RowSkipConsent);
    }

    [Fact]
    public async Task OldParserConsentIsClearedEvenIfTheContentIsUnchanged()
    {
        await Approve();
        var source = await Source();
        await _store.WriteAsync(await _store.BeginAsync(), "selection.Changes", source with
        { RowSkipConsent = source.RowSkipConsent! with { ParserVersion = XlsxChangeReader.Version - 1 } });
        Assert.False((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        Assert.Null((await Source()).RowSkipConsent);
        Assert.NotNull((await Analysis())!.RowSkipConsent);
    }

    [Fact]
    public async Task StalePreviewMissingSelectionCancellationAndReplacementCannotSave()
    {
        var preview = await _coordinator.PreviewChangesAsync(2032);
        await Assert.ThrowsAsync<InvalidDataException>(() => _coordinator.ApplyRowSkipsAsync(preview, [], 2032));
        await Assert.ThrowsAsync<InvalidDataException>(() => _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2033));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032, cancellation.Token));
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Valid());
        await Assert.ThrowsAsync<InvalidDataException>(() => _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032));
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Create());
        _identity.Value = "another-fictional-file";
        await Assert.ThrowsAsync<SourceException>(() => _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032));
        Assert.Null((await Source()).RowSkipConsent);
        Assert.Equal(_previous.OriginalId, (await Analysis())!.OriginalId);
    }

    [Fact]
    public async Task ExcludingEveryChangeRefusesAndPreservesLastGood()
    {
        await File.WriteAllBytesAsync(FilePath, FictionalChangeWorkbook.Create(sheet =>
            sheet.Descendants(FictionalChangeWorkbook.Namespace + "row")
                .Where(row => (string?)row.Attribute("r") is "2" or "5").ToArray()
                .ToList().ForEach(row => row.Remove())));
        Assert.False((await _coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
        var preview = await _coordinator.PreviewChangesAsync(2032);
        Assert.Single(preview.Changes);
        Assert.Equal("架空除外科目B", preview.Changes[0].AfterSubject);
        Assert.Equal(ChangeErrorCode.Empty, (await Assert.ThrowsAsync<ChangeParseException>(() =>
            _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032))).Code);
        Assert.Null((await Source()).RowSkipConsent);
        Assert.Equal(_previous.OriginalId, (await Analysis())!.OriginalId);
    }

    [Fact]
    public async Task StorageFailureRollsBackConsentAnalysisAndAttemptTogether()
    {
        var preview = await _coordinator.PreviewChangesAsync(2032);
        var source = await Source();
        var lease = await _store.BeginAsync();
        var beforeAttempt = await _store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes");
        var unapproved = new MaterialAnalysis(source.Id, MaterialKind.Changes, XlsxChangeReader.Version,
            source.Digest, source.OriginalName, DateTimeOffset.UtcNow, 2032, Changes: _previous.Changes,
            RowSkipConsent: new(source.Id, source.Digest, 2032, XlsxChangeReader.Version, [3, 4]));
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAnalysisAsync(lease, unapproved));
        await using (var connection = new SqliteConnection("Data Source=" + Path.Combine(DataRoot, "school", "school.sqlite")))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fictional_failure BEFORE UPDATE ON entry " +
                "WHEN NEW.key='attempt.Changes' BEGIN SELECT RAISE(ABORT,'fictional write refusal'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => _coordinator.ApplyRowSkipsAsync(preview, [3, 4], 2032));
        Assert.Null((await Source()).RowSkipConsent);
        Assert.Equal(_previous.OriginalId, (await Analysis())!.OriginalId);
        Assert.Equal(beforeAttempt, await _store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes"));
    }

    [Fact]
    public async Task PreviousPayloadsWithoutOptionalConsentRemainReadable()
    {
        var json = JsonNode.Parse(DataCodec.Encode(await Source()))!.AsObject();
        json.Remove("rowSkipConsent");
        Assert.Null(DataCodec.Decode<SourceRecord>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(json)).RowSkipConsent);
        json = JsonNode.Parse(DataCodec.Encode(_previous))!.AsObject(); json.Remove("rowSkipConsent");
        Assert.Null(DataCodec.Decode<MaterialAnalysis>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(json)).RowSkipConsent);
    }

    private async Task Approve() => await _coordinator.ApplyRowSkipsAsync(
        await _coordinator.PreviewChangesAsync(2032), [3, 4], 2032);
    private async Task<SourceRecord> Source() => (await _store.ReadAsync<SourceRecord>(
        await _store.BeginAsync(), "selection.Changes"))!;
    private async Task<MaterialAnalysis?> Analysis() => await _store.ReadAsync<MaterialAnalysis>(
        await _store.BeginAsync(), "analysis.Changes");
    public async Task DisposeAsync()
    {
        await _store.DisposeAsync(); _protector.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class FakeIdentity : IFileIdentityProvider
    {
        public string Value { get; set; } = "fictional-owned-file";
        public string Identity(FileStream stream) => Value;
    }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fictional-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-key");
        public void Dispose() => _cipher.Dispose();
    }
}
