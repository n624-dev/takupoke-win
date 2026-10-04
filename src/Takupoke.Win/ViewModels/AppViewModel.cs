using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Notifications;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

namespace Takupoke.Win.ViewModels;

public sealed record MaterialSnapshot(SourceRecord? Source, MaterialAnalysis? Analysis, MaterialAttempt? ParseAttempt, MaterialAttempt? AcquisitionAttempt, RecoveryJob? RecoveryJob = null, RecoveryPreview? RecoveryPreview = null, RecoveryPreviewDisplay? RecoveryDisplay = null)
{
    public string AnalysisStatus(MaterialKind kind)
    {
        if (Source is null) return "未選択";
        var previous = Analysis is null ? "" : "（前回結果あり）";
        if (AcquisitionAttempt?.Failure is not null) return "取得失敗" + previous;
        if (ParseAttempt?.Failure is not null) return "解析失敗" + previous;
        return Analysis is not null && Analysis.SourceDigest == Source.Digest && Analysis.ParserVersion == MaterialCoordinator.ParserVersion(kind)
            ? "解析済み" : "未解析" + previous;
    }
}

public sealed class AppViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly SemaphoreSlim _preferenceOperations = new(1, 1);
    private CancellationTokenSource _session = new();
    private readonly HttpClient _http;
    private readonly SchoolDataStore _school;
    private readonly PreferencesStore _preferences;
    private readonly PublicEventsStore _events;
    private readonly ApiClient _api;
    private readonly SharedDataUpdater _shared;
    private readonly MaterialCoordinator _materials;
    private readonly RecoveryCoordinator _recovery;
    private readonly WindowsRecoveryModels _recoveryModels;
    public bool OcrModelReady { get; private set; }
    public bool OcrModelInstalled { get; private set; }
    public string? RecoveryModelMessage { get; private set; }
    public FoundryPinnedManifest? FoundryModel { get; private set; }
    public IReadOnlyList<FoundryPinnedManifest> FoundryCandidates => FoundryPinnedModelStore.Candidates;
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
    public AppViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        var directory = OfflineTest
            ? new DataDirectoryResult(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? throw new InvalidOperationException("CI data root is required."))
            : DataDirectory.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Root = directory.Root; RootMigrationMessage = directory.Message;
        var offline = OfflineTest ? new OfflineTestNetwork(Root) : null;
        _http = offline is not null ? new(offline) : ApiClient.CreateHttpClient();
        _school = new(Root, new WindowsDpapiProtector()); _preferences = new(Root); _events = new(Root);
        _api = new(_http); _shared = new(_api, _school); _materials = new(_school, new(new WindowsFileIdentity()));
        _recoveryModels = new(Root);
        _recovery = new(_school, _materials, new WindowsPdfRecovery(_recoveryModels).BuildAsync, _recoveryModels.ProvidersAsync);
        _authentication = new(new OidcClient(_http), offline is null ? null : offline.OpenBrowser);
        _authentication.ProgressChanged += value => OperationStatus = value;
        NavigationAnchor = Today; WeekStart = Today.DisplayWeekStart();
        _notifications = new(_school, _notificationSink);
        _notificationSink.Activated += () => _dispatcher.TryEnqueue(() => NotificationActivated?.Invoke());
        _school.RetentionChanged += ClearPrivateData;
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
            try { await _recoveryModels.CleanupBeforeProvidersAsync(token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { /* Model status is reported by ReloadAsync; school data remains available. */ }
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
    private int ParserYear => int.TryParse(Preferences.DefaultSchoolYear, out var year) && year is >= 1900 and <= 9998 ? year : Today.SchoolYear();
    public Task RefreshAsync() => RefreshAsync(automatic: false);
    public Task RefreshAutomaticallyAsync(bool force = false)
    {
        if (Locked || _automaticPaused || !force && DateTimeOffset.UtcNow - _lastAutomaticCheck < TimeSpan.FromMinutes(1)) return Task.CompletedTask;
        // Unlock/resume can arrive before a cancelled native worker has joined.
        // Preserve the fresh request until that serialized operation finishes.
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
            if ((await _materials.RefreshAsync(kind, ParserYear, token)).Error is not null) incomplete = true;
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
        var result = await _materials.SelectAsync(kind, path, ParserYear, token); await ReloadAsync(token);
        Status = result.Error ?? (result.Parsed ? "資料の解析結果を保存しました。" : "資料を保存しました。");
    });
    public Task ReparseAsync(MaterialKind kind) => RunAsync(async token =>
    {
        ResumeFileMonitoring();
        var result = await _materials.ReparseAsync(kind, ParserYear, token); await ReloadAsync(token);
        Status = result.Error ?? "最新の原本を取得して再解析しました。";
    });
    public Task PrepareRecoveryAsync(MaterialKind kind) => RunAsync(async token =>
    {
        var result = await _recovery.PrepareAsync(kind, ParserYear, token); await ReloadAsync(token, notify: false); Status = result.Message;
    }, "端末内でPDFの内容を復旧しています。");
    public Task AdoptRecoveryAsync(MaterialKind kind, RecoveryPreview preview) => RunAsync(async token =>
    {
        await _recovery.AdoptAsync(kind, preview, token); await ReloadAsync(token); Status = "確認した復旧結果を保存しました。";
    }, "確認した結果を保存しています。");
    public Task InstallOcrModelAsync() => RunAsync(async token =>
    {
        await _recoveryModels.InstallOcrAsync((done, total) => _dispatcher.TryEnqueue(() => OperationStatus = $"日本語OCRモデルを取得しています（{done / 1024 / 1024} / {total / 1024 / 1024} MB）。"), token);
        OcrModelInstalled = await _recoveryModels.OcrInstalledAsync(token) is not null;
        OcrModelReady = await _recoveryModels.OcrStateAsync(token) is not null; Status = "日本語OCRモデルを準備しました。資料の復旧を再度開始できます。";
    }, "日本語OCRモデルを取得しています。学校資料は外部へ送信されません。");
    public Task DeleteOcrModelAsync() => RunAsync(async token => { await _recoveryModels.DeleteOcrAsync(token); OcrModelReady = false; OcrModelInstalled = false; Status = "日本語OCRモデルを削除しました。"; });
    public Task InstallFoundryModelAsync(FoundryPinnedManifest manifest) => RunAsync(async token =>
    {
        await _recoveryModels.Foundry.InstallAsync(manifest, progress => _dispatcher.TryEnqueue(() => OperationStatus = $"端末内AIモデルを取得しています（{progress:0}%）。"), token);
        FoundryModel = await _recoveryModels.Foundry.InstalledAsync(token); Status = "端末内AIモデルを準備しました。PDFの復旧を再度開始できます。";
    }, "端末内AIモデルを取得しています。学校資料は外部へ送信されません。");
    public Task DeleteFoundryModelAsync() => RunAsync(async token => { await _recoveryModels.Foundry.DeleteAsync(token); FoundryModel = null; Status = "端末内AIモデルを削除しました。"; });
    public async Task<byte[]> ReadRecoveryPdfAsync(MaterialKind kind, RecoveryPreview preview)
    {
        if (Locked) throw new OperationCanceledException();
        var lease = await _school.BeginAsync(_session.Token); if (lease != preview.Lease) throw new OperationCanceledException();
        var acquisition = await _school.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, _session.Token);
        if (acquisition?.Failure is not null) throw new InvalidDataException("最新の原本を取得できません。資料の詳細から再取得してください。");
        var source = await _school.ReadAsync<SourceRecord>(lease, "selection." + kind, _session.Token);
        if (source?.Id != preview.SourceId || source.Digest != preview.Document.PdfHash) throw new OperationCanceledException("確認中のPDFが更新されました。");
        var bytes = await _school.ReadOriginalAsync(lease, preview.SourceId, _session.Token);
        if (NotificationDiff.Digest(bytes) != preview.Document.PdfHash) { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); throw new InvalidDataException("原本のハッシュを確認できません。"); }
        return bytes;
    }
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
        finally { _preferenceOperations.Release(); SnapshotChanged?.Invoke(); }
    }
    public async Task<byte[]> ReadPdfAsync(MaterialKind kind, bool accepted)
    {
        if (Locked) throw new OperationCanceledException();
        var lease = await _school.BeginAsync();
        var source = accepted ? Materials.GetValueOrDefault(kind)?.Analysis?.OriginalId : Materials.GetValueOrDefault(kind)?.Source?.Id;
        return source is not null ? await _school.ReadOriginalAsync(lease, source) : throw new InvalidDataException("保存したPDFがありません。");
    }
    public Task<ChangePreview> PreviewChangesAsync() => _materials.PreviewChangesAsync(ParserYear, _session.Token);
    public Task ReacquireAsync(MaterialKind kind) => RunAsync(async token =>
    { ResumeFileMonitoring(); var result = await _materials.RefreshAsync(kind, ParserYear, token); await ReloadAsync(token); Status = result.Error ?? "同じ原本を確認しました。"; });
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
    private async Task ReloadAsync(CancellationToken token, bool notify = true)
    {
        if (Locked) throw new OperationCanceledException();
        if (_displayPeriod is null || _displayPeriod != SchoolDataPeriod.FromInstant(DateTimeOffset.UtcNow))
            await _notifications.ClearAsync(token);
        var lease = await _school.BeginAsync(token);
        token.ThrowIfCancellationRequested();
        if (Locked) throw new OperationCanceledException();
        // BeginAsync may intentionally clear a previous retention period. Capture
        // the epoch after that transition, then reject later lock/period changes.
        var epoch = PrivateEpoch;
        var snapshots = new Dictionary<MaterialKind, MaterialSnapshot>();
        foreach (var kind in Enum.GetValues<MaterialKind>()) snapshots[kind] = new(
            await _school.ReadAsync<SourceRecord>(lease, "selection." + kind, token), await _school.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind, token),
            await _school.ReadAsync<MaterialAttempt>(lease, "attempt." + kind, token), await _school.ReadAsync<MaterialAttempt>(lease, "acquisition." + kind, token),
            await _school.ReadAsync<RecoveryJob>(lease, "recovery." + kind, token), await _school.ReadAsync<RecoveryPreview>(lease, "recovery.preview." + kind, token));
        foreach (var kind in snapshots.Keys.ToArray())
            if (snapshots[kind].Analysis is { Recovery: not null } analysis &&
                !await Task.Run(() => RecoveryAnalysisConverter.MayDisplay(analysis, token), token))
                snapshots[kind] = snapshots[kind] with { Analysis = null, ParseAttempt = new MaterialAttempt(analysis.ParsedAt,
                    "以前の復旧結果の確認内容を検証できません。資料を再解析してください。", false, analysis.SourceDigest,
                    analysis.SchoolYear, ParserVersion: analysis.ParserVersion) };
        foreach (var kind in snapshots.Keys.ToArray())
            if (snapshots[kind].RecoveryPreview is { } preview)
            {
                var display = await Task.Run(() => RecoveryPreviewDisplay.Create(preview, token), token);
                snapshots[kind] = snapshots[kind] with { RecoveryDisplay = display };
            }
        RecoveryModelMessage = null;
        try { OcrModelInstalled = await _recoveryModels.OcrInstalledAsync(token) is not null; OcrModelReady = await _recoveryModels.OcrStateAsync(token) is not null; FoundryModel = await _recoveryModels.Foundry.InstalledAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { OcrModelReady = false; OcrModelInstalled = false; FoundryModel = null; RecoveryModelMessage = "保存したAIモデルの状態を確認できません。資料の正常な解析結果は引き続き利用できます。モデル管理から準備し直してください。"; }
        var mappingRecord = await _school.ReadAsync<SavedMapping>(lease, "api.mapping", token); var mappings = mappingRecord?.Rules;
        var linksRecord = await _school.ReadAsync<SavedLinks>(lease, "api.links", token); var links = linksRecord?.Payload.Validated();
        var timesRecord = await _school.ReadAsync<SavedTimes>(lease, "api.times", token); var times = timesRecord?.Data.Validated();
        var events = new List<SchoolEvent>();
        var eventRecords = new Dictionary<int, SavedEvents>();
        var years = _events.SavedYears();
        foreach (var year in years) { var saved = await LoadEventsAsync(year, token); if (saved is not null) { eventRecords[year] = saved; events.AddRange(saved.Payload.Project()); } }
        token.ThrowIfCancellationRequested();
        if (await _school.BeginAsync(token) != lease || Locked || epoch != PrivateEpoch) throw new OperationCanceledException();
        var timetable = snapshots[MaterialKind.Timetable].Analysis?.Timetable;
        if (timetable is not null && mappings is not null) timetable = timetable with { Lessons = timetable.Lessons.Select(l => l with { Names = mappings.Apply(l.Names, l.ClassName) }).ToArray() };
        MappingRecord = mappingRecord; LinksRecord = linksRecord; TimesRecord = timesRecord;
        Materials = snapshots; Mappings = mappings; Links = links; SavedEventYears = eventRecords.Keys.Order().ToArray(); EventRecords = eventRecords;
        Data = new(timetable, snapshots[MaterialKind.Changes].Analysis?.Changes,
            new[] { snapshots[MaterialKind.Exam].Analysis?.Special, snapshots[MaterialKind.ExamReturn].Analysis?.Special }.OfType<SpecialAnalysis>().ToArray(), events, times);
        _displayPeriod = lease.Period;
        await _school.CollectOriginalsAsync(lease, token);
        if (!_automaticPaused && !_session.IsCancellationRequested) _watcher.Replace(snapshots.Values.Select(s => s.Source?.Path).OfType<string>());
        if (notify) await CheckNotificationsAsync(token);
        SnapshotChanged?.Invoke();
    }
    private Task CheckNotificationsAsync(CancellationToken token)
    {
        var eligible = Materials.Where(p => p.Value.Analysis is { } analysis && p.Value.Source?.Digest == analysis.SourceDigest
            && analysis.ParserVersion == (p.Key == MaterialKind.Changes ? Takupoke.Infrastructure.Parsing.XlsxChangeReader.Version : p.Key == MaterialKind.Timetable
                ? Takupoke.Infrastructure.Parsing.PdfScheduleParser.TimetableVersion : Takupoke.Infrastructure.Parsing.PdfScheduleParser.SpecialVersion))
            .ToDictionary(p => p.Key, p => p.Value.Analysis!.SourceDigest);
        return _notifications.CheckAsync(eligible.ContainsKey(MaterialKind.Changes) ? Data : Data with { Changes = null }, eligible, Preferences, Today, token);
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
        var completed = false;
        try { await action(_session.Token); completed = true; }
        catch (OperationCanceledException) { Status = "処理を中止しました。保存期間内の正常なデータは保持しています。"; }
        catch (ApiException error) { Status = error.Message; }
        catch (SourceException error) { Status = error.Message; }
        catch { Status = "処理を完了できませんでした。保存済みの正常なデータは保持しています。もう一度お試しください。"; }
        finally
        {
            if (!completed && !Locked && epoch == PrivateEpoch)
            {
                try { await ReloadAsync(CancellationToken.None, notify: false); }
                catch { var message = Status; ClearPrivateData(); Status = message; }
            }
            Busy = false; _operations.Release(); SnapshotChanged?.Invoke();
            if (!automatic && Status.Length > 0) _statusUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                var refreshGeneration = _operationGeneration;
                _dispatcher.TryEnqueue(() =>
                {
                    if (refreshGeneration == _operationGeneration) _ = RefreshAutomaticallyAsync(force: true);
                });
            }
        }
    }
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
        await _school.DisposeAsync(); _notificationSink.Dispose(); _http.Dispose(); _session.Dispose(); _operations.Dispose(); _preferenceOperations.Dispose();
    }
}
