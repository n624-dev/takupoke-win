using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private LessonNames Names(string cls, ScheduleBlock block) => block.Content is ChangeContent change
        ? _model.Mappings?.Present(change.Change).After ?? change.Names
        : _model.Mappings?.Apply(block.Content.Names, cls) ?? block.Content.Names;
    private Button LessonButton(DateOnly day, string cls, ScheduleBlock block)
    {
        var names = Names(cls, block); var time = _model.Engine.CardTime(day, cls, block);
        var progress = _model.Engine.IsInProgress(day, cls, block, DateTimeOffset.UtcNow) ? "授業中 · " : "";
        var kind = block.Content switch { ChangeContent c => c.Change.KindLabel + " · ", SpecialContent { Kind: MaterialKind.Exam } => "試験 · ", SpecialContent => "返却 · ", _ => "" };
        var periods = block.StartPeriod == block.EndPeriod ? block.StartPeriod + "限" : $"{block.StartPeriod}〜{block.EndPeriod}限";
        var button = Button(names.Subject, () => LessonDetail(day, cls, block));
        button.Content = Panel(Text(progress + kind + ClassSelection.Display(cls) + " · " + periods, 12), Text(DisplayText.CellSubject(names.Subject), 17),
            Text(DisplayText.FullWidthKana(names.Teacher)), Text(DisplayText.FullWidthKana(names.Room)), Text(time?.Display ?? "時刻未確認", 12));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, progress + ClassSelection.Display(cls) + "、" + periods + "、" + DisplayText.Continuous(names.DetailSubject)
            + "、教員 " + DisplayText.Continuous(names.DetailTeacher) + "、教室 " + DisplayText.Continuous(names.DetailRoom) + "、" + (time?.Display ?? "時刻未確認"));
        return button;
    }
    private void BuildHome()
    {
        TitleText("ホーム", "page-home"); Add(Text(_model.Today.ToString("yyyy年M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")), 20));
        Add(Button("資料と更新情報を確認", _model.RefreshAsync, "refresh-home"));
        var updates = _model.Revisions.Where(p => p.Value.Changed).Select(p => AppViewModel.DataSetLabel(p.Key)).ToArray();
        if (updates.Length > 0) Add(Card(Panel(Text(string.Join("・", updates) + "のデータを取得・更新できます。"), Button("学校アカウントでデータを更新", _model.UpdateSharedAsync))));
        var classes = _model.Preferences.SelectedClasses;
        if (classes.Length == 0) Add(Card(Panel(Text("今日の授業を表示するには、クラスを選択してください。"), Button("クラスを選択", ChooseClasses))));
        var engine = _model.Engine; var day = _model.Today; var fullDay = engine.FullDayEventTitle(day, classes);
        var plan = engine.Plan(day);
        foreach (var e in plan.HeaderEvents(fullDay is not null)) Add(Card(Text(e.Title + " · " + e.Tag)));
        if (fullDay is not null) Add(Card(Text(fullDay, 20)));
        foreach (var cls in classes)
        {
            foreach (var message in engine.MissingMessages(day, cls)) Add(Text(ClassSelection.Display(cls) + "：" + message));
            foreach (var block in engine.Blocks(day, cls)) Add(LessonButton(day, cls, block));
        }
        var memo = plan.Events.Where(e => e.Tag == "行事メモ" && !plan.Events.Any(other => other.Tag != "行事メモ" && other.Title.Trim() == e.Title.Trim())).Select(e => e.Title).Distinct().ToArray();
        if (memo.Length > 0) Add(Card(Text("メモ\n" + string.Join("\n", memo))));
        Add(Button("今日の週を時間割で表示", () => { _model.WeekStart = day.DisplayWeekStart(); Navigation.SelectedItem = Navigation.MenuItems[2]; return Task.CompletedTask; }));
        Add(Text("お気に入り", 21));
        var favorites = _model.Links?.Items.Where(i => i.Visible && _model.Preferences.FavoriteIds.Contains(i.Id) && !_model.Preferences.HiddenIds.Contains(i.Id)).ToArray() ?? [];
        if (favorites.Length == 0) Add(Text("一覧からリンクをお気に入りに登録できます。"));
        foreach (var item in favorites) Add(LinkButton(item));
        var recommended = _model.Links?.Recommendations(_model.Preferences.HiddenIds).ToArray() ?? [];
        if (recommended.Length > 0) { Add(Text("おすすめ", 21)); foreach (var item in recommended) Add(LinkButton(item)); }
    }
    private async Task LessonDetail(DateOnly day, string cls, ScheduleBlock block)
    {
        if (block.Content is ChangeContent change) { await ChangeDetail(change.Change); return; }
        var names = Names(cls, block); var time = _model.Engine.CardTime(day, cls, block);
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
        var presentation = _model.Mappings?.Present(change) ?? new ChangePresentation(new(change.BeforeSubject), new(change.AfterSubject, change.Teacher, change.Room));
        var panel = Panel(Text(change.KindLabel + " · " + change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod, 20),
            Text("変更前：" + DisplayText.Continuous(presentation.Before.DetailSubject)), Text("変更後：" + DisplayText.Continuous(presentation.After.DetailSubject)),
            Text("教員：" + DisplayText.Continuous(presentation.After.DetailTeacher)), Text("教室：" + DisplayText.Continuous(presentation.After.DetailRoom)),
            Text("備考：" + change.Note), Text("時刻：" + string.Join(" / ", _model.Engine.ChangeTimes(change).Select(t => t?.Display ?? "未確認"))));
        if (SchoolDate.TryParse(change.ChangeDate, out var day) && change.DetailPeriods is { } periods)
        {
            var originalEngine = new TimetableEngine(_model.Data, false, true);
            var originals = periods.Select(p => originalEngine.Slot(day, p, change.DisplayClassName)).SelectMany(slot => slot.BaseLessons.Select(l => l.Names).Concat(slot.SpecialLessons.Select(l => l.Names))).Distinct().ToArray();
            if (originals.Length > 0) panel.Children.Add(new Expander { Header = "変更前の時間割", Content = Text(string.Join("\n", originals.Select(n => $"{n.DetailSubject} · {n.DetailTeacher} · {n.DetailRoom}"))) });
            var other = _model.Engine.ChangesOn(day, change.DisplayClassName).Where(c => c != change && c.DetailPeriods?.Intersect(periods).Any() == true).ToArray();
            if (other.Length > 0) panel.Children.Add(new Expander { Header = "同じ枠の他の変更", Content = Text(string.Join("\n", other.Select(c => c.KindLabel + " · " + c.BeforeSubject + " → " + c.AfterSubject + " · " + c.Note))) });
        }
        panel.Children.Add(new Expander { Header = "元の行の記載", Content = Text(change.RawText) });
        await Dialog("時間割変更の詳細", panel);
    }
}
