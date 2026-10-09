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
    private Button WeekNavigationButton(string label, string icon, Action action, bool enabled, string id)
    {
        var button = IconButton(label, icon, () => { action(); return Task.CompletedTask; }, id);
        button.Content = FluentIcon(icon); button.Width = 44; button.Height = 44; button.Padding = new Thickness(12); button.IsEnabled = enabled;
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); return button;
    }
    private static Grid TimetableControlRow(FrameworkElement primary, FrameworkElement secondary)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Children.Add(primary); Grid.SetColumn(secondary, 1); row.Children.Add(secondary);
        bool? stacked = null;
        void Reflow()
        {
            if (row.ActualWidth <= 0) return;
            primary.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            secondary.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var needsStack = primary.DesiredSize.Width + secondary.DesiredSize.Width + 12 > row.ActualWidth + 0.5;
            if (stacked == needsStack) return;
            stacked = needsStack;
            row.ColumnDefinitions[1].Width = needsStack ? new GridLength(0) : GridLength.Auto;
            row.ColumnSpacing = needsStack ? 0 : 12; row.RowSpacing = needsStack ? 12 : 0;
            Grid.SetColumn(secondary, needsStack ? 0 : 1); Grid.SetRow(secondary, needsStack ? 1 : 0);
            secondary.HorizontalAlignment = needsStack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        row.Loaded += (_, _) => Reflow(); row.SizeChanged += (_, _) => Reflow();
        return row;
    }
    private void BuildTimetable()
    {
        TitleText("時間割", "page-timetable");
        AddMaterialWarnings();
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        if (_model.EventStorageMessage is { } storageFailure) Add(Card(Text(storageFailure)));
        var bounds = _model.Engine.ReachableWeeks(_model.NavigationAnchor, _model.Preferences.SelectedClasses);
        var start = _model.WeekStart;
        if (start < bounds.Lower) start = bounds.Lower; if (start > bounds.Upper) start = bounds.Upper; _model.WeekStart = start;
        var calendar = new CalendarDatePicker { Date = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)),
            MinDate = new DateTimeOffset(bounds.Lower.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)),
            MaxDate = new DateTimeOffset(bounds.Upper.AddDays(6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)), MinWidth = 156, MinHeight = 44,
            Language = "ja-JP", DateFormat = "{year.full}/{month.integer}/{day.integer}", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(calendar, "timetable-week-picker"); AutomationProperties.SetName(calendar, "表示する週を選択");
        calendar.DateChanged += (_, args) => { if (args.NewDate is { } value && DateOnly.FromDateTime(value.LocalDateTime).Monday() != _model.WeekStart) { _model.WeekStart = DateOnly.FromDateTime(value.LocalDateTime).Monday(); Render(); } };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(WeekNavigationButton("前の週", "chevron-left", () => { _model.WeekStart = start.AddDays(-7); Render(); }, start > bounds.Lower, "timetable-previous"));
        var current = IconButton("今週", "calendar", () => { _model.OpenTodayWeek(); Render(); return Task.CompletedTask; }, "timetable-current");
        current.MinHeight = 44; navigation.Children.Add(current);
        navigation.Children.Add(WeekNavigationButton("次の週", "chevron-right", () => { _model.WeekStart = start.AddDays(7); Render(); }, start < bounds.Upper, "timetable-next"));
        var weekRow = TimetableControlRow(calendar, navigation);
        var classLabel = "クラス：" + (_model.Preferences.SelectedClasses.Length == 0 ? "未選択" : string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display)));
        var classes = IconButton(classLabel, "people", ChooseClasses, "timetable-classes"); AutomationProperties.SetName(classes, classLabel); classes.HorizontalAlignment = HorizontalAlignment.Stretch; classes.HorizontalContentAlignment = HorizontalAlignment.Left;
        var displayMenu = new MenuFlyout();
        var included = PreferenceControl(new ToggleMenuFlyoutItem { Text = "時間割変更を反映", IsChecked = _model.Preferences.IncludesChanges });
        included.Click += async (_, _) =>
        {
            var enabled = included.IsChecked;
            RecordOfflineDisplayClick(completed: false, selected: enabled);
            if (enabled != _model.Preferences.IncludesChanges)
                await _model.SavePreferencesAsync(current => current with { IncludesChanges = enabled });
            RecordOfflineDisplayClick(completed: true, selected: enabled);
        };
        var international = PreferenceControl(new ToggleMenuFlyoutItem { Text = "留学生向け授業を表示", IsChecked = _model.Preferences.International });
        international.Click += async (_, _) => { var enabled = international.IsChecked; if (enabled != _model.Preferences.International) await _model.SavePreferencesAsync(current => current with { International = enabled }); };
        displayMenu.Items.Add(included); displayMenu.Items.Add(international);
        var displayLabel = Text("表示設定", 15); displayLabel.IsTextSelectionEnabled = false;
        var displayOptions = new DropDownButton { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { FluentIcon("settings"), displayLabel } }, Flyout = displayMenu, MinHeight = 44, Padding = new Thickness(16, 10, 16, 10) };
        AutomationProperties.SetAutomationId(displayOptions, "timetable-display-options"); AutomationProperties.SetName(displayOptions, "時間割の表示設定");
        var selectionRow = TimetableControlRow(classes, displayOptions); Add(Panel(weekRow, selectionRow));
        if (_model.Preferences.SelectedClasses.Length == 0) Add(ScheduleNotice("クラスが未選択です", "", "クラスを選択", "people", ChooseClasses));
        else BuildWeekGrid(start);
        var weeklyEvents = Enumerable.Range(0, 7).Select(offset => start.AddDays(offset)).Select(day => (Day: day, Events: _model.Engine.Plan(day).Events)).Where(item => item.Events.Count > 0).ToArray();
        if (weeklyEvents.Length > 0)
        {
            var events = Panel(ScheduleHeading("この週の学校行事", "calendar"));
            foreach (var item in weeklyEvents) events.Children.Add(ScheduleDetailRow(item.Day.ToString("M/d（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), DisplayText.FullWidthKana(string.Join("・", item.Events.Select(e => e.Title)))));
            Add(Card(events));
        }
        if (_model.Data.Changes is null) Add(ScheduleNotice("時間割変更を表示できません", "時間割変更ファイルを選択して、状況を確認してください。", "時間割ファイルを開く", "document", () => OpenPage("materials")));
        if (Enumerable.Range(0, 7).Any(offset => !_model.SavedEventYears.Contains(start.AddDays(offset).SchoolYear()))) Add(ScheduleNotice("学校行事は未取得です", "", "学校行事を取得", "calendar", () => OpenPage("events")));
        var list = new StackPanel { Spacing = 16, Padding = new Thickness(16) };
        void AddChange(UIElement element) => list.Children.Add(element);
        AddChange(IconButton("一覧のクラスを選択", "people", () => ChooseClasses(changes: true), "timetable-change-classes"));
        var range = new ComboBox { Header = "表示する期間", ItemsSource = new[] { "今日以降", "この週", "全件" }, SelectedIndex = (int)_model.Preferences.ChangeRange, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(range, "timetable-change-range");
        range.SelectionChanged += async (_, _) => { if (range.SelectedIndex is >= 0 and <= 2 && range.SelectedIndex != (int)_model.Preferences.ChangeRange) { var value = (ChangeRange)range.SelectedIndex; await _model.SavePreferencesAsync(current => current with { ChangeRange = value }); } }; AddChange(range);
        var selected = (_model.Preferences.ChangeClasses.Length > 0 ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var parsedClasses = (_model.Data.Timetable?.Lessons.Select(l => l.ClassName) ?? []).Concat(_model.Data.Changes?.Select(c => c.DisplayClassName) ?? []).ToHashSet();
        if (_model.Preferences.ChangeClasses.Any(cls => !parsedClasses.Contains(cls))) AddChange(Text("選択したクラスの一部が、現在の時間割にありません。"));
        if (_model.Preferences.ChangeClasses.Length > 0) AddChange(IconButton("時間割と同じクラスにする", "people", () => _model.SavePreferencesAsync(current => current with { ChangeClasses = [] })));
        var changes = _model.Engine.Changes(selected, _model.Preferences.ChangeRange, _model.Today, start).ToArray();
        if (changes.Length == 0) AddChange(Text("この期間・クラスの時間割変更はありません。"));
        for (var index = 0; index < changes.Length; index++) AddChange(ChangeListButton(changes[index], index));
        var changeList = new Expander { Header = "時間割変更（" + changes.Length + "件）", Content = list, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = _changesExpanded };
        AutomationProperties.SetAutomationId(changeList, "timetable-change-list");
        changeList.Expanding += (_, _) => _changesExpanded = true; changeList.Collapsed += (_, _) => _changesExpanded = false; Add(changeList);
    }
    private Button ChangeListButton(ScheduleChange change, int index)
    {
        var before = ScheduleValue(_model.Presentation.BeforeSubject(change)); var after = ScheduleValue(_model.Presentation.ChangeNames(change).After.Subject);
        var date = SchoolDate.TryParse(change.ChangeDate, out var day) ? day.ToString("M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")) : change.ChangeDate;
        var heading = Text(date + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod, 14);
        var kind = Text(change.KindLabel, 14); kind.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; kind.Foreground = WarningBrush;
        var changeNames = Text(before + " → " + after, 17); changeNames.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var content = Panel(heading, kind, changeNames); content.Spacing = 6;
        if (!string.IsNullOrWhiteSpace(change.Note) && change.Note.Trim() != change.KindLabel) content.Children.Add(Text(DisplayText.FullWidthKana(change.Note), 14));
        foreach (var text in content.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = false;
        var description = change.ChangeDate + "、" + ClassSelection.Display(change.DisplayClassName) + "、" + change.DisplayPeriod + "、" + change.KindLabel + "、" + before + "から" + after + "へ";
        var button = Button(description, () => ChangeDetail(change), "timetable-change-" + index);
        AutomationProperties.SetName(button, description);
        var row = new Grid { ColumnSpacing = 16 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(content); var arrow = FluentIcon("chevron-right", 18); arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
        button.Content = row; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(16); button.MinHeight = 100;
        return button;
    }
    private void BuildWeekGrid(DateOnly start)
    {
        var classes = _model.Preferences.SelectedClasses; var engine = _model.Engine;
        var days = engine.DisplayedDays(start, classes); var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        var scale = Math.Max(1, _uiSettings.TextScaleFactor);
        var timeWidth = 84.0 * scale;
        var dayMinimum = Math.Max(128, classes.Length * 128) * scale;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(timeWidth) });
        foreach (var day in days) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = dayMinimum });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var allNoClass = days.All(day => engine.FullDayEventTitle(day, classes) is not null);
        var nestedRows = new List<Grid>();
        var fullDayCards = new List<Border>();
        var timeLabels = new List<Border>();
        for (var period = 1; period <= 8; period++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100 * scale) });
            for (var column = 0; column <= days.Count; column++)
            {
                var cell = new Border { BorderThickness = new Thickness(0, 0, 1, 1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] };
                Grid.SetRow(cell, period); Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
            var time = engine.CommonPeriodTime(period, days, classes);
            var texts = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var periodLabel = Text(allNoClass ? "" : period + "限", 15); periodLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var clockLabel = Text(allNoClass || time is null ? "" : DisplayText.PeriodTime(time), 12);
            clockLabel.Visibility = allNoClass || time is null ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetAutomationId(periodLabel, "timetable-period-label-" + period);
            AutomationProperties.SetAutomationId(clockLabel, "timetable-clock-label-" + period);
            foreach (var label in new[] { periodLabel, clockLabel }) { label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.NoWrap; texts.Children.Add(label); }
            var timeCell = new Border { Child = texts, Padding = new Thickness(12, 10, 12, 10) };
            AutomationProperties.SetAutomationId(timeCell, "timetable-time-" + period);
            AutomationProperties.SetName(timeCell, period + "限の時刻");
            texts.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            timeWidth = Math.Max(timeWidth, Math.Ceiling(texts.DesiredSize.Width) + 24);
            timeLabels.Add(timeCell); Grid.SetRow(timeCell, period); grid.Children.Add(timeCell);
        }
        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex]; var plan = engine.Plan(day); var fullDay = engine.FullDayEventTitle(day, classes);
            var header = Panel(Text(day.ToString("M/d（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 16));
            foreach (var schoolEvent in plan.HeaderEvents(fullDay is not null)) header.Children.Add(Text(DisplayText.FullWidthKana(schoolEvent.Title), 12));
            foreach (var cls in classes) foreach (var message in engine.MissingMessages(day, cls)) header.Children.Add(Text(ClassSelection.Display(cls) + "：" + message, 12));
            var dayHeader = Card(header); dayHeader.Padding = new Thickness(12); dayHeader.Margin = new Thickness(3, 0, 3, 6); header.Spacing = 6;
            if (day == _model.Today)
            {
                header.Children.Add(Text("今日", 13));
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
                var card = Card(title); card.Background = TimetableCardBackground(); card.Padding = new Thickness(16); card.Margin = new Thickness(3); fullDayCards.Add(card); Grid.SetColumn(card, dayIndex + 1); Grid.SetRow(card, 1); Grid.SetRowSpan(card, 8); grid.Children.Add(card); continue;
            }
            var cellGrid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
            for (var period = 1; period <= 8; period++) cellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100 * scale) });
            var offset = 0;
            foreach (var cls in classes)
            {
                var positioned = TimetableEngine.Positioned(engine.Blocks(day, cls)); var lanes = Math.Max(1, positioned.Select(p => p.Lane + 1).DefaultIfEmpty(1).Max());
                for (var lane = 0; lane < lanes; lane++) cellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var index = 0; index < positioned.Count; index++)
                {
                    var p = positioned[index];
                    var button = LessonButton(day, cls, p.Block, showTime: p.Block.StartPeriod != p.Block.EndPeriod || engine.CommonPeriodTime(p.Block.StartPeriod, days, classes) is null, lane: p.Lane, index: index); button.VerticalAlignment = VerticalAlignment.Stretch;
                    var background = new Border { Background = TimetableCardBackground(), CornerRadius = new CornerRadius(10), Margin = new Thickness(3), IsHitTestVisible = false };
                    AutomationProperties.SetAccessibilityView(background, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                    Grid.SetColumn(background, offset + p.Lane); Grid.SetRow(background, p.Block.StartPeriod - 1); Grid.SetRowSpan(background, p.Block.EndPeriod - p.Block.StartPeriod + 1); cellGrid.Children.Add(background);
                    Grid.SetColumn(button, offset + p.Lane); Grid.SetRow(button, p.Block.StartPeriod - 1); Grid.SetRowSpan(button, p.Block.EndPeriod - p.Block.StartPeriod + 1); cellGrid.Children.Add(button);
                }
                for (var period = 1; period <= 8; period++)
                {
                    if (positioned.Any(p => p.Block.StartPeriod <= period && p.Block.EndPeriod >= period)) continue;
                    var empty = Text("—", 14); empty.TextAlignment = TextAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(empty, offset); Grid.SetColumnSpan(empty, lanes); Grid.SetRow(empty, period - 1); cellGrid.Children.Add(empty);
                }
                offset += lanes;
            }
            grid.ColumnDefinitions[dayIndex + 1].MinWidth = 128 * Math.Max(1, cellGrid.ColumnDefinitions.Count) * scale;
            nestedRows.Add(cellGrid);
            Grid.SetColumn(cellGrid, dayIndex + 1); Grid.SetRow(cellGrid, 1); Grid.SetRowSpan(cellGrid, 8); grid.Children.Add(cellGrid);
        }
        grid.ColumnDefinitions[0].Width = new GridLength(timeWidth);
        void FitRows()
        {
            var heights = Enumerable.Repeat(allNoClass ? 12.5 * scale : 100.0 * scale, 8).ToArray();
            for (var row = 0; !allNoClass && row < timeLabels.Count; row++)
            {
                // Measure the content without the previous fixed row constraint.
                timeLabels[row].Child.Measure(new Windows.Foundation.Size(Math.Max(1, timeWidth - 24), double.PositiveInfinity));
                heights[row] = Math.Max(heights[row], Math.Ceiling(timeLabels[row].Child.DesiredSize.Height) + 20);
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
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroll, "timetable-grid-scroller");
        void FitWidth()
        {
            foreach (var label in timeLabels)
            {
                label.Child.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                timeWidth = Math.Max(timeWidth, Math.Ceiling(label.Child.DesiredSize.Width) + 24);
            }
            grid.ColumnDefinitions[0].Width = new GridLength(timeWidth);
            var minimum = timeWidth + grid.ColumnDefinitions.Skip(1).Sum(column => column.MinWidth);
            var width = Math.Max(minimum, scroll.ActualWidth);
            if (Math.Abs(grid.Width - width) > 0.5 || double.IsNaN(grid.Width)) grid.Width = width;
        }
        scroll.Loaded += (_, _) => FitWidth(); scroll.SizeChanged += (_, _) => FitWidth();
        _timetableScroller = scroll;
        _timetableScrollKey = start.Iso() + ":" + string.Join(",", classes);
        _restoringTimetableScroll = _timetableScrollPosition is { } position && position.Key == _timetableScrollKey;
        if (_timetableScrollPosition is { } saved && _restoringTimetableScroll)
            scroll.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_timetableScroller != scroll) return;
                PageContent.UpdateLayout();
                scroll.ChangeView(saved.Horizontal, 0, null, true);
                PageScroller.ChangeView(null, saved.Vertical, null, true);
                _restoringTimetableScroll = false;
            });
        if (_model.OfflineTest)
        {
            void LayoutDiagnostic(object? sender, object args)
            {
                if (_timetableScroller != scroll) return;
                // Expose only geometry from the isolated fake-data UI test.
                // ScrollViewer's UIA bounds may include unclipped content.
                var pageOrigin = PageScroller.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
                var geometry = FormattableString.Invariant($"Synthetic layout: root={RootGrid.ActualWidth:R},{RootGrid.ActualHeight:R}; page={PageScroller.ActualWidth:R},{PageScroller.ActualHeight:R}; viewport={PageScroller.ViewportWidth:R},{PageScroller.ViewportHeight:R}; pageOrigin={pageOrigin.X:R},{pageOrigin.Y:R}; host={PageHost.ActualWidth:R},{PageHost.ActualHeight:R}; table={scroll.ActualWidth:R},{scroll.ActualHeight:R}; tableHeight={scroll.ActualHeight:R}; contentHeight={grid.ActualHeight:R}; tableTop={scroll.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point(0, 0)).Y:R}; pageBottomPadding={PageContent.Padding.Bottom:R}; textScale={_uiSettings.TextScaleFactor:R}; scale={RootGrid.XamlRoot.RasterizationScale:R}");
                if (AutomationProperties.GetName(scroll) != geometry) AutomationProperties.SetName(scroll, geometry);
            }
            scroll.LayoutUpdated += LayoutDiagnostic;
            scroll.Unloaded += (_, _) => scroll.LayoutUpdated -= LayoutDiagnostic;
        }
        Add(scroll);
    }
    private Microsoft.UI.Xaml.Media.Brush TimetableCardBackground()
    {
        var color = _accessibility.HighContrast ? _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : RootGrid.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 50, 50, 50) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B));
    }
    private Task ChooseClasses() => ChooseClasses(false);
    private async Task ChooseClasses(bool changes)
    {
        var available = changes ? ClassSelection.Candidates.Concat(_model.Preferences.ChangeClasses).Concat(_model.Data.Changes?.Select(c => c.DisplayClassName) ?? []).Distinct().Order().ToArray() : ClassSelection.Candidates.ToArray();
        var selected = (changes ? _model.Preferences.ChangeClasses : _model.Preferences.SelectedClasses).ToHashSet();
        var checks = available.Select(cls =>
        {
            var label = Text(ClassSelection.Display(cls), 16); label.IsTextSelectionEnabled = false;
            return new CheckBox { Content = label, Tag = cls, IsChecked = selected.Contains(cls), MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 4) };
        }).ToArray();
        foreach (var check in checks)
        {
            AutomationProperties.SetAutomationId(check, "class-" + (string)check.Tag);
            AutomationProperties.SetName(check, ClassSelection.Display((string)check.Tag));
        }
        var warning = Text(changes ? "変更一覧に表示するクラスを選んでください。時間割とは別に選べます。" : "クラスを1つ選んでください。1年生はホームルームと学科を1つずつ組み合わせて選べます。");
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
            var groupChoices = new Grid { ColumnSpacing = 12 };
            for (var column = 0; column < 3; column++) groupChoices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var position = 0;
            foreach (var check in group)
            {
                if (position % 3 == 0) groupChoices.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumn(check, position % 3); Grid.SetRow(check, position / 3); groupChoices.Children.Add(check); position++;
            }
            choices.Children.Add(Card(Panel(groupHeader, groupChoices)));
        }
        if (await Dialog(changes ? "変更一覧のクラス" : "時間割のクラス", choices, "保存", "キャンセル") == ContentDialogResult.Primary)
        {
            var values = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
            if (!changes && !ClassSelection.IsValid(values)) return;
            await _model.SavePreferencesAsync(current => changes ? current with { ChangeClasses = values } : current with { SelectedClasses = values });
        }
    }
}
