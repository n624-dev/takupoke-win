using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Notifications;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

namespace Takupoke.Win.ViewModels;

public sealed record MaterialSnapshot(SourceRecord? Source, MaterialAnalysis? Analysis, MaterialAttempt? ParseAttempt, MaterialAttempt? AcquisitionAttempt)
{
    public bool NeedsAttention(MaterialKind kind, int? schoolYear = null) => AcquisitionAttempt?.Failure is not null
        || ParseAttempt?.Failure is not null
        || Source is not null && !MaterialCoordinator.AnalysisMatches(Analysis, Source, kind, schoolYear);

    public string AnalysisStatus(MaterialKind kind, int? schoolYear = null)
    {
        if (Source is null) return "未選択";
        var previous = Analysis is null ? "" : "（前回結果あり）";
        if (AcquisitionAttempt?.Failure is not null) return "取得失敗" + previous;
        if (ParseAttempt?.Failure is not null) return "解析失敗" + previous;
        return MaterialCoordinator.AnalysisMatches(Analysis, Source, kind, schoolYear)
            ? "解析済み" : "未解析" + previous;
    }
}

public sealed class AppViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly SemaphoreSlim _preferenceOperations = new(1, 1);
    private readonly SemaphoreSlim _notificationResets = new(1, 1);
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
    private readonly WindowsNotifications _notificationSink = new();
    private readonly NotificationCoordinator _notifications;
    private readonly DispatcherQueueTimer _timer;
    private SchoolDataPeriod? _displayPeriod;
    private string _status = "";
    private string _operationStatus = "読み込み中です。";
    private DateTimeOffset _statusUntil;
    private DateTimeOffset _lastAutomaticCheck;
    private bool _busy;
    private bool _pendingRefresh;
    private long _notificationReset;
    private long _clearedNotificationReset;
    public bool RetentionRecoveryNeeded { get; private set; }
    private bool _notificationsInitialized;
    private bool _protocolRegistered;
    private bool _automaticPaused;
    public bool AutomaticRefreshPaused => _automaticPaused;
    public bool Locked { get; private set; }
    public long PrivateEpoch { get; private set; }
    public string Status { get => _status; private set { _statusUntil = DateTimeOffset.UtcNow.AddSeconds(5); SetProperty(ref _status, value); } }
    public string OperationStatus { get => _operationStatus; private set => SetProperty(ref _operationStatus, value); }
    public void DismissStatus() => Status = "";
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public bool OfflineTest { get; } = Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1";
    public string Root { get; }
    public string? RootMigrationMessage { get; }
    public string? PlatformMessage { get; private set; }
    public UserPreferences Preferences { get; private set; } = new();
    public bool PreferencesReady { get; private set; }
    public ScheduleData Data { get; private set; } = new();
    public MappingRules? Mappings { get; private set; }
    public LinksPayload? Links { get; private set; }
    public IReadOnlyDictionary<MaterialKind, MaterialSnapshot> Materials { get; private set; } = new Dictionary<MaterialKind, MaterialSnapshot>();
    private RevisionCheckResult _revisionChecks = RevisionCheckResult.Empty;
    public IReadOnlyDictionary<DataSet, RevisionResult> Revisions => _revisionChecks.Revisions;
    public IReadOnlyDictionary<DataSet, ApiFailure> RevisionFailures => _revisionChecks.Failures;
    public IReadOnlyList<int> SavedEventYears { get; private set; } = [];
    public IReadOnlyDictionary<int, SavedEvents> EventRecords { get; private set; } = new Dictionary<int, SavedEvents>();
    public bool HasCurrentAnalysis(MaterialKind kind) => Materials.TryGetValue(kind, out var item)
        && MaterialCoordinator.AnalysisMatches(item.Analysis, item.Source, kind,
            ChangeNormalizer.EffectiveSchoolYear(Preferences.DefaultSchoolYear, Today));
    public DateOnly Today => SchoolDate.InJapan(DateTimeOffset.UtcNow);
    public DateOnly WeekStart { get; set; }
    public SchedulePresentation Presentation => new(Data, Mappings);
    public TimetableEngine Engine => Presentation.Engine(Preferences);
    public TimetableEngine HomeEngine => Presentation.Engine(Preferences, home: true);
    public DateOnly NavigationAnchor { get; private set; }
    public void OpenTodayWeek() { NavigationAnchor = Today; WeekStart = Today.Monday(); }
    public event Action? SnapshotChanged;
    public event Action? ClockChanged;
    public event Action? PrivateDataCleared;
    public event Action? NotificationActivated;
    public string NotificationStatus => _notificationSink.Status;
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public AppViewModel(DispatcherQueue dispatcher) : this(dispatcher, new WindowsDpapiProtector()) { }
    internal AppViewModel(DispatcherQueue dispatcher, IKeyProtector protector, IFileIdentityProvider? identity = null)
    {
        _dispatcher = dispatcher;
        var directory = OfflineTest
            ? new DataDirectoryResult(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? throw new InvalidOperationException("CI data root is required."))
            : DataDirectory.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Root = directory.Root; RootMigrationMessage = directory.Message;
        var offline = OfflineTest ? new OfflineTestNetwork(Root) : null;
        _http = offline is not null ? new(offline) : ApiClient.CreateHttpClient();
        _school = new(Root, protector); _preferences = new(Root); _events = new(Root);
        _api = new(_http); _shared = new(_api, _school); _materials = new(_school, new(identity ?? new WindowsFileIdentity()));
        _authentication = new(new OidcClient(_http), offline is null ? null : offline.OpenBrowser);
        _authentication.ProgressChanged += value => OperationStatus = value;
        NavigationAnchor = Today; WeekStart = Today.DisplayWeekStart();
        _notifications = new(_school, _notificationSink);
        _notificationSink.Activated += () => _dispatcher.TryEnqueue(() => NotificationActivated?.Invoke());
        _school.RetentionChanged += () => { Interlocked.Increment(ref _notificationReset); ClearPrivateData(); };
        _school.RetentionUnavailable += canRecover => { RetentionRecoveryNeeded = canRecover; ClearPrivateData(); };
        _watcher.Changed += () =>
        {
            var generation = System.Threading.Volatile.Read(ref _operationGeneration);
            _dispatcher.TryEnqueue(() => { if (!_automaticPaused && generation == _operationGeneration) { if (Busy) _pendingRefresh = true; else _ = RefreshAutomaticallyAsync(force: true); } });
        };
        _timer = dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromSeconds(1); _timer.Tick += TimerTick;
        Program.ProtocolCallback = uri => _dispatcher.TryEnqueue(() => _authentication.HandleCallback(uri)); Program.DrainCallbacks();
    }
    private DateTimeOffset _nextCheck = DateTimeOffset.UtcNow.AddMinutes(15);
    private DateOnly? _lastDay;
    private DateTimeOffset _lastClock;
    private long _operationGeneration;
    private bool _checkedEventSource;
    public string? EventSourceMessage { get; private set; }
    public string? EventsUpdateMessage { get; private set; }
    private void TimerTick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTimeOffset.UtcNow;
        if (!Busy && Status.Length > 0 && now >= _statusUntil) DismissStatus();
        if (_displayPeriod is { } displayed && displayed != SchoolDataPeriod.FromInstant(now))
        { _session.Cancel(); _authentication.Cancel(); ClearPrivateData(); _ = RefreshAsync(); return; }
        if (!_automaticPaused && Preferences.KeepInTray && now >= _nextCheck) { _nextCheck = now.AddMinutes(15); _ = RefreshAutomaticallyAsync(force: true); }
        if (_lastDay != Today || now - _lastClock >= TimeSpan.FromSeconds(15)) { _lastDay = Today; _lastClock = now; ClockChanged?.Invoke(); }
    }
    private void ClearPrivateData()
    {
        if (_displayPeriod is not null) Cancel();
        PrivateEpoch++;
        Data = Data with { Timetable = null, Changes = null, Specials = null, Times = null };
        LinksRecord = null; MappingRecord = null; TimesRecord = null; Links = null; Mappings = null; Materials = new Dictionary<MaterialKind, MaterialSnapshot>(); _revisionChecks = RevisionCheckResult.Empty;
        SharedUpdateResults = []; SharedUpdateMessage = null; DismissStatus();
        _displayPeriod = null; _watcher.Replace([]);
        void Notify() { PrivateDataCleared?.Invoke(); SnapshotChanged?.Invoke(); }
        if (_dispatcher.HasThreadAccess) Notify(); else _dispatcher.TryEnqueue(Notify);
    }
    public async Task InitializeAsync()
    {
        await RunAsync(async token =>
        {
            Preferences = await _preferences.LoadAsync(token); PreferencesReady = true;
            InitializePlatform();
            await ReloadAsync(token);
        });
        _timer.Start();
        if (!OfflineTest) await RefreshAutomaticallyAsync(force: true);
    }
    private void InitializePlatform()
    {
        if (OfflineTest) return;
        if (!_protocolRegistered)
        {
            try { BrowserAuthenticator.RegisterProtocol(); _protocolRegistered = true; PlatformMessage = null; }
            catch { PlatformMessage = "ブラウザから認証結果を受け取る設定を登録できませんでした。アプリを起動し直してください。保存済みの資料は利用できます。"; }
        }
        if (!_notificationsInitialized) { _notificationSink.Initialize(); _notificationsInitialized = true; }
    }
    private int ParserYear => ChangeNormalizer.EffectiveSchoolYear(Preferences.DefaultSchoolYear, Today)
        ?? throw new ChangeParseException(ChangeErrorCode.DefaultYear);
    private int ParserYearFor(MaterialKind kind) => kind == MaterialKind.Changes ? ParserYear : Today.SchoolYear();
    public Task RefreshAsync() => RefreshAsync(automatic: false);
    public Task RefreshAutomaticallyAsync(bool force = false)
    {
        if (Locked || _automaticPaused || !force && DateTimeOffset.UtcNow - _lastAutomaticCheck < TimeSpan.FromMinutes(1)) return Task.CompletedTask;
        if (Busy) { _pendingRefresh = true; return Task.CompletedTask; }
        _lastAutomaticCheck = DateTimeOffset.UtcNow;
        return RefreshAsync(automatic: true);
    }
    public Task ResumeAutomaticRefreshAsync(bool force = false, bool refresh = true)
    {
        ResumeFileMonitoring();
        SnapshotChanged?.Invoke();
        return refresh ? RefreshAutomaticallyAsync(force) : Task.CompletedTask;
    }
    private void ResumeFileMonitoring()
    {
        _automaticPaused = false;
        if (!Locked) _watcher.Replace(Materials.Values.Select(s => s.Source?.Path).OfType<string>());
    }
    private Task RefreshAsync(bool automatic)
    {
        if (Locked) return Task.CompletedTask;
        if (!automatic) _automaticPaused = false;
        if (Busy) { _pendingRefresh = true; return Task.CompletedTask; }
        return RunAsync(async token =>
    {
        if (!PreferencesReady) { Preferences = await _preferences.LoadAsync(token); PreferencesReady = true; }
        InitializePlatform();
        // Checking the lease first invalidates expired data before reading originals or contacting the API.
        await _school.BeginAsync(token);
        var incomplete = false;
        foreach (var kind in Enum.GetValues<MaterialKind>())
        {
            if (kind == MaterialKind.Changes && ChangeNormalizer.EffectiveSchoolYear(Preferences.DefaultSchoolYear, Today) is null) { incomplete = true; continue; }
            if ((await _materials.RefreshAsync(kind, ParserYearFor(kind), token)).Error is not null) incomplete = true;
        }
        if (!OfflineTest)
        {
            ApplyRevisionCheck(await _shared.CheckDetailedAsync(token));
            incomplete |= RevisionFailures.Count > 0;
            var failedYears = new List<int>();
            foreach (var year in _events.SavedYears())
                try { await _events.SaveAsync(await _api.DownloadEventsAsync(year, await LoadEventsAsync(year, token), token), token); }
                catch (ApiException) { failedYears.Add(year); }
            EventsUpdateMessage = failedYears.Count == 0 ? null : string.Join("、", failedYears) + "年度の学校行事を更新確認できませんでした。保存済みの結果を表示しています。";
            incomplete |= failedYears.Count > 0;
            if (!_checkedEventSource)
            {
                _checkedEventSource = true;
                var sourceState = await new EventSourceChecker(_http).CheckAsync(await LoadEventsAsync(EventSourceChecker.SourceSchoolYear, token), token);
                EventSourceMessage = EventSourceChecker.Message(sourceState);
                incomplete |= sourceState == EventSourceState.Unavailable;
            }
        }
        await ReloadAsync(token);
        if (!automatic) Status = incomplete
            ? "一部の資料や更新情報を確認できませんでした。各項目の状態を確認してください。保存済みの正常なデータは保持しています。"
            : "資料と更新情報を確認しました。";
        }, "資料と更新情報を確認しています。", automatic);
    }
    public Task SelectAsync(MaterialKind kind, string path) => RunAsync(async token =>
    {
        ResumeFileMonitoring();
        var result = await _materials.SelectAsync(kind, path, ParserYearFor(kind), token); await ReloadAsync(token);
        Status = result.Error ?? (result.Parsed ? "資料の解析結果を保存しました。" : "資料を保存しました。");
    });
    public Task ReparseAsync(MaterialKind kind) => RunAsync(async token =>
    {
        ResumeFileMonitoring();
        var result = await _materials.ReparseAsync(kind, ParserYearFor(kind), token); await ReloadAsync(token);
        Status = result.Error ?? "保存した資料を再解析しました。";
    });
    public Task UpdateSharedAsync() => RunAsync(async token =>
    {
        SharedUpdateResults = []; SharedUpdateMessage = null;
        InitializePlatform();
        if (!OfflineTest && !_protocolRegistered)
        { SharedUpdateMessage = PlatformMessage; Status = PlatformMessage!; return; }
        IReadOnlyList<UpdateResult> results;
        try { results = await _shared.UpdateAsync(_authentication.AuthenticateAsync, token); }
        catch (ApiException error) { SharedUpdateMessage = error.Message; throw; }
        catch (OperationCanceledException) { SharedUpdateMessage = "取得を中止しました。"; throw; }
        finally { if (!Locked) await ReloadAsync(CancellationToken.None); }
        SharedUpdateResults = results;
        OperationStatus = "取得結果を確認しています。";
        ApplyRevisionCheck(await _shared.CheckDetailedAsync(token), clearPreviousUpdateFailures: false);
        Status = string.Join(" / ", results.Select(r => DataSetLabel(r.Kind) + "：" + (r.Updated ? "更新しました" : r.Failure is { } failure ? new ApiException(failure).Message : "保持しています")));
    }, "更新情報を確認しています。");
    public IReadOnlyList<UpdateResult> SharedUpdateResults { get; private set; } = [];
    public string? SharedUpdateMessage { get; private set; }
    private void ApplyRevisionCheck(RevisionCheckResult check, bool clearPreviousUpdateFailures = true)
    {
        _revisionChecks = _revisionChecks.Merge(check);
        if (!clearPreviousUpdateFailures) return;
        SharedUpdateResults = SharedUpdateResults.Where(result => result.Failure is null || !check.Revisions.ContainsKey(result.Kind)).ToArray();
        if (check.Failures.Count == 0 && check.Revisions.Count == Enum.GetValues<DataSet>().Length) SharedUpdateMessage = null;
    }
    public Task CheckLinkRevisionAsync() => RunAsync(async token =>
    {
        // Public revision checks do not read selected files or authenticate.
        // They remain available while automatic selected-file checks are paused.
        ApplyRevisionCheck(await _shared.CheckDetailedAsync(DataSet.Links, token));
    }, "一覧の更新情報を確認しています。", automatic: true);
    public Task FetchEventsAsync(int year) => RunAsync(async token =>
    { await _events.SaveAsync(await _api.DownloadEventsAsync(year, await LoadEventsAsync(year, token), token), token); EventSourceMessage = null; EventsUpdateMessage = null; await ReloadAsync(token); Status = "学校行事を保存しました。元PDFの更新確認は次回起動時に行います。"; });
    public async Task SavePreferencesAsync(Func<UserPreferences, UserPreferences> update)
    {
        await _preferenceOperations.WaitAsync();
        try
        {
            if (!PreferencesReady) { Status = "個人設定を読み込めませんでした。再読み込みしてください。"; return; }
            // Apply just this edit to the latest preferences. Authentication and
            // file-provider waits never block local settings or lose another edit.
            var next = update(Preferences).Validated();
            await _preferences.SaveAsync(next); Preferences = next;
            if (!Locked && _displayPeriod is not null)
                try { await CheckNotificationsAsync(_session.Token); }
                catch (OperationCanceledException) { }
                catch { Status = "設定は保存しましたが、通知の確認を完了できませんでした。"; }
        }
        catch { Status = "個人設定を保存できませんでした。もう一度お試しください。"; }
        finally
        {
            try { await ClearExpiredNotificationsAsync(); }
            catch { Status = "保存期間が切り替わりましたが、以前の通知履歴を削除できませんでした。"; }
            _preferenceOperations.Release(); SnapshotChanged?.Invoke();
        }
    }
    public async Task<byte[]> ReadPdfAsync(MaterialKind kind, bool accepted)
    {
        if (Locked) throw new OperationCanceledException();
        var lease = await _school.BeginAsync();
        var source = accepted ? Materials.GetValueOrDefault(kind)?.Analysis?.OriginalId : Materials.GetValueOrDefault(kind)?.Source?.Id;
        return source is not null ? await _school.ReadOriginalAsync(lease, source) : throw new InvalidDataException("保存したPDFがありません。");
    }
    public Task<ChangePreview> PreviewChangesAsync() => _materials.PreviewChangesAsync(ParserYear, _session.Token);
    public Task ApplyChangeRowSkipsAsync(ChangePreview preview, IReadOnlyList<int> rows) => RunAsync(async token =>
    {
        await _materials.ApplyRowSkipsAsync(preview, rows, ParserYear, token);
        await ReloadAsync(token);
        Status = "選んだ行を除外して読み込みました。";
    });
    public Task ReacquireAsync(MaterialKind kind) => RunAsync(async token =>
    { ResumeFileMonitoring(); var result = await _materials.RefreshAsync(kind, ParserYearFor(kind), token); await ReloadAsync(token); Status = result.Error ?? "同じ原本を確認しました。"; });
    public SavedLinks? LinksRecord { get; private set; }
    public SavedMapping? MappingRecord { get; private set; }
    public SavedTimes? TimesRecord { get; private set; }
    private readonly SortedSet<int> _eventReadFailures = [];
    public string? EventStorageMessage => _eventReadFailures.Count == 0 ? null
        : string.Join("、", _eventReadFailures) + "年度の保存した学校行事を読み込めませんでした。この年度の行事は時間割に反映していません。設定の「学校行事」で取得し直してください。他の資料は引き続き表示します。";
    private async Task<SavedEvents?> LoadEventsAsync(int year, CancellationToken token)
    {
        var read = await _events.LoadAvailableAsync(year, token);
        if (read.Failed) _eventReadFailures.Add(year); else _eventReadFailures.Remove(year);
        return read.Events;
    }
    private async Task ReloadAsync(CancellationToken token)
    {
        var lease = await _school.BeginAsync(token);
        // An empty in-memory snapshot also occurs on same-period startup and
        // unlock. Only the store's actual retention reset invalidates OS tags.
        await ClearExpiredNotificationsAsync();
        RetentionRecoveryNeeded = false;
        var snapshots = new Dictionary<MaterialKind, MaterialSnapshot>();
        foreach (var kind in Enum.GetValues<MaterialKind>()) snapshots[kind] = new(
            await _school.ReadAsync<SourceRecord>(lease, "selection." + kind, token), await _school.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind, token),
            await _school.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token), await _school.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, token));
        var mappingRecord = await _shared.LoadMappingAsync(lease, token); var mappings = mappingRecord?.Rules;
        var linksRecord = await _school.ReadAsync<SavedLinks>(lease, "api.links", token); var links = linksRecord?.Payload.Validated();
        var timesRecord = await _shared.LoadTimesAsync(lease, token); var times = timesRecord?.Data;
        var events = new List<SchoolEvent>();
        var eventRecords = new Dictionary<int, SavedEvents>();
        var years = _events.SavedYears();
        foreach (var year in years) { var saved = await LoadEventsAsync(year, token); if (saved is not null) { eventRecords[year] = saved; events.AddRange(saved.Payload.Project()); } }
        token.ThrowIfCancellationRequested();
        if (await _school.BeginAsync(token) != lease) throw new OperationCanceledException();
        var timetable = snapshots[MaterialKind.Timetable].Analysis?.Timetable;
        if (timetable is not null && mappings is not null) timetable = timetable with { Lessons = timetable.Lessons.Select(l => l with { Names = mappings.Apply(l.Names, l.ClassName) }).ToArray() };
        MappingRecord = mappingRecord; LinksRecord = linksRecord; TimesRecord = timesRecord;
        Materials = snapshots; Mappings = mappings; Links = links; SavedEventYears = eventRecords.Keys.Order().ToArray(); EventRecords = eventRecords;
        Data = new(timetable, snapshots[MaterialKind.Changes].Analysis?.Changes,
            new[] { snapshots[MaterialKind.Exam].Analysis?.Special, snapshots[MaterialKind.ExamReturn].Analysis?.Special }.OfType<SpecialAnalysis>().ToArray(), events, times);
        _displayPeriod = lease.Period;
        await _school.CollectOriginalsAsync(lease, token);
        if (!_automaticPaused && !_session.IsCancellationRequested) _watcher.Replace(snapshots.Values.Select(s => s.Source?.Path).OfType<string>());
        await CheckNotificationsAsync(token);
        SnapshotChanged?.Invoke();
    }
    private Task CheckNotificationsAsync(CancellationToken token)
    {
        var eligible = Materials.Where(p => HasCurrentAnalysis(p.Key)).ToDictionary(p => p.Key, p => p.Value.Analysis!.SourceDigest);
        var availability = new[] { MaterialKind.Changes, MaterialKind.Exam, MaterialKind.ExamReturn }.ToDictionary(
            kind => kind == MaterialKind.Changes ? "changes" : kind == MaterialKind.Exam ? "exam" : "examReturn",
            kind => Materials[kind].Source is null ? NotificationSourceState.Absent
                : eligible.ContainsKey(kind) ? NotificationSourceState.Available : NotificationSourceState.Unavailable);
        return _notifications.CheckAsync(eligible.ContainsKey(MaterialKind.Changes) ? Data : Data with { Changes = null }, eligible, Preferences, Today, token, availability);
    }
    private async Task ClearExpiredNotificationsAsync()
    {
        await _notificationResets.WaitAsync();
        try
        {
            var reset = Volatile.Read(ref _notificationReset);
            if (reset == _clearedNotificationReset) return;
            await _notifications.ClearAsync(CancellationToken.None);
            _clearedNotificationReset = reset;
        }
        finally { _notificationResets.Release(); }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action, string progress = "処理しています。", bool automatic = false)
    {
        if (Locked) { Status = "Windowsのロック中は学校データを利用できません。"; return; }
        // Picker completion can race a refresh triggered by window activation.
        // Explicit operations must wait for the current operation, never disappear.
        var epoch = PrivateEpoch; var generation = _operationGeneration;
        await _operations.WaitAsync();
        if (Locked || epoch != PrivateEpoch || generation != _operationGeneration)
        {
            _operations.Release();
            Status = "学校データの利用状態が変わったため処理を中止しました。もう一度お試しください。";
            return;
        }
        OperationStatus = progress; Busy = true;
        if (_session.IsCancellationRequested) { _session.Dispose(); _session = new(); }
        try { await action(_session.Token); }
        catch (OperationCanceledException) { Status = "処理を中止しました。保存期間内の正常なデータは保持しています。"; }
        catch (SchoolDataStore.RetentionStateException error)
        {
            RetentionRecoveryNeeded = error.CanRecover; Status = error.Message;
        }
        catch (ApiException error) { Status = error.Message; }
        catch (SourceException error) { Status = error.Message; }
        catch { Status = "処理を完了できませんでした。保存済みの正常なデータは保持しています。もう一度お試しください。"; }
        finally
        {
            // Retention can cancel the operation before ReloadAsync is reached.
            // Its obsolete OS history must still be removed after cancellation.
            try { await ClearExpiredNotificationsAsync(); }
            catch { Status = "保存期間が切り替わりましたが、以前の通知履歴を削除できませんでした。"; }
            Busy = false; _operations.Release(); SnapshotChanged?.Invoke();
            if (!automatic && Status.Length > 0) _statusUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            if (_pendingRefresh) { _pendingRefresh = false; _dispatcher.TryEnqueue(() => _ = RefreshAutomaticallyAsync(force: true)); }
        }
    }
    public Task RecoverRetentionAsync() => RunAsync(async token =>
    {
        if (!RetentionRecoveryNeeded) return;
        await _school.RecoverRetentionAsync(token);
        RetentionRecoveryNeeded = false;
        // Retention invalidates the old session while resetting the store.
        // Load through a new operation rather than applying its cancelled lease.
        _automaticPaused = false; _pendingRefresh = true;
        Status = "学校データを初期化しました。資料を選び直し、学校アカウントのデータを再取得してください。";
    });

    public void Cancel() { _automaticPaused = true; _operationGeneration++; _pendingRefresh = false; _watcher.Replace([]); _session.Cancel(); _authentication.Cancel(); }
    public void SuspendAutomaticRefresh()
    {
        Cancel();
        Status = Busy ? "中止を要求しました。処理の終了を待っています。" : "自動確認を中止しました。";
        SnapshotChanged?.Invoke();
    }
    public async Task SetLockedAsync(bool locked)
    {
        Locked = locked; Cancel(); ClearPrivateData();
        await _school.SetProtectedDataAvailableAsync(!locked);
        if (!locked) await ResumeAutomaticRefreshAsync(force: true);
    }
    public static string MaterialLabel(MaterialKind kind) => kind switch { MaterialKind.Timetable => "通常時間割PDF", MaterialKind.Changes => "時間割変更XLSX", MaterialKind.Exam => "試験時間割PDF", _ => "試験返却時間割PDF" };
    public static string DataSetLabel(DataSet kind) => kind switch { DataSet.Links => "リンク一覧", DataSet.Mapping => "名称対応表", _ => "授業時刻" };
    public async ValueTask DisposeAsync()
    {
        Cancel(); _timer.Stop(); _watcher.Dispose(); _authentication.Dispose();
        await _operations.WaitAsync(); await _preferenceOperations.WaitAsync();
        await _school.DisposeAsync(); _notificationSink.Dispose(); _http.Dispose(); _session.Dispose(); _operations.Dispose(); _preferenceOperations.Dispose(); _notificationResets.Dispose();
    }
}
