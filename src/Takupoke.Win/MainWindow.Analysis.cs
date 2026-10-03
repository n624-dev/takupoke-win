using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private void BuildAnalysis(MaterialKind kind)
    {
        TitleText(AppViewModel.MaterialLabel(kind) + "の解析結果", "page-analysis-" + kind);
        Add(IconButton("資料の詳細に戻る", "back", () => OpenPage("material." + kind)));
        var analysis = _model.Materials.GetValueOrDefault(kind)?.Analysis;
        if (analysis is null) { Add(Card(SettingsDescription("正常な解析結果はまだありません。資料の詳細で取得・解析状況を確認してください。"))); return; }
        Add(Card(Panel(DataField("解析した資料", analysis.SourceName))));
        var rows = new List<UIElement>();
        if (kind is MaterialKind.Timetable or MaterialKind.Changes)
        {
            var selected = (kind == MaterialKind.Timetable ? _model.Preferences.TimetableAnalysisClasses : _model.Preferences.ChangeAnalysisClasses).FirstOrDefault() ?? "";
            var available = kind == MaterialKind.Timetable ? analysis.Timetable?.Lessons.Select(l => l.ClassName).Distinct().Order().ToArray() ?? []
                : analysis.Changes?.Select(c => c.DisplayClassName).Distinct().Order().ToArray() ?? [];
            var picker = new ComboBox { Header = "クラス", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.Items.Add(new ComboBoxItem { Content = "すべて", Tag = "" });
            foreach (var cls in available.Concat(selected.Length == 0 ? [] : new[] { selected }).Distinct().Order())
                picker.Items.Add(new ComboBoxItem { Content = ClassSelection.Display(cls) + (!available.Contains(cls) ? "（現在の資料に該当なし）" : ""), Tag = cls });
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == selected);
            AutomationProperties.SetAutomationId(picker, "analysis-class");
            picker.SelectionChanged += async (_, _) =>
            {
                if (picker.SelectedItem is not ComboBoxItem { Tag: string cls } || cls == selected || !picker.IsLoaded) return;
                var values = cls.Length == 0 ? Array.Empty<string>() : [cls];
                await _model.SavePreferencesAsync(current => kind == MaterialKind.Timetable ? current with { TimetableAnalysisClasses = values } : current with { ChangeAnalysisClasses = values });
            };
            var filters = Panel(SettingsSectionTitle("確認する範囲"), SettingInput(picker));
            if (selected.Length > 0 && !available.Contains(selected)) filters.Children.Add(SettingsDescription("選択したクラスは現在の解析結果にありません。選択は保持しています。"));
            if (kind == MaterialKind.Timetable)
            {
                var weekday = _model.Preferences.TimetableAnalysisWeekday;
                var days = new ComboBox { Header = "曜日", ItemsSource = new[] { "すべて", "月", "火", "水", "木", "金" }, SelectedIndex = weekday, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetAutomationId(days, "analysis-weekday");
                days.SelectionChanged += async (_, _) => { if (days.SelectedIndex is >= 0 and <= 5 && days.SelectedIndex != weekday && days.IsLoaded) { var value = days.SelectedIndex; await _model.SavePreferencesAsync(current => current with { TimetableAnalysisWeekday = value }); } };
                filters.Children.Add(SettingInput(days));
                foreach (var lesson in analysis.Timetable?.Lessons.Where(l => (selected.Length == 0 || l.ClassName == selected) && (weekday == 0 || l.Weekday == weekday)) ?? [])
                    rows.Add(AnalysisResultRow(DisplayText.Continuous(lesson.Names.Subject),
                        ClassSelection.Display(lesson.ClassName) + " · " + new[] { "", "月", "火", "水", "木", "金" }[lesson.Weekday] + "曜 · " + lesson.Period + "限",
                        () => NormalAnalysisDetail(lesson), string.Join(" · ", new[] { DisplayText.Continuous(lesson.Names.Teacher), DisplayText.Continuous(lesson.Names.Room) }.Where(value => value.Length > 0))));
            }
            else foreach (var change in analysis.Changes?.Where(c => selected.Length == 0 || c.DisplayClassName == selected) ?? [])
                rows.Add(AnalysisResultRow(change.BeforeSubject + " → " + change.AfterSubject,
                    change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod + " · " + change.KindLabel,
                    () => ChangeDetail(change), change.Note));
            Add(Card(filters));
        }
        else if (analysis.Special is { } special)
            foreach (var lesson in special.Lessons)
                rows.Add(AnalysisResultRow(DisplayText.Continuous(lesson.Names.Subject), lesson.Date + " · " + ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限",
                    () => SpecialAnalysisDetail(special, lesson), string.Join(" · ", lesson.Lines)));
        Add(SettingsSectionTitle("読み取った内容 · " + rows.Count + "件"));
        if (rows.Count == 0) Add(Card(SettingsDescription("該当する項目はありません。確認するクラスや曜日を変更してください。")));
        else Add(SettingsGroup(rows.ToArray()));
        Add(TechnicalDetails(DataField("解析版", analysis.ParserVersion.ToString())));
    }
    private Button AnalysisResultRow(string title, string location, Func<Task> action, string? note = null)
    {
        var button = Button(title + "\n" + location + (string.IsNullOrEmpty(note) ? "" : "\n" + note), action);
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(12); button.MinHeight = 64;
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = Text(title.Length == 0 ? "科目の記載なし" : title, 16); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var content = Panel(heading, SettingsDescription(location)); content.Spacing = 6;
        if (!string.IsNullOrEmpty(note)) content.Children.Add(SettingsDescription(note));
        foreach (var text in content.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = false;
        grid.Children.Add(content);
        var arrow = FluentIcon("chevron-right", 14); arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 1); grid.Children.Add(arrow);
        button.Content = grid; return button;
    }
    private Task SpecialAnalysisDetail(SpecialAnalysis analysis, SpecialLesson lesson) => Dialog("特別時間割の授業詳細", Panel(
        Card(Panel(SettingsSectionTitle(DisplayText.Continuous(lesson.Names.Subject)), SettingsDescription(lesson.Date + " · " + ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限"),
            DataField("時刻", analysis.TimeFor(lesson)?.Display ?? "未確認"), DataField("教員", DisplayText.Continuous(lesson.Names.Teacher)), DataField("教室", DisplayText.Continuous(lesson.Names.Room)))),
        new Expander { Header = "元のセルの記載", Content = Text(string.Join("\n", lesson.Lines)) }));
    private Task NormalAnalysisDetail(NormalLesson lesson)
    {
        var names = _model.Mappings?.Apply(lesson.Names, lesson.ClassName) ?? lesson.Names;
        return Dialog("授業詳細", Panel(Card(Panel(SettingsSectionTitle(DisplayText.Continuous(names.DetailSubject)), SettingsDescription(ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限"),
            DataField("教員", DisplayText.Continuous(names.DetailTeacher)), DataField("教室", DisplayText.Continuous(names.DetailRoom)))),
            new Expander { Header = "PDFの記載名", Content = Text(lesson.Names.Subject + "\n" + lesson.Names.Teacher + "\n" + lesson.Names.Room) },
            new Expander { Header = "元のセルの記載", Content = Text(lesson.SourceText) }));
    }
}
