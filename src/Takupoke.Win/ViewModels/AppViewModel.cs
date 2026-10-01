using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

namespace Takupoke.Win.ViewModels;

public sealed record MaterialSnapshot(SourceRecord? Source, MaterialAnalysis? Analysis, MaterialAttempt? ParseAttempt, MaterialAttempt? AcquisitionAttempt);

public sealed class AppViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CancellationTokenSource _session = new();
    private readonly HttpClient _http;
    private readonly SchoolDataStore _school;
    private readonly PreferencesStore _preferences;
    private readonly PublicEventsStore _events;
    private readonly ApiClient _api;
    private readonly SharedDataUpdater _shared;
    private readonly MaterialCoordinator _materials;
    private readonly SourceWatcher _watcher = new();
    private readonly BrowserAuthenticator _authentication;
    private readonly DispatcherQueueTimer _timer;
    private SchoolDataPeriod? _displayPeriod;
    private string _status = "読み込み中です。";
    private bool _busy;
    private bool _pendingRefresh;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public bool OfflineTest { get; } = Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1";
    public string Root { get; }
    public UserPreferences Preferences { get; private set; } = new();
    public ScheduleData Data { get; private set; } = new();
    public MappingRules? Mappings { get; private set; }
    public LinksPayload? Links { get; private set; }
    public IReadOnlyDictionary<MaterialKind, MaterialSnapshot> Materials { get; private set; } = new Dictionary<MaterialKind, MaterialSnapshot>();
    public IReadOnlyDictionary<DataSet, RevisionResult> Revisions { get; private set; } = new Dictionary<DataSet, RevisionResult>();
    public IReadOnlyList<int> SavedEventYears { get; private set; } = [];
    public DateOnly Today => SchoolDate.InJapan(DateTimeOffset.UtcNow);
    public DateOnly WeekStart { get; set; }
    public TimetableEngine Engine => new(Data, Preferences.IncludesChanges, Preferences.International, Mappings is null ? null : Mappings.IsInternational);
    public event Action? SnapshotChanged;
    public event Action? PrivateDataCleared;
    private sealed class OfflineHandler : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("CI does not perform network requests."); }
    public AppViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        Root = OfflineTest ? Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? throw new InvalidOperationException("CI data root is required.")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TakupokeWin");
        _http = OfflineTest ? new(new OfflineHandler()) : ApiClient.CreateHttpClient();
        _school = new(Root, new WindowsDpapiProtector()); _preferences = new(Root); _events = new(Root);
        _api = new(_http); _shared = new(_api, _school); _materials = new(_school, new(new WindowsFileIdentity()));
        _authentication = new(new OidcClient(_http)); WeekStart = Today.DisplayWeekStart();
        _school.RetentionChanged += ClearPrivateData;
        _watcher.Changed += () => _dispatcher.TryEnqueue(() => _ = RefreshAsync());
        _timer = dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromSeconds(1); _timer.Tick += TimerTick;
        Program.ProtocolCallback = uri => _dispatcher.TryEnqueue(() => _authentication.HandleCallback(uri)); Program.DrainCallbacks();
    }
    private DateTimeOffset _nextCheck = DateTimeOffset.UtcNow.AddMinutes(15);
    private DateOnly? _lastDay;
    private int _lastMinute = -1;
    private void TimerTick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTimeOffset.UtcNow;
        if (_displayPeriod is { } displayed && displayed != SchoolDataPeriod.FromInstant(now))
        { _session.Cancel(); _authentication.Cancel(); ClearPrivateData(); _ = RefreshAsync(); return; }
        if (Preferences.KeepInTray && now >= _nextCheck) { _nextCheck = now.AddMinutes(15); _ = RefreshAsync(); }
        if (_lastDay != Today || _lastMinute != now.Minute) { _lastDay = Today; _lastMinute = now.Minute; SnapshotChanged?.Invoke(); }
    }
    private void ClearPrivateData()
    {
        Data = Data with { Timetable = null, Changes = null, Specials = null, Times = null };
        Links = null; Mappings = null; Materials = new Dictionary<MaterialKind, MaterialSnapshot>(); Revisions = new Dictionary<DataSet, RevisionResult>();
        _displayPeriod = null; _watcher.Replace([]);
        void Notify() { PrivateDataCleared?.Invoke(); SnapshotChanged?.Invoke(); }
        if (_dispatcher.HasThreadAccess) Notify(); else _dispatcher.TryEnqueue(Notify);
    }
    public async Task InitializeAsync()
    {
        await RunAsync(async token =>
        {
            Preferences = await _preferences.LoadAsync(token);
            if (!OfflineTest) BrowserAuthenticator.RegisterProtocol();
            await ReloadAsync(token); Status = "学校資料を選択すると、端末内で解析します。";
        });
        _timer.Start();
        if (!OfflineTest) await RefreshAsync();
    }
    private int ParserYear => int.TryParse(Preferences.DefaultSchoolYear, out var year) && year is >= 1900 and <= 9998 ? year : Today.SchoolYear();
    public Task RefreshAsync()
    {
        if (Busy) { _pendingRefresh = true; return Task.CompletedTask; }
        return RunAsync(async token =>
    {
        // Checking the lease first invalidates expired data before reading originals or contacting the API.
        await _school.BeginAsync(token);
        foreach (var kind in Enum.GetValues<MaterialKind>()) await _materials.RefreshAsync(kind, ParserYear, token);
        if (!OfflineTest)
        {
            Revisions = await _shared.CheckAsync(token);
            foreach (var year in _events.SavedYears())
                try { await _events.SaveAsync(await _api.DownloadEventsAsync(year, await _events.LoadAsync(year, token), token), token); }
                catch (ApiException) { /* Keep each previously validated public year independently. */ }
        }
        await ReloadAsync(token); Status = "登録した原本と公開更新情報を確認しました。OneDriveのクラウド同期完了を示すものではありません。";
        });
    }
    public Task SelectAsync(MaterialKind kind, string path) => RunAsync(async token =>
    {
        var result = await _materials.SelectAsync(kind, path, ParserYear, token); await ReloadAsync(token);
        Status = result.Error ?? (result.Parsed ? "資料の解析結果を保存しました。" : "資料を保存しました。");
    });
    public Task ReparseAsync(MaterialKind kind) => RunAsync(async token =>
    {
        var result = await _materials.ReparseAsync(kind, ParserYear, token); await ReloadAsync(token);
        Status = result.Error ?? "保存した資料を再解析しました。";
    });
    public Task UpdateSharedAsync() => RunAsync(async token =>
    {
        var results = await _shared.UpdateAsync(_authentication.AuthenticateAsync, token);
        await ReloadAsync(token);
        Revisions = await _shared.CheckAsync(token);
        Status = string.Join(" / ", results.Select(r => DataSetLabel(r.Kind) + "：" + (r.Updated ? "更新しました" : r.Failure is { } failure ? new ApiException(failure).Message : "保持しています")));
    });
    public Task FetchEventsAsync(int year) => RunAsync(async token =>
    { await _events.SaveAsync(await _api.DownloadEventsAsync(year, await _events.LoadAsync(year, token), token), token); await ReloadAsync(token); Status = "学校行事を保存しました。"; });
    public Task SavePreferencesAsync(UserPreferences next) => RunAsync(async token =>
    {
        await _preferences.SaveAsync(next.Validated(), token); Preferences = next; SnapshotChanged?.Invoke();
    });
    public async Task<byte[]> ReadPdfAsync(MaterialKind kind, bool accepted)
    {
        var lease = await _school.BeginAsync();
        var source = accepted ? Materials.GetValueOrDefault(kind)?.Analysis?.OriginalId : Materials.GetValueOrDefault(kind)?.Source?.Id;
        return source is not null ? await _school.ReadOriginalAsync(lease, source) : throw new InvalidDataException("保存したPDFがありません。");
    }
    private async Task ReloadAsync(CancellationToken token)
    {
        var lease = await _school.BeginAsync(token);
        var snapshots = new Dictionary<MaterialKind, MaterialSnapshot>();
        foreach (var kind in Enum.GetValues<MaterialKind>()) snapshots[kind] = new(
            await _school.ReadAsync<SourceRecord>(lease, "selection." + kind, token), await _school.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind, token),
            await _school.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token), await _school.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, token));
        var mappings = (await _school.ReadAsync<SavedMapping>(lease, "api.mapping", token))?.Rules;
        var links = (await _school.ReadAsync<SavedLinks>(lease, "api.links", token))?.Payload.Validated();
        var times = (await _school.ReadAsync<SavedTimes>(lease, "api.times", token))?.Data.Validated();
        var events = new List<SchoolEvent>();
        var years = _events.SavedYears();
        foreach (var year in years) { var saved = await _events.LoadAsync(year, token); if (saved is not null) events.AddRange(saved.Payload.Project()); }
        token.ThrowIfCancellationRequested();
        if (await _school.BeginAsync(token) != lease) throw new OperationCanceledException();
        var timetable = snapshots[MaterialKind.Timetable].Analysis?.Timetable;
        if (timetable is not null && mappings is not null) timetable = timetable with { Lessons = timetable.Lessons.Select(l => l with { Names = mappings.Apply(l.Names, l.ClassName) }).ToArray() };
        Materials = snapshots; Mappings = mappings; Links = links; SavedEventYears = years;
        Data = new(timetable, snapshots[MaterialKind.Changes].Analysis?.Changes,
            new[] { snapshots[MaterialKind.Exam].Analysis?.Special, snapshots[MaterialKind.ExamReturn].Analysis?.Special }.OfType<SpecialAnalysis>().ToArray(), events, times);
        _displayPeriod = lease.Period;
        _watcher.Replace(snapshots.Values.Select(s => s.Source?.Path).OfType<string>());
        SnapshotChanged?.Invoke();
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Busy) return;
        await _operations.WaitAsync(); Busy = true;
        if (_session.IsCancellationRequested) { _session.Dispose(); _session = new(); }
        try { await action(_session.Token); }
        catch (OperationCanceledException) { Status = "処理を中止しました。保存期間内の正常なデータは保持しています。"; }
        catch (ApiException error) { Status = error.Message; }
        catch (SourceException error) { Status = error.Message; }
        catch { Status = "処理を完了できませんでした。保存済みの正常なデータは保持しています。もう一度お試しください。"; }
        finally
        {
            Busy = false; _operations.Release(); SnapshotChanged?.Invoke();
            if (_pendingRefresh) { _pendingRefresh = false; _dispatcher.TryEnqueue(() => _ = RefreshAsync()); }
        }
    }
    public void Cancel() { _session.Cancel(); _authentication.Cancel(); }
    public async Task SetLockedAsync(bool locked)
    {
        Cancel(); ClearPrivateData();
        await _school.SetProtectedDataAvailableAsync(!locked);
        if (!locked) await RefreshAsync();
    }
    public static string MaterialLabel(MaterialKind kind) => kind switch { MaterialKind.Timetable => "通常時間割PDF", MaterialKind.Changes => "時間割変更XLSX", MaterialKind.Exam => "試験時間割PDF", _ => "試験返却時間割PDF" };
    public static string DataSetLabel(DataSet kind) => kind switch { DataSet.Links => "リンク一覧", DataSet.Mapping => "名称対応表", _ => "授業時刻" };
    public async ValueTask DisposeAsync()
    {
        Cancel(); _timer.Stop(); _watcher.Dispose(); _authentication.Dispose();
        await _operations.WaitAsync(); await _school.DisposeAsync(); _http.Dispose(); _session.Dispose(); _operations.Dispose();
    }
}
