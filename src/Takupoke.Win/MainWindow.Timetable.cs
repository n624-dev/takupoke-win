using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private void BuildTimetable()
    {
        TitleText("時間割", "page-timetable");
        Add(Button("クラス：" + string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display)), ChooseClasses));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bounds = _model.Engine.ReachableWeeks(_model.Today, _model.Preferences.SelectedClasses);
        var start = _model.WeekStart;
        if (start < bounds.Lower) start = bounds.Lower; if (start > bounds.Upper) start = bounds.Upper; _model.WeekStart = start;
        var previous = Button("前の週", () => { _model.WeekStart = start.AddDays(-7); Render(); return Task.CompletedTask; }); previous.IsEnabled = start > bounds.Lower;
        var next = Button("次の週", () => { _model.WeekStart = start.AddDays(7); Render(); return Task.CompletedTask; }); next.IsEnabled = start < bounds.Upper;
        controls.Children.Add(previous); controls.Children.Add(Button("今週", () => { _model.WeekStart = _model.Today.DisplayWeekStart(); Render(); return Task.CompletedTask; })); controls.Children.Add(next);
        Add(controls);
        var calendar = new CalendarDatePicker { Date = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)),
            MinDate = new DateTimeOffset(bounds.Lower.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)),
            MaxDate = new DateTimeOffset(bounds.Upper.AddDays(6).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)), Header = "表示する週" };
        calendar.DateChanged += (_, args) => { if (args.NewDate is { } value) { _model.WeekStart = DateOnly.FromDateTime(value.DateTime).Monday(); Render(); } }; Add(calendar);
        var included = new ToggleSwitch { Header = "時間割変更を反映", IsOn = _model.Preferences.IncludesChanges };
        included.Toggled += async (_, _) => await _model.SavePreferencesAsync(_model.Preferences with { IncludesChanges = included.IsOn }); Add(included);
        var international = new ToggleSwitch { Header = "留学生向けの授業を表示", IsOn = _model.Preferences.International };
        international.Toggled += async (_, _) => await _model.SavePreferencesAsync(_model.Preferences with { International = international.IsOn }); Add(international);
        if (_model.Preferences.SelectedClasses.Length == 0) Add(Text("クラスを選択してください。"));
        else BuildWeekGrid(start);
        Add(Text("時間割変更一覧", 22)); Add(Button("一覧のクラスを選択", () => ChooseClasses(changes: true)));
        var range = new ComboBox { Header = "一覧の範囲", ItemsSource = new[] { "今日以降", "この週", "全件" }, SelectedIndex = (int)_model.Preferences.ChangeRange };
        range.SelectionChanged += async (_, _) => await _model.SavePreferencesAsync(_model.Preferences with { ChangeRange = (ChangeRange)range.SelectedIndex }); Add(range);
        var selected = (_model.Preferences.ChangeClasses.Length > 0 ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var changes = _model.Engine.Changes(selected, _model.Preferences.ChangeRange, _model.Today, start).ToArray();
        if (changes.Length == 0) Add(Text("この条件の時間割変更はありません。"));
        foreach (var change in changes) Add(Button(change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod + " · " + change.KindLabel + " · " + change.BeforeSubject + " → " + change.AfterSubject, () => ChangeDetail(change)));
    }
    private void BuildWeekGrid(DateOnly start)
    {
        var classes = _model.Preferences.SelectedClasses; var engine = _model.Engine;
        var days = engine.DisplayedDays(start, classes); var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        foreach (var day in days) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(190, classes.Length * 160)) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var period = 1; period <= 8; period++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });
            var time = engine.CommonPeriodTime(period, days, classes);
            var label = Text(period + "限\n" + (time?.Display ?? "日別に確認"), 13); Grid.SetRow(label, period); grid.Children.Add(label);
        }
        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex]; var plan = engine.Plan(day); var fullDay = engine.FullDayEventTitle(day, classes);
            var header = Panel(Text(day.ToString("M/d（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 18));
            foreach (var schoolEvent in plan.HeaderEvents(fullDay is not null)) header.Children.Add(Text(schoolEvent.Title, 12));
            foreach (var cls in classes) foreach (var message in engine.MissingMessages(day, cls)) header.Children.Add(Text(ClassSelection.Display(cls) + "：" + message, 12));
            Grid.SetColumn(header, dayIndex + 1); grid.Children.Add(header);
            if (fullDay is not null)
            {
                var card = Card(Text(fullDay, 20)); Grid.SetColumn(card, dayIndex + 1); Grid.SetRow(card, 1); Grid.SetRowSpan(card, 8); grid.Children.Add(card); continue;
            }
            var cellGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
            for (var period = 1; period <= 8; period++) cellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });
            var offset = 0;
            foreach (var cls in classes)
            {
                var positioned = TimetableEngine.Positioned(engine.Blocks(day, cls)); var lanes = Math.Max(1, positioned.Select(p => p.Lane + 1).DefaultIfEmpty(1).Max());
                for (var lane = 0; lane < lanes; lane++) cellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                foreach (var p in positioned)
                {
                    var button = LessonButton(day, cls, p.Block); button.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetColumn(button, offset + p.Lane); Grid.SetRow(button, p.Block.StartPeriod - 1); Grid.SetRowSpan(button, p.Block.EndPeriod - p.Block.StartPeriod + 1); cellGrid.Children.Add(button);
                }
                offset += lanes;
            }
            Grid.SetColumn(cellGrid, dayIndex + 1); Grid.SetRow(cellGrid, 1); Grid.SetRowSpan(cellGrid, 8); grid.Children.Add(cellGrid);
        }
        Add(grid);
    }
    private Task ChooseClasses() => ChooseClasses(false);
    private async Task ChooseClasses(bool changes)
    {
        var available = changes ? ClassSelection.Candidates.Concat(_model.Data.Changes?.Select(c => c.DisplayClassName) ?? []).Distinct().Order().ToArray() : ClassSelection.Candidates.ToArray();
        var selected = (changes ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var checks = available.Select(cls => new CheckBox { Content = ClassSelection.Display(cls), Tag = cls, IsChecked = selected.Contains(cls) }).ToArray();
        foreach (var check in checks) AutomationProperties.SetAutomationId(check, "class-" + (string)check.Tag);
        var warning = Text(changes ? "時間割の選択と独立した一覧専用のクラスです。" : "基本は1クラスです。1年生はホームルームと学科を1つずつ組み合わせられます。");
        if (!changes)
            foreach (var check in checks) check.Checked += (_, _) =>
            {
                var chosen = (string)check.Tag;
                foreach (var other in checks.Where(c => c != check && c.IsChecked == true && !ClassSelection.Compatible(chosen, (string)c.Tag))) other.IsChecked = false;
            };
        if (await Dialog(changes ? "変更一覧のクラス" : "時間割のクラス", Panel([warning, .. checks]), "保存", "キャンセル") == ContentDialogResult.Primary)
        {
            var values = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
            if (!changes && !ClassSelection.IsValid(values)) return;
            await _model.SavePreferencesAsync(changes ? _model.Preferences with { ChangeClasses = values } : _model.Preferences with { SelectedClasses = values });
        }
    }
}
