using System.Globalization;
using System.Text;

namespace Takupoke.Core;

public static class LinkSearch
{
    private static readonly Dictionary<string, string> Kana = BuildKana();
    private static Dictionary<string, string> BuildKana()
    {
        var keys = "あ い う え お か き く け こ さ し す せ そ た ち つ て と な に ぬ ね の は ひ ふ へ ほ ま み む め も や ゆ よ ら り る れ ろ わ を ん が ぎ ぐ げ ご ざ じ ず ぜ ぞ だ ぢ づ で ど ば び ぶ べ ぼ ぱ ぴ ぷ ぺ ぽ ぁ ぃ ぅ ぇ ぉ ゔ きゃ きゅ きょ しゃ しゅ しょ ちゃ ちゅ ちょ にゃ にゅ にょ ひゃ ひゅ ひょ みゃ みゅ みょ りゃ りゅ りょ ぎゃ ぎゅ ぎょ じゃ じゅ じょ びゃ びゅ びょ ぴゃ ぴゅ ぴょ うぇ うぃ うぉ てぃ でぃ ふぁ ふぃ ふぇ ふぉ".Split(' ');
        var values = "a i u e o ka ki ku ke ko sa shi su se so ta chi tsu te to na ni nu ne no ha hi fu he ho ma mi mu me mo ya yu yo ra ri ru re ro wa o n ga gi gu ge go za ji zu ze zo da ji zu de do ba bi bu be bo pa pi pu pe po a i u e o vu kya kyu kyo sha shu sho cha chu cho nya nyu nyo hya hyu hyo mya myu myo rya ryu ryo gya gyu gyo ja ju jo bya byu byo pya pyu pyo we wi wo ti di fa fi fe fo".Split(' ');
        return keys.Zip(values).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal);
    }
    private static string Hiragana(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
        .Select(c => c is >= '\u30A1' and <= '\u30F6' ? ((char)(c - 0x60)).ToString() : c.ToString()));
    private static string CanonicalRomaji(string value) => value.Replace("shi", "si").Replace("chi", "ti").Replace("tsu", "tu").Replace("fu", "hu").Replace("ji", "zi");
    public static string Normalize(string value) => CanonicalRomaji(string.Concat(Hiragana(value).EnumerateRunes().Where(r =>
        !Rune.IsWhiteSpace(r) && Rune.GetUnicodeCategory(r) is not (UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)).Select(r => r.ToString())));
    public static string Romaji(string value)
    {
        var source = Hiragana(value);
        var result = new StringBuilder();
        var doubled = false;
        for (var index = 0; index < source.Length;)
        {
            var character = source[index];
            if (character == 'っ') { doubled = true; index++; continue; }
            if (character == 'ー')
            {
                if (result.Length > 0 && "aeiou".Contains(result[^1])) result.Append(result[^1]);
                index++; continue;
            }
            var pair = index + 1 < source.Length ? source.Substring(index, 2) : "";
            string syllable;
            if (Kana.TryGetValue(pair, out var combined)) { syllable = combined; index += 2; }
            else { syllable = Kana.GetValueOrDefault(character.ToString(), character.ToString()); index++; }
            if (doubled && syllable[0] is >= 'a' and <= 'z') result.Append(syllable[0]);
            doubled = false;
            result.Append(syllable);
        }
        return CanonicalRomaji(result.ToString());
    }
    public static int Score(string terms, string query)
    {
        query = Normalize(query);
        if (query.Length == 0) return -1;
        var expanded = new List<string>();
        foreach (var term in terms.Split('|').Where(t => t.Length > 0))
        {
            expanded.Add(Normalize(term));
            if (Hiragana(term).Any(c => c is >= '\u3041' and <= '\u3096'))
            {
                var roman = Normalize(Romaji(term));
                expanded.Add(roman);
                expanded.Add(roman.Replace("ou", "o").Replace("uu", "u").Replace("oo", "o"));
            }
        }
        var best = -1;
        for (var index = 0; index < expanded.Count; index++)
        {
            var term = expanded[index];
            if (term == query) best = Math.Max(best, 1000 - index);
            else if (term.StartsWith(query, StringComparison.Ordinal)) best = Math.Max(best, 800 - index);
            else if (term.Contains(query, StringComparison.Ordinal)) best = Math.Max(best, 500 - index);
        }
        return best;
    }
}
