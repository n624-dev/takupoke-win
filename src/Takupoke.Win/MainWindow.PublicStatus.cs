using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private static readonly (PublicService Service, string Title, (string Id, string Title)[] Features)[] StatusServices =
    [
        (PublicService.Api, "たくポケAPI", [("events", "行事配信"), ("links", "リンク集"),
            ("mappings", "名称データ"), ("timetable-times", "授業時刻")]),
        (PublicService.Authentication, "たくにん", [("database", "認証データベース"), ("oidc", "認証設定と署名")])
    ];
    private HttpClient? _statusHttp;
    private CancellationTokenSource? _statusLifetime;
    private readonly Dictionary<PublicService, FunctionalStatus> _functionalStatus = [];
    private readonly HashSet<PublicService> _failedStatus = [];
    private readonly Dictionary<int, (DateTimeOffset Compared, bool Waiting)> _publisherStatus = [];
    private PublicMonitoringResult? _monitoring;
    private bool _monitoringFailed, _statusChecking;
    private CancellationTokenSource? _statusExpiry;

    private void UpdateStatusVisibility()
    {
        if (!_windowActive || _page != "status" || _model.OfflineTest)
        {
            if (_statusLifetime is { } previous)
            {
                _statusLifetime = null;
                previous.Cancel();
                _statusChecking = false;
                _statusExpiry = null;
            }
            return;
        }
        if (_statusLifetime is not null) return;
        var lifetime = new CancellationTokenSource();
        _statusLifetime = lifetime;
        _ = ObserveStatusAsync(lifetime);
    }

    private async Task ObserveStatusAsync(CancellationTokenSource lifetime)
    {
        try
        {
            do
            {
                await RefreshPublicStatusAsync();
                await Task.Delay(TimeSpan.FromMinutes(3), lifetime.Token);
            } while (ReferenceEquals(lifetime, _statusLifetime));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { lifetime.Dispose(); }
    }

    private async Task RefreshPublicStatusAsync()
    {
        if (_statusLifetime is not { } lifetime || _statusChecking) return;
        _statusChecking = true;
        var client = new PublicStatusClient(_statusHttp ??= ApiClient.CreateHttpClient());
        var token = lifetime.Token;
        async Task<T?> Read<T>(Func<Task<T>> fetch) where T : class
        {
            try { return await fetch(); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or
                ApiException or System.Text.Json.JsonException or IOException or FormatException)
            { return null; }
        }
        try
        {
            var services = StatusServices.Select(entry => (entry.Service,
                Result: Read(() => client.DownloadAsync(entry.Service, token)))).ToArray();
            var monitoring = Read(() => client.DownloadMonitoringAsync(_monitoring, token));
            await Task.WhenAll(services.Select(item => (Task)item.Result).Append(monitoring));
            if (!ReferenceEquals(lifetime, _statusLifetime) || token.IsCancellationRequested) return;
            foreach (var item in services)
            {
                if (await item.Result is { } document)
                {
                    _functionalStatus[item.Service] = document;
                    _failedStatus.Remove(item.Service);
                    if (item.Service == PublicService.Api) UpdatePublisherStatus(document);
                }
                else _failedStatus.Add(item.Service);
            }
            var latest = await monitoring;
            _monitoringFailed = latest is null;
            if (latest is not null) _monitoring = latest;
        }
        finally
        {
            if (ReferenceEquals(lifetime, _statusLifetime))
            {
                _statusChecking = false;
                Render();
                ScheduleStatusExpiry(lifetime);
            }
        }
    }

    private void UpdatePublisherStatus(FunctionalStatus status)
    {
        if (status.Data?.Events is not { } evidence) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var year in evidence.Versions.Select(version => version.SchoolYear).Distinct())
        {
            var version = evidence.Find(year, "v1", now);
            if (version?.SourceCheckedAt is not { } compared || compared > now) continue;
            if (_publisherStatus.TryGetValue(year, out var old) && compared < old.Compared) continue;
            if (version.SourceState == "source_differs") _publisherStatus[year] = (compared, true);
            if (version.SourceState == "matched" && version.ReviewState == "reviewed"
                && version.ReviewCheckedAt is { } reviewed && reviewed <= now) _publisherStatus[year] = (compared, false);
        }
    }

    private bool PublisherWaiting(int year)
    {
        var published = _publisherStatus.GetValueOrDefault(year);
        var previousDifference = _model.EventSourceDifferenceAt(year);
        return published.Waiting || previousDifference is { } compared && compared > published.Compared;
    }

    private string? CurrentEventSourceMessage()
    {
        var messages = _model.SavedEventYears.Order().Select(year =>
            (Year: year, Warning: PublisherWaiting(year) ? "更新待ち。保存済みの行事を表示しています。"
                : _model.EventSourceDifferenceAt(year) is not null ? null : _model.EventSourceWarning(year)))
            .Where(item => item.Warning is not null).Select(item => item.Year + "年度：" + item.Warning).ToArray();
        return messages.Length == 0 ? null : string.Join("\n", messages);
    }

    private void ScheduleStatusExpiry(CancellationTokenSource lifetime)
    {
        _statusExpiry?.Cancel();
        var now = DateTimeOffset.UtcNow;
        var deadlines = _functionalStatus.Values.SelectMany(status => status.Features.Select(feature => feature.ValidUntil)
            .Append(status.Data?.Events?.ValidUntil ?? DateTimeOffset.MinValue))
            .Append(_monitoring?.ReceivedAt.AddMinutes(3) ?? DateTimeOffset.MinValue);
        var next = deadlines.Where(date => date > now).DefaultIfEmpty().Min();
        if (next <= now) { _statusExpiry = null; return; }
        var expiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _statusExpiry = expiry;
        _ = Invalidate();
        async Task Invalidate()
        {
            try
            {
                var wait = next - now;
                await Task.Delay(wait < TimeSpan.FromMinutes(3) ? wait : TimeSpan.FromMinutes(3), expiry.Token);
                if (!ReferenceEquals(expiry, _statusExpiry) || expiry.IsCancellationRequested) return;
                _statusExpiry = null;
                Render();
                ScheduleStatusExpiry(lifetime);
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested) { }
            finally { expiry.Dispose(); }
        }
    }

    private void BuildPublicStatus()
    {
        TitleText("ステータス", "page-status");
        BackToSettings();
        var refresh = Button("稼働・配信状況を更新", RefreshPublicStatusAsync, "status-refresh");
        refresh.IsEnabled = !_statusChecking && !_model.OfflineTest;
        Add(refresh);
        var now = DateTimeOffset.UtcNow;

        Add(SettingsSectionTitle("配信データ"));
        var year = _model.Today.SchoolYear();
        _model.EventRecords.TryGetValue(year, out var saved);
        var evidence = _failedStatus.Contains(PublicService.Api) ? null :
            _functionalStatus.GetValueOrDefault(PublicService.Api)?.Data?.Events?.Find(year, "v1", now);
        var waiting = PublisherWaiting(year);
        Add(SettingsGroup(
            SettingsRow("学校行事", () => OpenPage("events"), "status-events",
                waiting ? "更新待ち" : evidence?.SourceState == "matched" ? "元PDFの一致を確認" : "元PDFは未確認"),
            SettingsDescription("内容レビュー  " + (evidence is { ReviewState: "reviewed", ReviewCheckedAt: { } reviewed } && reviewed <= now
                ? "確認済み" : "未確認")),
            SettingsDescription("端末保存版  " + (saved is null ? "未取得" : evidence is null || saved.DataVersion is null
                ? "未確認" : saved.DataVersion == evidence.DataVersion ? "配信版と一致" : waiting ? "保存済み" : "更新あり"))));

        Add(SettingsSectionTitle("サービス稼働状況"));
        foreach (var (service, title, features) in StatusServices)
        {
            var panel = Panel(SettingsSectionTitle(title));
            var monitoring = _monitoring?.Snapshot.Services.GetValueOrDefault(service);
            var currentMonitor = _monitoring is { } previous && !_monitoringFailed && now >= previous.ReceivedAt
                && now - previous.ReceivedAt < TimeSpan.FromMinutes(3) ? monitoring : null;
            panel.Children.Add(SettingsDescription("公開監視  " + MonitorLabel(currentMonitor?.State)));
            if (_monitoring is { } received && monitoring is not null)
                panel.Children.Add(SettingsDescription("監視情報の取得  " + StatusTime(received.ReceivedAt)));
            var functional = _functionalStatus.GetValueOrDefault(service);
            foreach (var (id, featureTitle) in features)
            {
                var feature = functional?.Find(id);
                var state = !_failedStatus.Contains(service) ? feature?.Current(now) : null;
                var label = state switch
                {
                    FunctionalState.Available => "利用可能",
                    FunctionalState.Unavailable => "利用できません",
                    _ => "未確認"
                };
                panel.Children.Add(SettingsDescription(featureTitle + "  " + label
                    + (feature is null ? "" : "  確認 " + StatusTime(feature.CheckedAt))));
            }
            if (monitoring is not null)
            {
                var details = Panel();
                if (currentMonitor is null) details.Children.Add(SettingsDescription("前回取得した公開監視の情報です。"));
                if (monitoring.Availability is { } availability)
                    details.Children.Add(SettingsDescription("稼働率  " + availability.ToString("P2")));
                foreach (var day in monitoring.History.Reverse())
                    details.Children.Add(SettingsDescription(day.Date.ToString("yyyy/MM/dd") + "  " + MonitorLabel(day.State)));
                foreach (var incident in monitoring.Incidents)
                {
                    details.Children.Add(SettingsDescription(incident.Title));
                    foreach (var update in incident.Updates)
                        details.Children.Add(SettingsDescription(StatusTime(update.PublishedAt) + "  " + update.Message));
                }
                if (details.Children.Count > 0)
                    panel.Children.Add(new Expander { Header = "稼働履歴・障害情報", Content = details });
            }
            Add(Card(panel));
        }
        Add(new HyperlinkButton { Content = "公開ステータスページ", NavigateUri = PublicMonitoring.Page });
    }

    private static string StatusTime(DateTimeOffset date) => date.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy/MM/dd HH:mm") + "（日本時間）";
    private static string MonitorLabel(string? state) => state switch
    {
        "operational" => "正常稼働",
        "degraded" => "一部機能に影響",
        "downtime" => "停止中",
        "maintenance" => "メンテナンス中",
        "not_monitored" => "監視対象外",
        "recovered" or "recovered_degraded" => "復旧",
        _ => "未確認"
    };
}
