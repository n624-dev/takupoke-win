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
    private AutoSuggestBox? _linkSearch;
    private StackPanel? _linkResults;
    private void BuildLinks()
    {
        TitleText("一覧", "page-links");
        var search = new AutoSuggestBox { PlaceholderText = "リンク名を検索", Text = _linkQuery };
        _linkSearch = search;
        AutomationProperties.SetAutomationId(search, "link-search");
        AutomationProperties.SetName(search, "リンクを検索");
        search.TextChanged += (_, _) =>
        {
            // AutoSuggestBox queues TextChanged. A snapshot refresh can replace
            // this control before that callback arrives; its old text must not
            // overwrite input in the replacement or update a different page.
            if (_page != "links" || !ReferenceEquals(_linkSearch, search)) return;
            _linkQuery = search.Text;
            PopulateLinks();
        }; Add(search);
        Add(IconButton("非表示のリンクを管理", "settings", RestoreHiddenLinks));
        _linkResults = new StackPanel { Spacing = 24 }; Add(_linkResults); PopulateLinks();
    }
    private void PopulateLinks()
    {
        if (_linkResults is null || DeferRenderForPopups()) return;
        _linkResults.Children.Clear();
        if (_model.Links is not { } links)
        { _linkResults.Children.Add(Card(Panel(Text("リンクはまだ取得していません。", 18), IconButton("データを取得", "download", () => OpenPage("account"))))); return; }
        if (LinkSearch.Normalize(_linkQuery).Length > 0)
        {
            var results = links.Search(_linkQuery, _model.Preferences.HiddenIds).ToArray();
            if (results.Length == 0) _linkResults.Children.Add(Text("該当するリンクはありません。"));
            var group = new StackPanel { Spacing = 8 };
            foreach (var link in results) group.Children.Add(LinkButton(link, links.Categories.FirstOrDefault(category => category.Id == link.CategoryId)?.Label));
            if (results.Length > 0) _linkResults.Children.Add(Card(group));
        }
        else
            foreach (var category in links.Categories)
            {
                var items = category.Buttons.Where(i => i.Visible && !_model.Preferences.HiddenIds.Contains(i.Id)).ToArray();
                if (items.Length == 0) continue;
                var heading = Text(category.Label, 20); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                var group = Panel(heading);
                foreach (var link in items) group.Children.Add(LinkButton(link));
                _linkResults.Children.Add(Card(group));
            }
        if (_linkResults.Children.Count == 0) _linkResults.Children.Add(Card(Text("表示するリンクがありません。非表示のリンクの設定を確認してください。")));
    }
    private FrameworkElement LinkButton(LinkItem link, string? subtitle = null)
    {
        var favorite = _model.Preferences.FavoriteIds.Contains(link.Id);
        var button = Button(link.Label, () => OpenLink(link), "link-" + link.Id);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.BorderThickness = new Thickness(0);
        button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        button.Padding = new Thickness(12); button.MinHeight = 64;
        var color = _model.Preferences.LinkColors.GetValueOrDefault(link.Id) ?? link.Color;
        var icon = FluentIcon("link", 20); icon.Foreground = new SolidColorBrush(TextOnTint(LinkColor(color)));
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Child = icon,
            Background = _accessibility.HighContrast ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : new SolidColorBrush(LinkColor(color)) };
        if (_accessibility.HighContrast) icon.Foreground = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(badge);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var label = Text(link.Label, 16); label.IsTextSelectionEnabled = false; labels.Children.Add(label);
        if (subtitle is not null) { var category = SettingsDescription(subtitle); category.IsTextSelectionEnabled = false; labels.Children.Add(category); }
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        if (favorite) { var star = FluentIcon("star-filled", 18); star.VerticalAlignment = VerticalAlignment.Center; if (!_accessibility.HighContrast) star.Foreground = new SolidColorBrush(LinkColor("yellow")); Grid.SetColumn(star, 2); row.Children.Add(star); }
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, link.Label + (subtitle is null ? "" : "、" + subtitle) + (favorite ? "、お気に入り" : ""));
        var menu = new MenuFlyout();
        void Item(string label, string iconName, Func<Task> action) { var item = new MenuFlyoutItem { Text = label, Icon = FluentIcon(iconName) }; item.Click += async (_, _) => await action(); menu.Items.Add(item); }
        Item("開く", "open", () => OpenLink(link));
        if (Uri.TryCreate(link.Href, UriKind.Absolute, out var address) && address.Scheme == "https") Item(_model.Preferences.OpeningMode == LinkOpeningMode.External ? "今回だけアプリ内で開く" : "今回だけ既定のブラウザで開く", "globe", () => OpenLink(link, true));
        Item(favorite ? "お気に入りから外す" : "お気に入りに追加", "star", async () =>
        { await _model.SavePreferencesAsync(current => { var ids = current.FavoriteIds.ToHashSet(); if (favorite) ids.Remove(link.Id); else ids.Add(link.Id); return current with { FavoriteIds = ids }; }); });
        Item("アイコンの色を変更", "palette", async () =>
        {
            var choice = new ComboBox { Header = "リンクの色" };
            foreach (var entry in LinkPalette) choice.Items.Add(new ComboBoxItem { Content = entry.Label, Tag = entry.Id });
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == color);
            if (await Dialog("色を変更", choice, "保存", "キャンセル") == ContentDialogResult.Primary && choice.SelectedItem is ComboBoxItem { Tag: string value })
                await _model.SavePreferencesAsync(current => current with { LinkColors = new Dictionary<string, string>(current.LinkColors) { [link.Id] = value } });
        });
        if (_model.Preferences.LinkColors.ContainsKey(link.Id)) Item("既定の色に戻す", "palette", async () =>
        { await _model.SavePreferencesAsync(current => { var map = new Dictionary<string, string>(current.LinkColors); map.Remove(link.Id); return current with { LinkColors = map }; }); });
        Item("非表示にする", "dismiss", async () => { await _model.SavePreferencesAsync(current => { var hidden = current.HiddenIds.ToHashSet(); hidden.Add(link.Id); return current with { HiddenIds = hidden }; }); });
        button.ContextFlyout = menu;
        // Keyboard users can open the same context menu with the application key or Shift+F10.
        var more = Button(link.Label + "の編集", () => Task.CompletedTask, "link-menu-" + link.Id);
        more.Content = FluentIcon("more"); more.Flyout = menu; more.MinHeight = more.MinWidth = 40;
        more.Padding = new Thickness(10); more.BorderThickness = new Thickness(0);
        more.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        more.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(more, "お気に入り・色・表示を変更");
        var container = new Grid { ColumnSpacing = 8 };
        container.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        container.Children.Add(button); Grid.SetColumn(more, 1); container.Children.Add(more);
        RegisterPopupTree(container);
        return container;
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
        var original = _model.Preferences.HiddenIds.ToHashSet(); var hidden = original.ToHashSet(); var panel = Panel(Text("再表示するリンクにチェックを付けて「保存」を押してください。"));
        foreach (var link in _model.Links?.Items.Where(i => i.Visible && hidden.Contains(i.Id)) ?? [])
        { var checkbox = new CheckBox { Content = link.Label, IsChecked = false }; checkbox.Checked += (_, _) => hidden.Remove(link.Id); checkbox.Unchecked += (_, _) => hidden.Add(link.Id); panel.Children.Add(checkbox); }
        if (panel.Children.Count == 1) { await Message("非表示のリンク", "非表示にしたリンクはありません。"); return; }
        if (await Dialog("非表示のリンク", panel, "保存", "キャンセル") == ContentDialogResult.Primary)
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
