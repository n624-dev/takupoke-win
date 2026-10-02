using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        AutomationProperties.SetAutomationId(search, "link-search");
        search.TextChanged += (_, _) => { _linkQuery = search.Text; PopulateLinks(); }; Add(search);
        Add(Button("非表示のリンクを管理", RestoreHiddenLinks));
        _linkResults = new StackPanel { Spacing = 12 }; Add(_linkResults); PopulateLinks();
    }
    private void PopulateLinks()
    {
        if (_linkResults is null) return;
        _linkResults.Children.Clear();
        if (_model.Links is not { } links)
        { _linkResults.Children.Add(Text("リンク一覧を取得していません。学校アカウントで取得してください。")); _linkResults.Children.Add(Button("設定でデータを取得", () => OpenPage("account"))); return; }
        if (LinkSearch.Normalize(_linkQuery).Length > 0)
        {
            var results = links.Search(_linkQuery, _model.Preferences.HiddenIds).ToArray();
            if (results.Length == 0) _linkResults.Children.Add(Text("該当するリンクはありません。"));
            foreach (var link in results) _linkResults.Children.Add(LinkButton(link, links.Categories.FirstOrDefault(category => category.Id == link.CategoryId)?.Label));
        }
        else
            foreach (var category in links.Categories)
            {
                var items = category.Buttons.Where(i => i.Visible && !_model.Preferences.HiddenIds.Contains(i.Id)).ToArray();
                if (items.Length == 0) continue;
                _linkResults.Children.Add(Text(category.Label, 22)); foreach (var link in items) _linkResults.Children.Add(LinkButton(link));
            }
    }
    private Button LinkButton(LinkItem link, string? subtitle = null)
    {
        var favorite = _model.Preferences.FavoriteIds.Contains(link.Id);
        var button = Button((favorite ? "★ " : "") + link.Label, () => OpenLink(link), "link-" + link.Id);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.BorderThickness = new Thickness(0);
        var color = _model.Preferences.LinkColors.GetValueOrDefault(link.Id) ?? link.Color;
        var icon = new FontIcon { Glyph = "\uE71B", FontFamily = new FontFamily("Segoe Fluent Icons"), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), FontSize = 18 };
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Child = icon,
            Background = _accessibility.HighContrast ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : new SolidColorBrush(LinkColor(color)) };
        if (_accessibility.HighContrast) icon.Foreground = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(badge);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var label = Text(link.Label, 16); label.IsTextSelectionEnabled = false; labels.Children.Add(label);
        if (subtitle is not null) { var category = Text(subtitle, 12); category.IsTextSelectionEnabled = false; labels.Children.Add(category); }
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        if (favorite) { var star = Text("★", 18); star.IsTextSelectionEnabled = false; if (!_accessibility.HighContrast) star.Foreground = new SolidColorBrush(LinkColor("yellow")); Grid.SetColumn(star, 2); row.Children.Add(star); }
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, link.Label + (subtitle is null ? "" : "、" + subtitle) + (favorite ? "、お気に入り" : ""));
        var menu = new MenuFlyout();
        void Item(string label, Func<Task> action) { var item = new MenuFlyoutItem { Text = label }; item.Click += async (_, _) => await action(); menu.Items.Add(item); }
        Item("開く", () => OpenLink(link)); Item("今回だけ別の開き方で開く", () => OpenLink(link, true));
        Item(favorite ? "お気に入りから外す" : "お気に入りに追加", async () =>
        { await _model.SavePreferencesAsync(current => { var ids = current.FavoriteIds.ToHashSet(); if (favorite) ids.Remove(link.Id); else ids.Add(link.Id); return current with { FavoriteIds = ids }; }); });
        Item("色を変更", async () =>
        {
            var choice = new ComboBox { Header = "リンクの色" };
            foreach (var entry in LinkPalette) choice.Items.Add(new ComboBoxItem { Content = entry.Label, Tag = entry.Id });
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == color);
            if (await Dialog("色を変更", choice, "保存", "キャンセル") == ContentDialogResult.Primary && choice.SelectedItem is ComboBoxItem { Tag: string value })
                await _model.SavePreferencesAsync(current => current with { LinkColors = new Dictionary<string, string>(current.LinkColors) { [link.Id] = value } });
        });
        if (_model.Preferences.LinkColors.ContainsKey(link.Id)) Item("既定色に戻す", async () =>
        { await _model.SavePreferencesAsync(current => { var map = new Dictionary<string, string>(current.LinkColors); map.Remove(link.Id); return current with { LinkColors = map }; }); });
        Item("非表示", async () => { await _model.SavePreferencesAsync(current => { var hidden = current.HiddenIds.ToHashSet(); hidden.Add(link.Id); return current with { HiddenIds = hidden }; }); });
        button.ContextFlyout = menu;
        // Keyboard users can open the same context menu with the application key or Shift+F10.
        return button;
    }
    private static readonly (string Id, string Label)[] LinkPalette =
    [
        ("sky", "スカイ"), ("blue", "ブルー"), ("emerald", "エメラルド"), ("green", "グリーン"),
        ("amber", "アンバー"), ("yellow", "イエロー"), ("orange", "オレンジ"), ("rose", "ローズ"),
        ("red", "レッド"), ("indigo", "インディゴ"), ("purple", "パープル"), ("pink", "ピンク"),
        ("teal", "ティール"), ("slate", "スレート"), ("gray", "グレー")
    ];
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
        var original = _model.Preferences.HiddenIds.ToHashSet(); var hidden = original.ToHashSet(); var panel = new StackPanel { Spacing = 8 };
        foreach (var link in _model.Links?.Items.Where(i => i.Visible && hidden.Contains(i.Id)) ?? [])
        { var checkbox = new CheckBox { Content = link.Label, IsChecked = true }; checkbox.Unchecked += (_, _) => hidden.Remove(link.Id); checkbox.Checked += (_, _) => hidden.Add(link.Id); panel.Children.Add(checkbox); }
        if (panel.Children.Count == 0) panel.Children.Add(Text("非表示にしたリンクはありません。"));
        if (await Dialog("非表示のリンク（チェックを外すと再表示）", panel, "保存", "キャンセル") == ContentDialogResult.Primary)
            await _model.SavePreferencesAsync(current => { var next = current.HiddenIds.ToHashSet(); next.ExceptWith(original.Except(hidden)); return current with { HiddenIds = next }; });
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
