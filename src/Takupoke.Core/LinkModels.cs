using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Core;

public enum LinkOpeningMode { InApp, External }
public sealed record LinkItem(string Id, string CategoryId, string Label, string Href, string Color,
    bool Visible, int SortOrder, bool Recommended, int RecommendationOrder, IReadOnlyList<string> SearchAliases, string SearchTerms);
public sealed record LinkCategory(string Id, string Label, int SortOrder, IReadOnlyList<LinkItem> Buttons);
public sealed record LinksPayload(string Version, string LinksVersion, IReadOnlyList<LinkCategory> Categories)
{
    public static IReadOnlySet<string> Colors { get; } = new HashSet<string>
    { "sky", "blue", "emerald", "green", "amber", "yellow", "orange", "rose", "red", "indigo", "purple", "pink", "teal", "slate", "gray" };
    public IEnumerable<LinkItem> Items => Categories.SelectMany(c => c.Buttons);
    public LinksPayload Validated()
    {
        bool Id(string value) => Regex.IsMatch(value, "^[A-Za-z0-9_-]{1,100}$");
        bool Text(string value, int length) => value.Length is > 0 && value.Length <= length && value.Trim() == value;
        if (Version != "v1" || !Regex.IsMatch(LinksVersion, "^sha256-[a-f0-9]{64}$") || Categories.Count is < 1 or > 100
            || Categories.Select(c => c.Id).Distinct().Count() != Categories.Count || Items.Count() > 800 || Items.Select(i => i.Id).Distinct().Count() != Items.Count())
            throw new InvalidDataException("一覧のデータを確認できませんでした。");
        foreach (var category in Categories)
        {
            if (!Id(category.Id) || !Text(category.Label, 80)) throw new InvalidDataException("一覧のカテゴリを確認できませんでした。");
            foreach (var item in category.Buttons)
                if (!Id(item.Id) || item.CategoryId != category.Id || !Text(item.Label, 80) || !ValidUrl(item.Href) || !Colors.Contains(item.Color)
                    || item.SearchAliases.Count > 20 || item.SearchAliases.Any(a => !Text(a, 80)) || item.SearchTerms.Length is < 1 or > 5000)
                    throw new InvalidDataException("一覧のリンクを確認できませんでした。");
        }
        return this;
    }
    public static bool ValidUrl(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > 2048 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == "jrshikoku" || uri.Scheme == "https" && value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }
    public IEnumerable<LinkItem> Recommendations(IReadOnlySet<string> hidden) => Items.Where(i => i.Visible && i.Recommended && !hidden.Contains(i.Id))
        .OrderBy(i => i.RecommendationOrder).ThenBy(i => i.SortOrder).ThenBy(i => i.Label, StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), false));
    public IEnumerable<LinkItem> Search(string query, IReadOnlySet<string> hidden) => Items.Where(i => i.Visible && !hidden.Contains(i.Id))
        .Select(i => (Item: i, Score: LinkSearch.Score(i.SearchTerms, query))).Where(pair => pair.Score >= 0)
        .OrderByDescending(pair => pair.Score).Select(pair => pair.Item);
}

public sealed record UserPreferences
{
    public string[] SelectedClasses { get; init; } = [];
    public string[] ChangeClasses { get; init; } = [];
    public bool International { get; init; }
    public bool IncludesChanges { get; init; } = true;
    public ChangeRange ChangeRange { get; init; } = ChangeRange.Today;
    public string? DefaultSchoolYear { get; init; }
    public string[] TimetableAnalysisClasses { get; init; } = [];
    public string[] ChangeAnalysisClasses { get; init; } = [];
    public int TimetableAnalysisWeekday { get; init; }
    public HashSet<string> FavoriteIds { get; init; } = [];
    public HashSet<string> HiddenIds { get; init; } = [];
    public Dictionary<string, string> LinkColors { get; init; } = [];
    public LinkOpeningMode OpeningMode { get; init; } = LinkOpeningMode.InApp;
    public string MainColor { get; init; } = "default";
    public bool SetupCompleted { get; init; }
    public bool NotificationsSetupCompleted { get; init; }
    public bool NotifyChanges { get; init; }
    public bool NotifySpecials { get; init; }
    public bool KeepInTray { get; init; }
    public bool AutoStart { get; init; }
    public static IReadOnlyList<string> MainColors { get; } = ["default", "blue", "green", "yellow", "orange", "red", "pink", "purple"];
    public static string MainColorLabel(string color) => color switch
    { "default" => "デフォルト", "blue" => "青", "green" => "緑", "yellow" => "黄色", "orange" => "オレンジ", "red" => "赤", "pink" => "ピンク", "purple" => "紫", _ => "デフォルト" };
    public UserPreferences Validated()
    {
        if (TimetableAnalysisWeekday is < 0 or > 5 || SelectedClasses.Length > 2 || ChangeClasses.Length > 30 || !MainColors.Contains(MainColor)
            || LinkColors.Values.Any(c => !LinksPayload.Colors.Contains(c))) throw new InvalidDataException("個人設定の形式を確認できません。");
        return this;
    }
}
