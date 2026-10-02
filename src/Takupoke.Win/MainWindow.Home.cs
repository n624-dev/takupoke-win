using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private Button LessonButton(DateOnly day, string cls, ScheduleBlock block, bool home = false, bool showTime = true)
    {
        var names = _model.Presentation.Names(cls, block, home); var engine = home ? _model.HomeEngine : _model.Engine;
        var time = engine.CardTime(day, cls, block);
        var inProgress = home && engine.IsInProgress(day, cls, block, DateTimeOffset.UtcNow);
        var change = (block.Content as ChangeContent)?.Change;
        var button = Button(names.Subject, () => LessonDetail(day, cls, block));
        var content = new StackPanel { Spacing = home ? 4 : 1, HorizontalAlignment = home ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
        if (change is not null) { var kind = Text(change.KindLabel, 12); kind.Foreground = WarningBrush; content.Children.Add(kind); }
        if (inProgress) { var progress = Text("授業中", 12); progress.Foreground = ActionBrush; content.Children.Add(progress); }
        if (change?.IsCancellation != true)
        {
            var source = names.Subject.Trim().Length == 0 ? "変更を確認" : names.Subject.Trim();
            var subject = Text(home ? DisplayText.Continuous(source) : DisplayText.CellSubject(source), home ? 17 : 14);
            if (change is not null && !home)
            {
                subject.SizeChanged += (_, _) =>
                {
                    var shortName = _model.Mappings?.ShortSubject(change, _model.Data.Timetable?.Lessons ?? []);
                    var width = subject.ActualWidth;
                    if (width <= 0) return;
                    bool Fits(string value) { var measure = Text(value, subject.FontSize); measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity)); return measure.DesiredSize.Width <= width; }
                    subject.Text = DisplayText.ChangeCardSubject(DisplayText.CellSubject(source), shortName is null ? null : DisplayText.CellSubject(shortName), Fits);
                };
            }
            content.Children.Add(subject);
        }
        if (showTime && !home && time is not null) content.Children.Add(Text(time.Display, 11));
        var teacher = block.Content is SpecialContent && !home ? names.Teacher : DisplayText.Metadata(names.Teacher);
        var room = block.Content is SpecialContent && !home ? names.Room : DisplayText.Metadata(names.Room);
        foreach (var metadata in new[] { teacher, room }.Where(value => value.Length > 0)) content.Children.Add(Text(DisplayText.Continuous(metadata), home ? 14 : 12));
        if (!home)
        {
            foreach (var text in content.Children.OfType<TextBlock>()) text.TextAlignment = TextAlignment.Center;
            if (change is not null) button.Foreground = WarningBrush;
            button.Content = content; button.Padding = new Thickness(3);
        }
        else
        {
            var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var timeLabel = Text(DisplayText.PeriodTime(time), 13); timeLabel.TextAlignment = TextAlignment.Center; timeLabel.MinWidth = 60;
            row.Children.Add(timeLabel); Grid.SetColumn(content, 1); row.Children.Add(content); button.Content = row;
            if (inProgress) { button.BorderThickness = new Thickness(3, 0, 0, 0); button.BorderBrush = ActionBrush; }
        }
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var periods = block.StartPeriod == block.EndPeriod ? block.StartPeriod + "限" : $"{block.StartPeriod}限から{block.EndPeriod}限";
        AutomationProperties.SetName(button, day.ToString("M月d日") + "、" + ClassSelection.Display(cls) + "、" + periods + "、"
            + DisplayText.Accessibility(names, time, change?.KindLabel) + (inProgress ? "、授業中" : ""));
        return button;
    }
    private void BuildHome()
    {
        TitleText("ホーム", "page-home"); Add(Text(_model.Today.ToString("yyyy年M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 20));
        Add(Button("資料と更新情報を確認", _model.RefreshAsync, "refresh-home"));
        var updates = _model.Revisions.Where(p => p.Value.Changed && (p.Key == DataSet.Times || p.Key == DataSet.Links && _model.Links is not null || p.Key == DataSet.Mapping && _model.Mappings is not null)).Select(p => AppViewModel.DataSetLabel(p.Key)).ToArray();
        if (updates.Length > 0) Add(Card(Panel(Text(string.Join("・", updates) + "のデータを取得・更新できます。"), Button("データの更新を確認", () => OpenPage("account")))));
        var classes = _model.Preferences.SelectedClasses;
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        if (classes.Length == 0) Add(Card(Panel(Text("今日の授業を表示するには、クラスを選択してください。"), Button("クラスを選択", ChooseClasses))));
        var engine = _model.HomeEngine; var day = _model.Today; var fullDay = engine.FullDayEventTitle(day, classes);
        var plan = engine.Plan(day);
        var eventTitles = plan.Events.Select(e => e.Title).ToArray();
        if (eventTitles.Length > 0) Add(Text(DisplayText.FullWidthKana(string.Join("・", eventTitles))));
        if (classes.Length > 0 && _model.Data.Changes is null) Add(Text("時間割変更の解析結果がありません。"));
        if (classes.Length > 0 && _model.SavedEventYears.Count == 0) Add(Text("学校行事は未取得です。"));
        foreach (var cls in classes)
        {
            Add(Text(ClassSelection.Display(cls), 16));
            var missing = engine.MissingMessages(day, cls); var blocks = engine.Blocks(day, cls);
            if (missing.Count == 0 && blocks.Count == 0 && _model.Data.Changes is not null && _model.SavedEventYears.Count > 0) Add(Text("授業はありません。"));
            foreach (var message in missing) Add(Text(ClassSelection.Display(cls) + "：" + message));
            foreach (var block in blocks) Add(LessonButton(day, cls, block, home: true));
        }
        Add(AccentButton("時間割を見る", () => { _model.OpenTodayWeek(); Navigation.SelectedItem = Navigation.MenuItems[2]; return Task.CompletedTask; }, "home-timetable"));
        var favorites = _model.Links?.Items.Where(i => i.Visible && _model.Preferences.FavoriteIds.Contains(i.Id) && !_model.Preferences.HiddenIds.Contains(i.Id)).ToArray() ?? [];
        if (favorites.Length > 0) Add(Text("お気に入り", 21));
        foreach (var item in favorites) Add(LinkButton(item));
        var recommended = _model.Links?.Recommendations(_model.Preferences.HiddenIds).ToArray() ?? [];
        if (recommended.Length > 0) { Add(Text("おすすめ", 21)); foreach (var item in recommended) Add(LinkButton(item)); }
    }
    private async Task LessonDetail(DateOnly day, string cls, ScheduleBlock block)
    {
        if (block.Content is ChangeContent change) { await ChangeDetail(change.Change); return; }
        var names = _model.Presentation.Names(cls, block); var time = _model.Engine.CardTime(day, cls, block);
        var panel = Panel(Text(DisplayText.Continuous(names.DetailSubject), 22), Text($"{day:yyyy/M/d} · {ClassSelection.Display(cls)} · {block.StartPeriod}〜{block.EndPeriod}限"),
            Text("時刻：" + (time?.Display ?? "未確認")), Text("教員：" + (names.DetailTeacher.Length > 0 ? DisplayText.Continuous(names.DetailTeacher) : "記載なし")),
            Text("教室：" + (names.DetailRoom.Length > 0 ? DisplayText.Continuous(names.DetailRoom) : "記載なし")),
            new Expander { Header = "PDFの記載名", Content = Text($"科目：{names.Subject}\n教員：{names.Teacher}\n教室：{names.Room}") });
        if (block.Content is NormalContent normal) panel.Children.Add(new Expander { Header = "元のセルの記載", Content = Text(normal.Lesson.SourceText) });
        if (block.Content is SpecialContent special) panel.Children.Add(new Expander { Header = "元のセルの記載", Content = Text(string.Join("\n", special.Lesson.Lines)) });
        await Dialog("授業詳細", panel);
    }
    private async Task ChangeDetail(ScheduleChange change)
    {
        var presentation = _model.Presentation.ChangeNames(change);
        var panel = Panel(Text(change.KindLabel + " · " + change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod, 20),
            Text("変更前：" + DisplayText.Continuous(_model.Presentation.BeforeSubject(change, detail: true))), Text("変更後：" + DisplayText.Continuous(presentation.After.DetailSubject)),
            Text("教員：" + DisplayText.Continuous(presentation.After.DetailTeacher)), Text("教室：" + DisplayText.Continuous(presentation.After.DetailRoom)),
            Text("備考：" + DisplayText.FullWidthKana(change.Note)), Text("時刻：" + string.Join(" / ", _model.Engine.ChangeTimes(change).Select(t => t?.Display ?? "未確認"))));
        if (presentation.Before.DetailTeacher.Length > 0) panel.Children.Add(Text("変更前の教員：" + DisplayText.FullWidthKana(presentation.Before.DetailTeacher)));
        if (presentation.Before.DetailRoom.Length > 0) panel.Children.Add(Text("変更前の教室：" + DisplayText.FullWidthKana(presentation.Before.DetailRoom)));
        if (change.BeforeSubject.Length == 0 && _model.Presentation.NormalOriginals(change).Count > 0) panel.Children.Add(Text("変更前は通常時間割から表示しています。"));
        if (SchoolDate.TryParse(change.ChangeDate, out var day) && change.DetailPeriods is { } periods)
        {
            var originalEngine = new TimetableEngine(_model.Data, false, true);
            var originals = periods.Select(p => originalEngine.Slot(day, p, change.DisplayClassName)).SelectMany(slot => slot.BaseLessons.Select(l => _model.Mappings?.Apply(l.Names, l.ClassName) ?? l.Names)).Distinct().ToArray();
            if (originals.Length > 0) panel.Children.Add(new Expander { Header = "変更前の時間割", Content = Text(string.Join("\n", originals.Select(n => $"{n.DetailSubject} · {n.DetailTeacher} · {n.DetailRoom}"))) });
            var other = _model.Engine.ChangesOn(day, change.DisplayClassName).Where(c => c != change && c.DetailPeriods?.Intersect(periods).Any() == true).ToArray();
            if (other.Length > 0) panel.Children.Add(new Expander { Header = "同じ枠の他の変更", Content = Text(string.Join("\n", other.Select(c => c.KindLabel + " · " + c.DisplayPeriod + " · " + _model.Presentation.ChangeNames(c).Before.DetailSubject + " → " + _model.Presentation.ChangeNames(c).After.DetailSubject + " · " + DisplayText.FullWidthKana(c.Note) + "\n元の記載：" + c.RawText))) });
        }
        var specialOriginals = _model.Presentation.SpecialOriginals(change);
        if (specialOriginals.Count > 0) panel.Children.Add(new Expander { Header = "変更前の試験・返却時間割", Content = Text(string.Join("\n", specialOriginals.Select(item =>
            (item.Kind == MaterialKind.Exam ? "試験" : "返却") + " · " + item.Names.Subject + " · " + (item.Time?.Display ?? "時刻未確認") + " · " + item.Names.Teacher + " · " + item.Names.Room))) });
        panel.Children.Add(new Expander { Header = "元の行の記載", Content = Text(change.RawText) });
        await Dialog("時間割変更の詳細", panel);
    }
}
