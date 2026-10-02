using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private ScrollViewer? _timetableScroller;
    private string _timetableScrollKey = "";
    private (string Key, double Horizontal, double Vertical)? _timetableScrollPosition;
    private bool _changesExpanded = true;
    private bool _restoringTimetableScroll;
    private void BuildTimetable()
    {
        TitleText("時間割", "page-timetable");
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        var bounds = _model.Engine.ReachableWeeks(_model.NavigationAnchor, _model.Preferences.SelectedClasses);
        var start = _model.WeekStart;
        if (start < bounds.Lower) start = bounds.Lower; if (start > bounds.Upper) start = bounds.Upper; _model.WeekStart = start;
        var toolbar = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsOpen = false };
        var calendar = new CalendarDatePicker { Date = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)),
            MinDate = new DateTimeOffset(bounds.Lower.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)),
            MaxDate = new DateTimeOffset(bounds.Upper.AddDays(6).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)), MinWidth = 140 };
        calendar.Foreground = ActionBrush;
        calendar.DateChanged += (_, args) => { if (args.NewDate is { } value && DateOnly.FromDateTime(value.DateTime).Monday() != _model.WeekStart) { _model.WeekStart = DateOnly.FromDateTime(value.DateTime).Monday(); Render(); } };
        toolbar.Content = calendar;
        var previous = new AppBarButton { Label = "前週", Icon = new SymbolIcon(Symbol.Back), IsEnabled = start > bounds.Lower, Foreground = ActionBrush };
        previous.Click += (_, _) => { _model.WeekStart = start.AddDays(-7); Render(); };
        var current = new AppBarButton { Label = "今週", Icon = new SymbolIcon(Symbol.Calendar), Foreground = ActionBrush };
        current.Click += (_, _) => { _model.WeekStart = _model.Today.DisplayWeekStart(); Render(); };
        var next = new AppBarButton { Label = "翌週", Icon = new SymbolIcon(Symbol.Forward), IsEnabled = start < bounds.Upper, Foreground = ActionBrush };
        next.Click += (_, _) => { _model.WeekStart = start.AddDays(7); Render(); };
        var selectedClasses = new AppBarButton { Label = "クラス：" + string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display)), Icon = new SymbolIcon(Symbol.People) };
        selectedClasses.Click += async (_, _) => await ChooseClasses();
        toolbar.PrimaryCommands.Add(previous); toolbar.PrimaryCommands.Add(current); toolbar.PrimaryCommands.Add(next); toolbar.PrimaryCommands.Add(selectedClasses);
        var included = PreferenceControl(new AppBarToggleButton { Label = "変更を反映", IsChecked = _model.Preferences.IncludesChanges });
        included.Click += async (_, _) => { var enabled = included.IsChecked == true; if (enabled != _model.Preferences.IncludesChanges) await _model.SavePreferencesAsync(current => current with { IncludesChanges = enabled }); };
        var international = PreferenceControl(new AppBarToggleButton { Label = "留学生向け授業", IsChecked = _model.Preferences.International });
        international.Click += async (_, _) => { var enabled = international.IsChecked == true; if (enabled != _model.Preferences.International) await _model.SavePreferencesAsync(current => current with { International = enabled }); };
        toolbar.SecondaryCommands.Add(included); toolbar.SecondaryCommands.Add(international); Add(toolbar);
        if (_model.Preferences.SelectedClasses.Length == 0) Add(Text("クラスを選択してください。"));
        else BuildWeekGrid(start);
        var weeklyEvents = Enumerable.Range(0, 7).Select(offset => start.AddDays(offset)).Select(day => (Day: day, Events: _model.Engine.Plan(day).Events)).Where(item => item.Events.Count > 0).ToArray();
        if (weeklyEvents.Length > 0)
        {
            Add(Text("この週の学校行事", 22));
            foreach (var item in weeklyEvents) Add(Text(item.Day.ToString("M/d（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")) + " · " + DisplayText.FullWidthKana(string.Join("・", item.Events.Select(e => e.Title)))));
        }
        if (_model.Data.Changes is null) Add(Text("時間割変更の解析結果がありません。"));
        if (_model.SavedEventYears.Count == 0) Add(Text("学校行事は未取得です。"));
        var list = new StackPanel { Spacing = 12 };
        void AddChange(UIElement element) => list.Children.Add(element);
        AddChange(Button("一覧のクラスを選択", () => ChooseClasses(changes: true)));
        var range = new ComboBox { Header = "一覧の範囲", ItemsSource = new[] { "今日以降", "この週", "全件" }, SelectedIndex = (int)_model.Preferences.ChangeRange };
        range.SelectionChanged += async (_, _) => { if (range.SelectedIndex is >= 0 and <= 2 && range.SelectedIndex != (int)_model.Preferences.ChangeRange) { var value = (ChangeRange)range.SelectedIndex; await _model.SavePreferencesAsync(current => current with { ChangeRange = value }); } }; AddChange(range);
        var selected = (_model.Preferences.ChangeClasses.Length > 0 ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var parsedClasses = (_model.Data.Timetable?.Lessons.Select(l => l.ClassName) ?? []).Concat(_model.Data.Changes?.Select(c => c.DisplayClassName) ?? []).ToHashSet();
        if (_model.Preferences.ChangeClasses.Any(cls => !parsedClasses.Contains(cls))) AddChange(Text("保存した対象クラスの一部は現在の資料にありません。選択は保持しています。"));
        if (_model.Preferences.ChangeClasses.Length > 0) AddChange(Button("時間割設定に戻す", () => _model.SavePreferencesAsync(current => current with { ChangeClasses = [] })));
        var changes = _model.Engine.Changes(selected, _model.Preferences.ChangeRange, _model.Today, start).ToArray();
        if (changes.Length == 0) AddChange(Text("この条件の時間割変更はありません。"));
        foreach (var change in changes) AddChange(Button(change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod + " · " + change.KindLabel + " · " + _model.Presentation.BeforeSubject(change) + " → " + (_model.Presentation.ChangeNames(change).After.Subject.Trim().Length == 0 ? "記載なし" : _model.Presentation.ChangeNames(change).After.Subject) + " · " + DisplayText.FullWidthKana(change.Note), () => ChangeDetail(change)));
        var changeList = new Expander { Header = "時間割変更一覧（" + changes.Length + "件）", Content = list, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = _changesExpanded };
        AutomationProperties.SetAutomationId(changeList, "timetable-change-list");
        changeList.Expanding += (_, _) => _changesExpanded = true;
        changeList.Collapsed += (_, _) => _changesExpanded = false;
        Add(changeList);
    }
    private void BuildWeekGrid(DateOnly start)
    {
        var classes = _model.Preferences.SelectedClasses; var engine = _model.Engine;
        var days = engine.DisplayedDays(start, classes); var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        var scale = Math.Max(1, _uiSettings.TextScaleFactor);
        var timeWidth = 40.0 * scale;
        var dayMinimum = Math.Max(58, classes.Length * 58) * scale;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(timeWidth) });
        foreach (var day in days) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = dayMinimum });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var allNoClass = days.All(day => engine.FullDayEventTitle(day, classes) is not null);
        var nestedRows = new List<Grid>();
        var fullDayCards = new List<Border>();
        var timeLabels = new List<Border>();
        for (var period = 1; period <= 8; period++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72 * scale) });
            for (var column = 0; column <= days.Count; column++)
            {
                var cell = new Border { BorderThickness = new Thickness(0, 0, 1, 1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] };
                Grid.SetRow(cell, period); Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
            var time = engine.CommonPeriodTime(period, days, classes);
            var texts = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var periodLabel = Text(allNoClass ? "" : period + "限", 15); periodLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var clockLabel = Text(allNoClass ? "" : DisplayText.PeriodTime(time), 9);
            AutomationProperties.SetAutomationId(periodLabel, "timetable-period-label-" + period);
            AutomationProperties.SetAutomationId(clockLabel, "timetable-clock-label-" + period);
            foreach (var label in new[] { periodLabel, clockLabel }) { label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.NoWrap; texts.Children.Add(label); }
            var timeCell = new Border { Child = texts, Padding = new Thickness(6, 3, 6, 3) };
            AutomationProperties.SetAutomationId(timeCell, "timetable-time-" + period);
            texts.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            timeWidth = Math.Max(timeWidth, Math.Ceiling(texts.DesiredSize.Width) + 12);
            timeLabels.Add(timeCell); Grid.SetRow(timeCell, period); grid.Children.Add(timeCell);
        }
        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex]; var plan = engine.Plan(day); var fullDay = engine.FullDayEventTitle(day, classes);
            var header = Panel(Text(day.ToString("M/d（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 15));
            foreach (var schoolEvent in plan.HeaderEvents(fullDay is not null)) header.Children.Add(Text(DisplayText.FullWidthKana(schoolEvent.Title), 9));
            foreach (var cls in classes) foreach (var message in engine.MissingMessages(day, cls)) header.Children.Add(Text(ClassSelection.Display(cls) + "：" + message, 9));
            var dayHeader = Card(header); dayHeader.Padding = new Thickness(8);
            if (day == _model.Today)
            {
                header.Children.Add(Text("今日", 12));
                if (!_accessibility.HighContrast)
                {
                    var color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                    dayHeader.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(36, color.R, color.G, color.B));
                }
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dayHeader, "timetable-day-" + day.ToString("yyyy-MM-dd"));
            Grid.SetColumn(dayHeader, dayIndex + 1); grid.Children.Add(dayHeader);
            if (fullDay is not null)
            {
                var title = Text(fullDay, 14); title.FontWeight = Microsoft.UI.Text.FontWeights.Bold; title.TextAlignment = TextAlignment.Center;
                var card = Card(title); card.Padding = new Thickness(3); fullDayCards.Add(card); Grid.SetColumn(card, dayIndex + 1); Grid.SetRow(card, 1); Grid.SetRowSpan(card, 8); grid.Children.Add(card); continue;
            }
            var cellGrid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
            for (var period = 1; period <= 8; period++) cellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72 * scale) });
            var offset = 0;
            foreach (var cls in classes)
            {
                var positioned = TimetableEngine.Positioned(engine.Blocks(day, cls)); var lanes = Math.Max(1, positioned.Select(p => p.Lane + 1).DefaultIfEmpty(1).Max());
                for (var lane = 0; lane < lanes; lane++) cellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                foreach (var p in positioned)
                {
                    var button = LessonButton(day, cls, p.Block, showTime: p.Block.StartPeriod != p.Block.EndPeriod || engine.CommonPeriodTime(p.Block.StartPeriod, days, classes) is null); button.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetColumn(button, offset + p.Lane); Grid.SetRow(button, p.Block.StartPeriod - 1); Grid.SetRowSpan(button, p.Block.EndPeriod - p.Block.StartPeriod + 1); cellGrid.Children.Add(button);
                }
                for (var period = 1; period <= 8; period++)
                {
                    if (positioned.Any(p => p.Block.StartPeriod <= period && p.Block.EndPeriod >= period)) continue;
                    var empty = Text("—", 11); empty.TextAlignment = TextAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(empty, offset); Grid.SetColumnSpan(empty, lanes); Grid.SetRow(empty, period - 1); cellGrid.Children.Add(empty);
                }
                offset += lanes;
            }
            grid.ColumnDefinitions[dayIndex + 1].MinWidth = 58 * Math.Max(1, cellGrid.ColumnDefinitions.Count) * scale;
            nestedRows.Add(cellGrid);
            Grid.SetColumn(cellGrid, dayIndex + 1); Grid.SetRow(cellGrid, 1); Grid.SetRowSpan(cellGrid, 8); grid.Children.Add(cellGrid);
        }
        grid.ColumnDefinitions[0].Width = new GridLength(timeWidth);
        void FitRows()
        {
            var heights = Enumerable.Repeat(allNoClass ? 9.0 * scale : 72.0 * scale, 8).ToArray();
            for (var row = 0; !allNoClass && row < timeLabels.Count; row++)
            {
                // Measure the content without the previous fixed row constraint.
                timeLabels[row].Child.Measure(new Windows.Foundation.Size(Math.Max(1, timeWidth - 12), double.PositiveInfinity));
                heights[row] = Math.Max(heights[row], Math.Ceiling(timeLabels[row].Child.DesiredSize.Height) + 6);
            }
            foreach (var cells in nestedRows)
                foreach (var button in cells.Children.OfType<Button>())
                {
                    var width = cells.ActualWidth / Math.Max(1, cells.ColumnDefinitions.Count);
                    if (width <= 0) continue;
                    button.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                    var span = Grid.GetRowSpan(button); var needed = Math.Ceiling(button.DesiredSize.Height / span);
                    for (var row = Grid.GetRow(button); row < Grid.GetRow(button) + span; row++) heights[row] = Math.Max(heights[row], needed);
                }
            if (allNoClass)
                foreach (var card in fullDayCards)
                {
                    var width = (grid.ActualWidth - timeWidth) / Math.Max(1, days.Count);
                    if (width <= 0) continue;
                    card.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                    var needed = Math.Ceiling(card.DesiredSize.Height / 8);
                    for (var row = 0; row < 8; row++) heights[row] = Math.Max(heights[row], needed);
                }
            for (var row = 0; row < 8; row++)
            {
                grid.RowDefinitions[row + 1].Height = new GridLength(heights[row]);
                foreach (var cells in nestedRows) cells.RowDefinitions[row].Height = new GridLength(heights[row]);
            }
        }
        grid.Loaded += (_, _) => FitRows();
        grid.SizeChanged += (_, args) => { if (Math.Abs(args.PreviousSize.Width - args.NewSize.Width) > 0.5) FitRows(); };
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroll, "timetable-grid-scroller");
        void FitWidth()
        {
            // ViewportHeight can still describe the previous page during a
            // content swap. Use the arranged host size so removing the
            // navigation header and resizing both expand the timetable.
            scroll.Height = Math.Max(260, PageScroller.ActualHeight - 144);
            foreach (var label in timeLabels)
            {
                label.Child.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                timeWidth = Math.Max(timeWidth, Math.Ceiling(label.Child.DesiredSize.Width) + 12);
            }
            grid.ColumnDefinitions[0].Width = new GridLength(timeWidth);
            var minimum = timeWidth + grid.ColumnDefinitions.Skip(1).Sum(column => column.MinWidth);
            var width = Math.Max(minimum, scroll.ActualWidth);
            if (Math.Abs(grid.Width - width) > 0.5 || double.IsNaN(grid.Width)) grid.Width = width;
        }
        scroll.Loaded += (_, _) => FitWidth(); scroll.SizeChanged += (_, _) => FitWidth();
        Microsoft.UI.Xaml.SizeChangedEventHandler resize = (_, _) => FitWidth();
        PageScroller.SizeChanged += resize; scroll.Unloaded += (_, _) => PageScroller.SizeChanged -= resize;
        _timetableScroller = scroll;
        _timetableScrollKey = start.Iso() + ":" + string.Join(",", classes);
        _restoringTimetableScroll = _timetableScrollPosition is { } position && position.Key == _timetableScrollKey;
        if (_timetableScrollPosition is { } saved && _restoringTimetableScroll)
            scroll.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_timetableScroller != scroll) return;
                scroll.ChangeView(saved.Horizontal, saved.Vertical, null, true);
                _restoringTimetableScroll = false;
            });
        if (_model.OfflineTest)
        {
            void LayoutDiagnostic(object? sender, object args)
            {
                // Expose only geometry from the isolated fake-data UI test.
                // ScrollViewer's UIA bounds may include unclipped content.
                var pageOrigin = PageScroller.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
                var geometry = FormattableString.Invariant($"Synthetic layout: root={RootGrid.ActualWidth:R},{RootGrid.ActualHeight:R}; page={PageScroller.ActualWidth:R},{PageScroller.ActualHeight:R}; viewport={PageScroller.ViewportWidth:R},{PageScroller.ViewportHeight:R}; pageOrigin={pageOrigin.X:R},{pageOrigin.Y:R}; host={PageHost.ActualWidth:R},{PageHost.ActualHeight:R}; table={scroll.ActualWidth:R},{scroll.ActualHeight:R}; tableHeight={scroll.Height:R}; scale={RootGrid.XamlRoot.RasterizationScale:R}");
                if (AutomationProperties.GetName(scroll) != geometry) AutomationProperties.SetName(scroll, geometry);
            }
            scroll.LayoutUpdated += LayoutDiagnostic;
            scroll.Unloaded += (_, _) => scroll.LayoutUpdated -= LayoutDiagnostic;
        }
        Add(scroll);
    }
    private Task ChooseClasses() => ChooseClasses(false);
    private async Task ChooseClasses(bool changes)
    {
        var available = changes ? ClassSelection.Candidates.Concat(_model.Preferences.ChangeClasses).Concat(_model.Data.Changes?.Select(c => c.DisplayClassName) ?? []).Distinct().Order().ToArray() : ClassSelection.Candidates.ToArray();
        var selected = (changes ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var checks = available.Select(cls => new CheckBox { Content = ClassSelection.Display(cls), Tag = cls, IsChecked = selected.Contains(cls) }).ToArray();
        foreach (var check in checks) AutomationProperties.SetAutomationId(check, "class-" + (string)check.Tag);
        var warning = Text(changes ? "時間割の選択と独立した一覧専用のクラスです。" : "基本は1クラスです。1年生はホームルームと学科を1つずつ組み合わせられます。");
        if (changes)
        {
            foreach (var check in checks) check.Checked += (_, _) => { if (checks.Count(c => c.IsChecked == true) > 30) { check.IsChecked = false; warning.Text = "選択できるクラスは30件までです。"; } };
        }
        if (!changes)
            foreach (var check in checks) check.Checked += (_, _) =>
            {
                var chosen = (string)check.Tag;
                foreach (var other in checks.Where(c => c != check && c.IsChecked == true && !ClassSelection.Compatible(chosen, (string)c.Tag))) other.IsChecked = false;
            };
        var choices = Panel(warning);
        if (changes) choices.Children.Add(Button("時間割と同じクラスに戻す", () => { foreach (var check in checks) check.IsChecked = false; return Task.CompletedTask; }));
        string Group(string cls) => cls is "1_1" or "1_2" or "1_3" ? "1年 ホームルーム" : cls.StartsWith("AI_", StringComparison.Ordinal) ? "専攻科" : cls.Length > 1 && cls[1] == '_' && cls[0] is >= '1' and <= '5' ? cls[0] + "年 学科" : "その他";
        foreach (var group in checks.GroupBy(check => Group((string)check.Tag)))
        {
            var heading = Text(group.Key, 18); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var groupHeader = Panel(heading);
            if (changes)
            {
                var groupChecks = group.ToArray();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                actions.Children.Add(Button("すべて選択", () =>
                {
                    var count = checks.Count(check => check.IsChecked == true);
                    foreach (var check in groupChecks.Where(check => check.IsChecked != true))
                    { if (count >= 30) { warning.Text = "選択できるクラスは30件までです。"; break; } check.IsChecked = true; count++; }
                    return Task.CompletedTask;
                }, "class-group-select-" + group.Key));
                actions.Children.Add(Button("すべて解除", () => { foreach (var check in groupChecks) check.IsChecked = false; return Task.CompletedTask; }, "class-group-clear-" + group.Key));
                groupHeader.Children.Add(actions);
            }
            choices.Children.Add(groupHeader);
            foreach (var check in group) choices.Children.Add(check);
        }
        if (await Dialog(changes ? "変更一覧のクラス" : "時間割のクラス", choices, "保存", "キャンセル") == ContentDialogResult.Primary)
        {
            var values = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
            if (!changes && !ClassSelection.IsValid(values)) return;
            await _model.SavePreferencesAsync(current => changes ? current with { ChangeClasses = values } : current with { SelectedClasses = values });
        }
    }
}
