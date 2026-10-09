using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Storage;

/// <summary>Every private read/write is period- and generation-checked; SQLite stores encrypted payloads only.</summary>
public sealed class SchoolDataStore(string root, IKeyProtector protector, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly string _root = Path.GetFullPath(root);
    private string PrivateRoot => Path.Combine(_root, "school");
    private string MarkerFile => Path.Combine(_root, "school-data-period.json");
    private string DatabaseFile => Path.Combine(PrivateRoot, "school.sqlite");
    private SchoolDataPeriod? _period;
    private EnvelopeCipher? _cipher;
    private long _generation;
    private bool _available = true;
    public event Action? RetentionChanged;
    public bool ProtectedDataAvailable => Volatile.Read(ref _available);

    public async Task SetProtectedDataAvailableAsync(bool available)
    {
        Volatile.Write(ref _available, available);
        Interlocked.Increment(ref _generation);
        await _gate.WaitAsync();
        try { _cipher?.Dispose(); _cipher = null; }
        finally { _gate.Release(); }
    }
    public async Task<SchoolLease> BeginAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await EnsurePeriodAsync(token); return new(Interlocked.Read(ref _generation), _period!.Value); }
        finally { _gate.Release(); }
    }
    private void Verify(SchoolLease lease)
    {
        if (!ProtectedDataAvailable || lease.Generation != Interlocked.Read(ref _generation) || lease.Period != SchoolDataPeriod.FromInstant(_clock.GetUtcNow()) || _period != lease.Period)
            throw new OperationCanceledException("学校データの保存期間または利用状態が変わりました。");
    }
    private async Task EnsurePeriodAsync(CancellationToken token)
    {
        if (!ProtectedDataAvailable) throw new OperationCanceledException("端末のロック中は学校データを読み取りません。");
        Directory.CreateDirectory(_root);
        var current = SchoolDataPeriod.FromInstant(_clock.GetUtcNow());
        RetentionMarker? marker = null;
        if (File.Exists(MarkerFile)) marker = DataCodec.Decode<RetentionMarker>(await File.ReadAllBytesAsync(MarkerFile, token));
        if (marker is not null && (marker.SchemaVersion != 1 || marker.SchoolYear is < 1900 or > 9998 || marker.Half is < 1 or > 2)) throw new InvalidDataException("学校データの期間管理情報を確認できません。");
        var matches = marker is not null && marker.SchoolYear == current.SchoolYear && marker.Half == current.Half;
        if (!matches)
        {
            Interlocked.Increment(ref _generation);
            _period = null;
            _cipher?.Dispose(); _cipher = null;
            RetentionChanged?.Invoke();
            // Deletion failure leaves the old marker and blocks all school-data access until retry.
            if (Directory.Exists(PrivateRoot)) Directory.Delete(PrivateRoot, recursive: true);
            var initializing = PrivateRoot + ".initializing";
            if (Directory.Exists(initializing)) Directory.Delete(initializing, recursive: true);
            Directory.CreateDirectory(initializing);
            try
            {
                var key = RandomNumberGenerator.GetBytes(32);
                try { await DataCodec.AtomicWriteAsync(Path.Combine(initializing, "key.dpapi"), protector.Protect(key), token); }
                finally { CryptographicOperations.ZeroMemory(key); }
                await using (var connection = NewConnection(Path.Combine(initializing, "school.sqlite"), create: true))
                {
                    await connection.OpenAsync(token);
                    await ExecuteAsync(connection, "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA application_id=1414221899; PRAGMA user_version=1; CREATE TABLE entry(key TEXT PRIMARY KEY NOT NULL, payload BLOB NOT NULL); CREATE TABLE original(id TEXT PRIMARY KEY NOT NULL, payload BLOB NOT NULL, content BLOB NOT NULL);", token);
                }
                await using (var check = NewConnection(Path.Combine(initializing, "school.sqlite"))) { await check.OpenAsync(token); await ValidateDatabaseAsync(check, token); }
                Directory.Move(initializing, PrivateRoot);
                await DataCodec.AtomicWriteAsync(MarkerFile, DataCodec.Encode(new RetentionMarker(1, current.SchoolYear, current.Half)), token);
            }
            finally { if (Directory.Exists(initializing)) Directory.Delete(initializing, recursive: true); }
        }
        if (!File.Exists(DatabaseFile) || !File.Exists(Path.Combine(PrivateRoot, "key.dpapi"))) throw new InvalidDataException("保存情報の一部がありません。既存データを保護するため更新を停止しました。");
        _period = current;
        if (_cipher is null)
        {
            var key = protector.Unprotect(await File.ReadAllBytesAsync(Path.Combine(PrivateRoot, "key.dpapi"), token));
            try { if (key.Length != 32) throw new InvalidDataException("学校データの鍵を確認できません。"); _cipher = new(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
    }
    private static SqliteConnection NewConnection(string file, bool create = false) => new(new SqliteConnectionStringBuilder
    { DataSource = file, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5 }.ToString());
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token)
    { using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(token); }
    private static async Task ValidateDatabaseAsync(SqliteConnection connection, CancellationToken token)
    {
        async Task<object?> Scalar(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(token); }
        if (Convert.ToInt64(await Scalar("PRAGMA application_id")) != 1414221899 || Convert.ToInt64(await Scalar("PRAGMA user_version")) != 1
            || !string.Equals((string?)await Scalar("PRAGMA journal_mode"), "delete", StringComparison.OrdinalIgnoreCase)
            || (string?)await Scalar("PRAGMA quick_check") != "ok") throw new InvalidDataException("端末内の保存情報を確認できません。更新を停止しました。");
        await ExecuteAsync(connection, "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;", token);
    }
    private async Task<T> WithConnectionAsync<T>(SchoolLease lease, Func<SqliteConnection, Task<T>> action, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await EnsurePeriodAsync(token); Verify(lease);
            await using var connection = NewConnection(DatabaseFile);
            await connection.OpenAsync(token); await ValidateDatabaseAsync(connection, token);
            var result = await action(connection);
            try { Verify(lease); }
            catch { if (result is byte[] bytes) CryptographicOperations.ZeroMemory(bytes); throw; }
            return result;
        }
        finally { _gate.Release(); }
    }
    public Task<T?> ReadAsync<T>(SchoolLease lease, string key, CancellationToken token = default) => WithConnectionAsync(lease, async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM entry WHERE key=$key"; command.Parameters.AddWithValue("$key", key);
        var bytes = await command.ExecuteScalarAsync(token) as byte[];
        if (bytes is null) return default(T);
        var plain = _cipher!.Decrypt(bytes, key);
        try { return DataCodec.Decode<T>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }, token);
    public Task WriteAsync<T>(SchoolLease lease, string key, T value, CancellationToken token = default) => WithConnectionAsync(lease, async connection =>
    {
        var plain = DataCodec.Encode(value);
        try
        {
            var cipher = _cipher!.Encrypt(plain, key); Verify(lease);
            using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO entry(key,payload) VALUES($key,$payload) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload";
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$payload", cipher); await command.ExecuteNonQueryAsync(token);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return true;
    }, token);
    public Task SaveOriginalAsync(SchoolLease lease, SourceRecord source, byte[] content, CancellationToken token = default) => WithConnectionAsync(lease, async connection =>
    {
        Verify(lease);
        if (content.Length != source.ByteCount || NotificationDiff.Digest(content) != source.Digest) throw new InvalidDataException("原本と保存情報の対応を確認できません。");
        using var transaction = connection.BeginTransaction();
        var plain = DataCodec.Encode(source);
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandText = "INSERT INTO original(id,payload,content) VALUES($id,$payload,$content)";
                command.Parameters.AddWithValue("$id", source.Id);
                command.Parameters.AddWithValue("$payload", _cipher!.Encrypt(plain, "original:" + source.Id));
                command.Parameters.AddWithValue("$content", _cipher.Encrypt(content, "content:" + source.Id));
                await command.ExecuteNonQueryAsync(token);
            }
            var key = "selection." + source.Kind;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandText = "INSERT INTO entry(key,payload) VALUES($key,$payload) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload";
                command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$payload", _cipher!.Encrypt(plain, key));
                await command.ExecuteNonQueryAsync(token);
            }
            Verify(lease); await transaction.CommitAsync(token);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return true;
    }, token);
    public Task<byte[]> ReadOriginalAsync(SchoolLease lease, string id, CancellationToken token = default) => WithConnectionAsync(lease, async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT content FROM original WHERE id=$id"; command.Parameters.AddWithValue("$id", id);
        if (await command.ExecuteScalarAsync(token) is not byte[] cipher) throw new InvalidDataException("保存した原本を読み取れません。");
        return _cipher!.Decrypt(cipher, "content:" + id);
    }, token);
    public Task SaveAnalysisAsync(SchoolLease lease, MaterialAnalysis analysis,
        CancellationToken token = default, bool authorizeRowSkips = false) => WithConnectionAsync(lease, async connection =>
    {
        var selectionKey = "selection." + analysis.Kind;
        using var transaction = connection.BeginTransaction();
        SourceRecord source;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction; read.CommandText = "SELECT payload FROM entry WHERE key=$key"; read.Parameters.AddWithValue("$key", selectionKey);
            if (await read.ExecuteScalarAsync(token) is not byte[] payload) throw new InvalidDataException("解析元の選択情報がありません。");
            var bytes = _cipher!.Decrypt(payload, selectionKey);
            try
            {
                source = DataCodec.Decode<SourceRecord>(bytes);
                if (source.Id != analysis.OriginalId || source.Digest != analysis.SourceDigest) throw new OperationCanceledException("解析中に選択資料が変わりました。");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        if (analysis.RowSkipConsent is { } consent)
        {
            if (!consent.ValidFor(source, analysis.SchoolYear, analysis.ParserVersion)
                || analysis.Changes is not { Count: > 0 }
                || !authorizeRowSkips && !consent.SameAs(source.RowSkipConsent))
                throw new InvalidDataException("行の除外許可と解析元の対応を確認できません。");
        }
        else if (source.RowSkipConsent?.ValidFor(source, analysis.SchoolYear, analysis.ParserVersion) == true)
            throw new InvalidDataException("許可した行の除外を省いた結果は保存できません。");
        var writes = new List<(string, byte[])>
        {
            ("analysis." + analysis.Kind, DataCodec.Encode(analysis)),
            ("attempt." + analysis.Kind, DataCodec.Encode(new MaterialAttempt(analysis.ParsedAt, null, true, analysis.SourceDigest, analysis.SchoolYear, ParserVersion: analysis.ParserVersion)))
        };
        if (authorizeRowSkips)
            writes.Add((selectionKey, DataCodec.Encode(source with { RowSkipConsent = analysis.RowSkipConsent })));
        foreach (var pair in writes)
        {
            try
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "INSERT INTO entry(key,payload) VALUES($key,$payload) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload";
                command.Parameters.AddWithValue("$key", pair.Item1); command.Parameters.AddWithValue("$payload", _cipher!.Encrypt(pair.Item2, pair.Item1));
                await command.ExecuteNonQueryAsync(token);
            }
            finally { CryptographicOperations.ZeroMemory(pair.Item2); }
        }
        Verify(lease); await transaction.CommitAsync(token);
        return true;
    }, token);
    public Task CollectOriginalsAsync(SchoolLease lease, CancellationToken token = default) => WithConnectionAsync(lease, async connection =>
    {
        var retained = new HashSet<string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT key,payload FROM entry WHERE key LIKE 'selection.%' OR key LIKE 'analysis.%'";
            await using var reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var key = reader.GetString(0);
                var bytes = _cipher!.Decrypt((byte[])reader.GetValue(1), key);
                try { retained.Add(key.StartsWith("selection.", StringComparison.Ordinal) ? DataCodec.Decode<SourceRecord>(bytes).Id : DataCodec.Decode<MaterialAnalysis>(bytes).OriginalId); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        var ids = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT id FROM original";
            await using var reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) ids.Add(reader.GetString(0));
        }
        if (!retained.IsSubsetOf(ids.ToHashSet())) throw new InvalidDataException("保存資料の参照を確認できません。回収を停止しました。");
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids.Where(id => !retained.Contains(id)))
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "DELETE FROM original WHERE id=$id"; command.Parameters.AddWithValue("$id", id); await command.ExecuteNonQueryAsync(token);
        }
        Verify(lease); await transaction.CommitAsync(token);
        return true;
    }, token);
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _available, false); Interlocked.Increment(ref _generation);
        await _gate.WaitAsync();
        try { _cipher?.Dispose(); _cipher = null; }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
