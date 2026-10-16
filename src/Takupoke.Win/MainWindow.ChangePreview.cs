using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Infrastructure.Parsing;

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
            var confirmation = Text(""); confirmation.Visibility = Visibility.Collapsed;
            panel.Children.Add(confirmation);
            var confirming = false;
            var controls = new List<CheckBox>();
            var groups = preview.ReviewGroups;
            var warningsByRow = preview.Warnings.ToLookup(warning => warning.Row);
            var groupedRows = preview.ReviewRows.Select(row => row.Row).ToHashSet();
            foreach (var warning in preview.Warnings.Where(warning => !groupedRows.Contains(warning.Row ?? 0)))
                panel.Children.Add(Text(warning.Message));
            foreach (var group in groups)
            {
                var row = group.Rows[0];
                var fields = Panel(SettingsSectionTitle($"{group.RangeLabel}の元の記載"));
                if (group.Rows.Count > 1)
                    fields.Children.Add(Text($"{group.Rows.Count}行とも、曜日以外の値はありません。"));
                else foreach (var warning in warningsByRow[row.Row])
                    fields.Children.Add(Text(warning.Message));
                foreach (var field in row.Fields) fields.Children.Add(DataField(field.Name, field.Value));
                if (preview.CanSkipRows)
                {
                    var choice = new CheckBox { Content = group.Rows.Count == 1 ? "この行を除外する"
                            : $"この{group.Rows.Count}行をまとめて除外する", Tag = group.RowIds,
                        IsChecked = group.RowIds.All(selected.Contains) };
                    AutomationProperties.SetAutomationId(choice, group.AutomationId);
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
                    bool Current() => epoch == _model.PrivateEpoch && !_model.Locked &&
                        _model.ParserYear == preview.SchoolYear && preview.ParserVersion == XlsxChangeReader.Version &&
                        _model.Materials.GetValueOrDefault(Takupoke.Core.MaterialKind.Changes)?.Source is { } source &&
                        source.Id == preview.SourceId && source.Digest == preview.Digest;
                    dialog.IsPrimaryButtonEnabled = !preview.CanSkipRows || selected.Count > 0;
                    dialog.PrimaryButtonClick += async (_, args) =>
                    {
                        if (!preview.CanSkipRows) return;
                        args.Cancel = true;
                        confirmation.Visibility = Visibility.Visible;
                        if (!Current())
                        {
                            confirmation.Text = "資料または補完年度が変わりました。プレビューを開き直してください。";
                            dialog.IsPrimaryButtonEnabled = false; return;
                        }
                        if (!confirming)
                        {
                            confirming = true;
                            confirmation.Text = $"{selected.Count}行を除外します（{ChangeReviewGroup.DescribeRanges(selected)}）。内容を確認して読み込んでください。";
                            dialog.PrimaryButtonText = "除外して読み込む"; return;
                        }
                        var deferral = args.GetDeferral();
                        dialog.IsPrimaryButtonEnabled = false;
                        foreach (var control in controls) control.IsEnabled = false;
                        try
                        {
                            var accepted = await _model.ApplyChangeRowSkipsAsync(preview, selected.ToArray());
                            args.Cancel = !accepted;
                            if (!accepted && epoch == _model.PrivateEpoch && !_model.Locked)
                                confirmation.Text = _model.Status;
                        }
                        finally
                        {
                            var current = Current();
                            dialog.IsPrimaryButtonEnabled = current && selected.Count > 0;
                            foreach (var control in controls) control.IsEnabled = current;
                            deferral.Complete();
                        }
                    };
                    void Changed(object sender, RoutedEventArgs args)
                    {
                        confirming = false; dialog.PrimaryButtonText = "選んだ行を除外";
                        confirmation.Visibility = Visibility.Collapsed;
                        var choice = (CheckBox)sender;
                        if (choice.IsChecked == true) selected.UnionWith((int[])choice.Tag);
                        else selected.ExceptWith((int[])choice.Tag);
                        dialog.IsPrimaryButtonEnabled = selected.Count > 0;
                    }
                    foreach (var control in controls)
                    { control.Checked += Changed; control.Unchecked += Changed; }
                });
            panel.Children.Clear();
            return;
        }
    }
}
