using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Takupoke.Core;
using Windows.System;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private string _linkQuery = "";
    private StackPanel? _linkResults;
    private void BuildLinks()
    {
        TitleText("一覧", "page-links");
        var search = new AutoSuggestBox { PlaceholderText = "リンクを検索（かな・ローマ字も使えます）", Text = _linkQuery };
        search.TextChanged += (_, args) => { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { _linkQuery = search.Text; PopulateLinks(); } }; Add(search);
        Add(Button("非表示のリンクを管理", RestoreHiddenLinks));
        _linkResults = new StackPanel { Spacing = 12 }; Add(_linkResults); PopulateLinks();
    }
    private void PopulateLinks()
    {
        if (_linkResults is null) return;
        _linkResults.Children.Clear();
        if (_model.Links is not { } links)
        { _linkResults.Children.Add(Text("リンク一覧を取得していません。学校アカウントで取得してください。")); _linkResults.Children.Add(Button("学校アカウントでデータを更新", _model.UpdateSharedAsync)); return; }
        if (_linkQuery.Trim().Length > 0)
        {
            var results = links.Search(_linkQuery, _model.Preferences.HiddenIds).ToArray();
            if (results.Length == 0) _linkResults.Children.Add(Text("該当するリンクはありません。"));
            foreach (var link in results) _linkResults.Children.Add(LinkButton(link));
        }
        else
            foreach (var category in links.Categories.OrderBy(c => c.SortOrder))
            {
                var items = category.Buttons.Where(i => i.Visible && !_model.Preferences.HiddenIds.Contains(i.Id)).OrderBy(i => i.SortOrder).ToArray();
                if (items.Length == 0) continue;
                _linkResults.Children.Add(Text(category.Label, 22)); foreach (var link in items) _linkResults.Children.Add(LinkButton(link));
            }
    }
    private Button LinkButton(LinkItem link)
    {
        var favorite = _model.Preferences.FavoriteIds.Contains(link.Id);
        var button = Button((favorite ? "★ " : "") + link.Label, () => OpenLink(link));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        var color = _model.Preferences.LinkColors.GetValueOrDefault(link.Id) ?? link.Color;
        if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) button.BorderBrush = new SolidColorBrush(LinkColor(color));
        button.BorderThickness = new Thickness(3, 1, 1, 1);
        var menu = new MenuFlyout();
        void Item(string label, Func<Task> action) { var item = new MenuFlyoutItem { Text = label }; item.Click += async (_, _) => await action(); menu.Items.Add(item); }
        Item("開く", () => OpenLink(link)); Item("今回だけ別の開き方で開く", () => OpenLink(link, true));
        Item(favorite ? "お気に入りから外す" : "お気に入りに追加", async () =>
        { var ids = _model.Preferences.FavoriteIds.ToHashSet(); if (!ids.Add(link.Id)) ids.Remove(link.Id); await _model.SavePreferencesAsync(_model.Preferences with { FavoriteIds = ids }); });
        Item("色を変更", async () =>
        {
            var colors = LinksPayload.Colors.Order().ToArray(); var choice = new ComboBox { Header = "リンクの色", ItemsSource = colors, SelectedItem = color };
            if (await Dialog("色を変更", choice, "保存", "キャンセル") == ContentDialogResult.Primary && choice.SelectedItem is string value)
            { var map = new Dictionary<string, string>(_model.Preferences.LinkColors) { [link.Id] = value }; await _model.SavePreferencesAsync(_model.Preferences with { LinkColors = map }); }
        });
        Item("非表示", async () => { var hidden = _model.Preferences.HiddenIds.ToHashSet(); hidden.Add(link.Id); await _model.SavePreferencesAsync(_model.Preferences with { HiddenIds = hidden }); });
        button.ContextFlyout = menu;
        // Keyboard users can open the same context menu with the application key or Shift+F10.
        return button;
    }
    private static Windows.UI.Color LinkColor(string color)
    {
        var rgb = color switch
        { "sky" => 0x0284C7u, "blue" => 0x2563EBu, "emerald" => 0x059669u, "green" => 0x16A34Au, "amber" => 0xD97706u, "yellow" => 0xCA8A04u,
            "orange" => 0xEA580Cu, "rose" => 0xE11D48u, "red" => 0xDC2626u, "indigo" => 0x4F46E5u, "purple" => 0x9333EAu,
            "pink" => 0xDB2777u, "teal" => 0x0D9488u, "slate" => 0x475569u, _ => 0x6B7280u };
        return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private async Task RestoreHiddenLinks()
    {
        var hidden = _model.Preferences.HiddenIds.ToHashSet(); var panel = new StackPanel { Spacing = 8 };
        foreach (var link in _model.Links?.Items.Where(i => hidden.Contains(i.Id)) ?? [])
        { var checkbox = new CheckBox { Content = link.Label, IsChecked = true }; checkbox.Unchecked += (_, _) => hidden.Remove(link.Id); checkbox.Checked += (_, _) => hidden.Add(link.Id); panel.Children.Add(checkbox); }
        if (panel.Children.Count == 0) panel.Children.Add(Text("非表示にしたリンクはありません。"));
        if (await Dialog("非表示のリンク（チェックを外すと再表示）", panel, "保存", "キャンセル") == ContentDialogResult.Primary)
            await _model.SavePreferencesAsync(_model.Preferences with { HiddenIds = hidden });
    }
    private async Task OpenLink(LinkItem link, bool opposite = false)
    {
        if (!LinksPayload.ValidUrl(link.Href)) { await Message("リンクを開けません", "リンクの形式を確認できませんでした。"); return; }
        var uri = new Uri(link.Href);
        var external = _model.Preferences.OpeningMode == LinkOpeningMode.External;
        if (opposite) external = !external;
        if (uri.Scheme != "https" || external)
        { if (!await Launcher.LaunchUriAsync(uri)) await Message("リンクを開けません", "このリンクに対応するWindowsアプリが見つかりませんでした。"); }
        else await OpenBrowser(uri, link.Label);
    }
}
