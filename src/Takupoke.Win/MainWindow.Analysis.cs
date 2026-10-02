using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private void BuildAnalysis(MaterialKind kind)
    {
        TitleText(AppViewModel.MaterialLabel(kind) + "の解析結果", "page-analysis-" + kind);
        Add(Button("資料の詳細に戻る", () => OpenPage("material." + kind)));
        var analysis = _model.Materials.GetValueOrDefault(kind)?.Analysis;
        if (analysis is null) { Add(Text("正常な解析結果はありません。")); return; }
        Add(Text("解析版：" + analysis.ParserVersion + " · " + analysis.SourceName));
        if (kind is MaterialKind.Timetable or MaterialKind.Changes)
        {
            var selected = (kind == MaterialKind.Timetable ? _model.Preferences.TimetableAnalysisClasses : _model.Preferences.ChangeAnalysisClasses).FirstOrDefault() ?? "";
            var available = kind == MaterialKind.Timetable ? analysis.Timetable?.Lessons.Select(l => l.ClassName).Distinct().Order().ToArray() ?? []
                : analysis.Changes?.Select(c => c.DisplayClassName).Distinct().Order().ToArray() ?? [];
            var picker = new ComboBox { Header = "確認するクラス（時間割の選択とは独立）" };
            picker.Items.Add(new ComboBoxItem { Content = "すべて", Tag = "" });
            foreach (var cls in available.Concat(selected.Length == 0 ? [] : new[] { selected }).Distinct().Order())
                picker.Items.Add(new ComboBoxItem { Content = ClassSelection.Display(cls) + (!available.Contains(cls) ? "（現在の資料に該当なし）" : ""), Tag = cls });
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == selected);
            AutomationProperties.SetAutomationId(picker, "analysis-class");
            picker.SelectionChanged += async (_, _) =>
            {
                if (picker.SelectedItem is not ComboBoxItem { Tag: string cls } || cls == selected || !PageContent.Children.Contains(picker)) return;
                var values = cls.Length == 0 ? Array.Empty<string>() : [cls];
                await _model.SavePreferencesAsync(kind == MaterialKind.Timetable ? _model.Preferences with { TimetableAnalysisClasses = values } : _model.Preferences with { ChangeAnalysisClasses = values });
            };
            Add(picker);
            if (selected.Length > 0 && !available.Contains(selected)) Add(Text("選択したクラスは現在の解析結果にありません。選択は保持しています。"));
            if (kind == MaterialKind.Timetable)
            {
                var weekday = _model.Preferences.TimetableAnalysisWeekday;
                var days = new ComboBox { Header = "曜日", ItemsSource = new[] { "すべて", "月", "火", "水", "木", "金" }, SelectedIndex = weekday };
                AutomationProperties.SetAutomationId(days, "analysis-weekday");
                days.SelectionChanged += async (_, _) => { if (days.SelectedIndex is >= 0 and <= 5 && days.SelectedIndex != weekday && PageContent.Children.Contains(days)) await _model.SavePreferencesAsync(_model.Preferences with { TimetableAnalysisWeekday = days.SelectedIndex }); };
                Add(days);
                foreach (var lesson in analysis.Timetable?.Lessons.Where(l => (selected.Length == 0 || l.ClassName == selected) && (weekday == 0 || l.Weekday == weekday)) ?? [])
                    Add(Button(DisplayText.Continuous(lesson.Names.Subject) + "\n" + ClassSelection.Display(lesson.ClassName) + " · " + new[] { "", "月", "火", "水", "木", "金" }[lesson.Weekday] + "曜 · " + lesson.Period + "限\n" + lesson.Names.Teacher + " · " + lesson.Names.Room, () => NormalAnalysisDetail(lesson)));
            }
            else foreach (var change in analysis.Changes?.Where(c => selected.Length == 0 || c.DisplayClassName == selected) ?? [])
                Add(Button(change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod + " · " + change.KindLabel + "\n" + change.BeforeSubject + " → " + change.AfterSubject + " · " + change.Note, () => ChangeDetail(change)));
        }
        else if (analysis.Special is { } special)
            foreach (var lesson in special.Lessons)
                Add(Button(lesson.Date + " · " + ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限\n" + string.Join(" · ", lesson.Lines), () => Message("特別時間割の授業詳細", lesson.Date + " · " + ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限\n時刻：" + (special.TimeFor(lesson)?.Display ?? "未確認") + "\n" + string.Join("\n", lesson.Lines) + "\nPDFページ：" + lesson.Page)));
    }
    private Task NormalAnalysisDetail(NormalLesson lesson)
    {
        var names = _model.Mappings?.Apply(lesson.Names, lesson.ClassName) ?? lesson.Names;
        return Dialog("授業詳細", Panel(Text(DisplayText.Continuous(names.DetailSubject), 22), Text(ClassSelection.Display(lesson.ClassName) + " · " + lesson.Period + "限"),
            Text("教員：" + (names.DetailTeacher.Length == 0 ? "記載なし" : DisplayText.Continuous(names.DetailTeacher))), Text("教室：" + (names.DetailRoom.Length == 0 ? "記載なし" : DisplayText.Continuous(names.DetailRoom))),
            new Expander { Header = "PDFの記載名", Content = Text(lesson.Names.Subject + "\n" + lesson.Names.Teacher + "\n" + lesson.Names.Room) },
            new Expander { Header = "元のセルの記載", Content = Text(lesson.SourceText) }));
    }
}
