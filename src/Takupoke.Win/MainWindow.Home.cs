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
        var content = new StackPanel { Spacing = home ? 6 : 4, HorizontalAlignment = home ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
        if (change is not null) { var kind = Text(change.KindLabel, home ? 13 : 12); kind.Foreground = WarningBrush; content.Children.Add(kind); }
        if (inProgress) { var progress = Text("授業中", 12); progress.Foreground = ActionBrush; content.Children.Add(progress); }
        if (change?.IsCancellation != true)
        {
            var source = names.Subject.Trim().Length == 0 ? "変更を確認" : names.Subject.Trim();
            var subject = Text(home ? DisplayText.Continuous(source) : DisplayText.CellSubject(source), home ? 20 : 14);
            if (!home) { subject.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; subject.MaxLines = 2; subject.TextTrimming = TextTrimming.CharacterEllipsis; }
            if (change is not null && !home)
            {
                button.SizeChanged += (_, _) =>
                {
                    var shortName = _model.Mappings?.ShortSubject(change, _model.Data.Timetable?.Lessons ?? []);
                    var width = button.ActualWidth - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right;
                    if (width <= 0) return;
                    bool Fits(string value)
                    {
                        var measure = Text(value, subject.FontSize); measure.FontWeight = subject.FontWeight;
                        var line = Text("国", subject.FontSize); line.FontWeight = subject.FontWeight;
                        line.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                        measure.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                        return measure.DesiredSize.Height <= line.DesiredSize.Height * 2 + 0.5;
                    }
                    subject.Text = DisplayText.ChangeCardSubject(DisplayText.CellSubject(source), shortName is null ? null : DisplayText.CellSubject(shortName), Fits);
                };
            }
            content.Children.Add(subject);
        }
        if (showTime && !home && time is not null) content.Children.Add(Text(time.Display, 12));
        var teacher = block.Content is SpecialContent && !home ? names.Teacher : DisplayText.Metadata(names.Teacher);
        var room = block.Content is SpecialContent && !home ? names.Room : DisplayText.Metadata(names.Room);
        foreach (var metadata in new[] { teacher, room }.Where(value => value.Length > 0))
        {
            var text = Text(DisplayText.Continuous(metadata), home ? 15 : 12);
            if (!home) { text.MaxLines = 1; text.TextTrimming = TextTrimming.CharacterEllipsis; }
            content.Children.Add(text);
        }
        foreach (var text in content.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = false;
        if (!home)
        {
            foreach (var text in content.Children.OfType<TextBlock>()) text.TextAlignment = TextAlignment.Center;
            if (change is not null) button.Foreground = WarningBrush;
            button.Content = content; button.Padding = new Thickness(12, 10, 12, 10); button.Margin = new Thickness(3); button.CornerRadius = new CornerRadius(10);
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }
        else
        {
            var row = new Grid { ColumnSpacing = 20 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var timeColumn = new StackPanel { Spacing = 6, MinWidth = 84, VerticalAlignment = VerticalAlignment.Center };
            var periodLabel = Text(PeriodCaption(block.StartPeriod, block.EndPeriod), 13); periodLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var timeLabel = Text(DisplayText.PeriodTime(time), 14);
            foreach (var text in new[] { periodLabel, timeLabel }) { text.IsTextSelectionEnabled = false; text.TextAlignment = TextAlignment.Center; timeColumn.Children.Add(text); }
            row.Children.Add(timeColumn); Grid.SetColumn(content, 1); row.Children.Add(content); button.Content = row;
            button.Padding = new Thickness(20, 16, 20, 16); button.MinHeight = 112; button.CornerRadius = new CornerRadius(10);
            if (inProgress) { button.BorderThickness = new Thickness(3, 0, 0, 0); button.BorderBrush = ActionBrush; }
        }
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var periods = block.StartPeriod == block.EndPeriod ? block.StartPeriod + "限" : $"{block.StartPeriod}限から{block.EndPeriod}限";
        AutomationProperties.SetName(button, day.ToString("M月d日") + "、" + ClassSelection.Display(cls) + "、" + periods + "、"
            + DisplayText.Accessibility(names, time, change?.KindLabel) + (inProgress ? "、授業中" : ""));
        return button;
    }
    private static string PeriodCaption(int start, int end) => start == end ? start + "限" : $"{start}〜{end}限";
    private static string ScheduleValue(string value) => string.IsNullOrWhiteSpace(value) ? "記載なし" : DisplayText.Continuous(value);
    private StackPanel ScheduleHeading(string title, string icon)
    {
        var heading = Text(title, 22); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { FluentIcon(icon, 22), heading } };
    }
    private Border ScheduleNotice(string title, string message, string action, string icon, Func<Task> run)
    {
        var heading = Text(title, 18); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return Card(Panel(heading, Text(message), IconButton(action, icon, run)));
    }
    private static Grid ScheduleDetailRow(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 20, Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(92) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var caption = Text(label); caption.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        row.Children.Add(caption); var detail = Text(value); Grid.SetColumn(detail, 1); row.Children.Add(detail); return row;
    }
    private void BuildHome()
    {
        TitleText("ホーム", "page-home");
        Add(Text(_model.Today.ToString("yyyy年M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 18));
        Add(OperationControl(IconButton("更新を確認", "refresh", _model.RefreshAsync, "refresh-home")));
        var updates = _model.Revisions.Where(p => p.Value.Changed && (p.Key == DataSet.Times || p.Key == DataSet.Links && _model.Links is not null || p.Key == DataSet.Mapping && _model.Mappings is not null)).Select(p => AppViewModel.DataSetLabel(p.Key)).ToArray();
        if (updates.Length > 0) Add(ScheduleNotice("新しいデータがあります", string.Join("・", updates) + "を取得できます。", "データを取得", "download", () => OpenPage("account")));
        if (_model.RevisionFailures.Count > 0) Add(ScheduleNotice("更新を確認できませんでした", "通信状況を確認して、もう一度お試しください。保存済みのデータはそのまま使えます。", "もう一度確認", "refresh", _model.RefreshAsync));
        var classes = _model.Preferences.SelectedClasses;
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        if (classes.Length == 0) Add(ScheduleNotice("クラスを選んでください", "クラスを選ぶと、今日の授業をここに表示します。", "クラスを選択", "people", ChooseClasses));
        var engine = _model.HomeEngine; var day = _model.Today;
        var eventTitles = engine.Plan(day).Events.Select(e => e.Title).ToArray();
        if (eventTitles.Length > 0) Add(Card(Panel(ScheduleHeading("今日の学校行事", "calendar"), Text(DisplayText.FullWidthKana(string.Join("・", eventTitles))))));
        if (classes.Length > 0)
        {
            Add(ScheduleHeading("今日の授業", "calendar"));
            foreach (var cls in classes)
            {
                var heading = Text(ClassSelection.Display(cls), 18); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                var group = Panel(heading); var missing = engine.MissingMessages(day, cls); var blocks = engine.Blocks(day, cls);
                if (missing.Count == 0 && blocks.Count == 0 && _model.Data.Changes is not null && _model.SavedEventYears.Count > 0) group.Children.Add(Text("今日は授業がありません。"));
                foreach (var message in missing) group.Children.Add(Text(message));
                if (missing.Count > 0) group.Children.Add(IconButton("時間割ファイルを確認", "document", () => OpenPage("materials")));
                foreach (var block in blocks) group.Children.Add(LessonButton(day, cls, block, home: true));
                Add(Card(group));
            }
            if (_model.Data.Changes is null) Add(ScheduleNotice("時間割変更を表示できません", "時間割変更ファイルを選択して、状況を確認してください。", "時間割ファイルを開く", "document", () => OpenPage("materials")));
            if (_model.SavedEventYears.Count == 0) Add(ScheduleNotice("学校行事は未取得です", "学校行事を取得すると、授業のない日や試験の日を時間割に反映します。", "学校行事を取得", "calendar", () => OpenPage("events")));
        }
        Add(IconButton("時間割を見る", "calendar", () => { _model.OpenTodayWeek(); Navigation.SelectedItem = Navigation.MenuItems[2]; return Task.CompletedTask; }, "home-timetable"));
        var favorites = _model.Links?.Items.Where(i => i.Visible && _model.Preferences.FavoriteIds.Contains(i.Id) && !_model.Preferences.HiddenIds.Contains(i.Id)).ToArray() ?? [];
        if (favorites.Length > 0) { Add(ScheduleHeading("お気に入り", "star")); foreach (var item in favorites) Add(LinkButton(item)); }
        var recommended = _model.Links?.Recommendations(_model.Preferences.HiddenIds).ToArray() ?? [];
        if (recommended.Length > 0) { Add(ScheduleHeading("おすすめ", "link")); foreach (var item in recommended) Add(LinkButton(item)); }
    }
    private async Task LessonDetail(DateOnly day, string cls, ScheduleBlock block)
    {
        if (block.Content is ChangeContent change) { await ChangeDetail(change.Change); return; }
        var names = _model.Presentation.Names(cls, block); var time = _model.Engine.CardTime(day, cls, block);
        var panel = Panel(Text(ScheduleValue(names.DetailSubject), 24),
            ScheduleDetailRow("日付", day.ToString("yyyy年M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP"))),
            ScheduleDetailRow("クラス", ClassSelection.Display(cls)), ScheduleDetailRow("時限", PeriodCaption(block.StartPeriod, block.EndPeriod)),
            ScheduleDetailRow("時刻", time?.Display ?? "未確認"), ScheduleDetailRow("教員", ScheduleValue(names.DetailTeacher)),
            ScheduleDetailRow("教室", ScheduleValue(names.DetailRoom)),
            new Expander { Header = "PDFの記載名", Content = Text($"科目：{names.Subject}\n教員：{names.Teacher}\n教室：{names.Room}") });
        if (block.Content is NormalContent normal) panel.Children.Add(new Expander { Header = "元のセルの記載", Content = Text(normal.Lesson.SourceText) });
        if (block.Content is SpecialContent special) panel.Children.Add(new Expander { Header = "元のセルの記載", Content = Text(string.Join("\n", special.Lesson.Lines)) });
        await Dialog("授業詳細", panel);
    }
    private async Task ChangeDetail(ScheduleChange change)
    {
        var presentation = _model.Presentation.ChangeNames(change);
        var panel = Panel(Text(change.KindLabel, 24), ScheduleDetailRow("日付", SchoolDate.TryParse(change.ChangeDate, out var changedDay) ? changedDay.ToString("yyyy年M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")) : change.ChangeDate),
            ScheduleDetailRow("クラス", ClassSelection.Display(change.DisplayClassName)), ScheduleDetailRow("時限", change.DisplayPeriod),
            ScheduleDetailRow("変更前", ScheduleValue(_model.Presentation.BeforeSubject(change, detail: true))),
            ScheduleDetailRow("変更後", ScheduleValue(presentation.After.DetailSubject)), ScheduleDetailRow("教員", ScheduleValue(presentation.After.DetailTeacher)),
            ScheduleDetailRow("教室", ScheduleValue(presentation.After.DetailRoom)), ScheduleDetailRow("備考", ScheduleValue(change.Note)),
            ScheduleDetailRow("時刻", string.Join(" / ", _model.Engine.ChangeTimes(change).Select(t => t?.Display ?? "未確認")) is { Length: > 0 } times ? times : "未確認"));
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
