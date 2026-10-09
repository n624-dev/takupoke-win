using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private async Task PreviewChanges()
    {
        if (await Dialog("解析の警告", Text("曜日の問題や、曜日だけが残った行を確認できます。除外する行を選び、確認後に読み込めます。"),
            "確認して表示", "キャンセル") != ContentDialogResult.Primary) return;
        var epoch = _model.PrivateEpoch;
        var preview = await _model.PreviewChangesAsync();
        var selected = new SortedSet<int>();
        while (epoch == _model.PrivateEpoch && !_model.Locked)
        {
            var panel = Panel(Text(preview.CanSkipRows
                ? "元の記載を確認し、除外する行を選んでください。ファイルの内容が更新されるまで適用します。"
                : "閲覧のみです。正常結果の保存や時間割への反映は行いません。"));
            var controls = new List<CheckBox>();
            foreach (var warning in preview.Warnings) panel.Children.Add(Text(warning.Message));
            foreach (var row in preview.ReviewRows)
            {
                var fields = Panel(SettingsSectionTitle($"{row.Row}行目の元の記載"));
                foreach (var field in row.Fields) fields.Children.Add(DataField(field.Name, field.Value));
                if (preview.CanSkipRows)
                {
                    var choice = new CheckBox { Content = "この行を除外する", Tag = row.Row,
                        IsChecked = selected.Contains(row.Row) };
                    AutomationProperties.SetAutomationId(choice, "change-skip-row-" + row.Row);
                    controls.Add(choice);
                    fields.Children.Add(choice);
                }
                panel.Children.Add(Card(fields));
            }
            foreach (var change in preview.Changes)
                panel.Children.Add(Card(Panel(Text(change.BeforeSubject + " → " + change.AfterSubject, 16),
                    SettingsDescription(change.ChangeDate + " · " +
                        Takupoke.Core.ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod),
                    Text(change.RawText))));

            var result = await Dialog("時間割変更のプレビュー", panel,
                preview.CanSkipRows ? "選んだ行を除外" : "閉じる", preview.CanSkipRows ? "キャンセル" : null,
                configure: dialog =>
                {
                    dialog.IsPrimaryButtonEnabled = !preview.CanSkipRows || selected.Count > 0;
                    void Changed(object sender, RoutedEventArgs args)
                    {
                        var choice = (CheckBox)sender;
                        if (choice.IsChecked == true) selected.Add((int)choice.Tag);
                        else selected.Remove((int)choice.Tag);
                        dialog.IsPrimaryButtonEnabled = selected.Count > 0;
                    }
                    foreach (var control in controls)
                    { control.Checked += Changed; control.Unchecked += Changed; }
                });
            panel.Children.Clear();
            if (!preview.CanSkipRows || result != ContentDialogResult.Primary
                || epoch != _model.PrivateEpoch || _model.Locked) return;
            if (await Dialog("選んだ行を除外して読み込む",
                Text($"{selected.Count}行を時間割変更から除外します。除外する行：{string.Join("、", selected)}。ファイルの内容が更新されるまで適用します。"),
                "除外して読み込む", "キャンセル") != ContentDialogResult.Primary) continue;
            if (epoch != _model.PrivateEpoch || _model.Locked) return;
            await _model.ApplyChangeRowSkipsAsync(preview, selected.ToArray());
            return;
        }
    }
}
