using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Infrastructure.Parsing;

public sealed record PdfUnicodeMap(int CodeBytes, IReadOnlyDictionary<int, string> Values)
{
    public static PdfUnicodeMap Read(byte[] data, CancellationToken cancellationToken = default)
        => ReadWithCheckpoint(data, cancellationToken.ThrowIfCancellationRequested);

    internal static PdfUnicodeMap ReadWithCheckpoint(byte[] data, Action checkpoint)
    {
        checkpoint();
        var work = 0;
        void Consume() { if (++work % 128 == 0) checkpoint(); }
        if (data.Length > 2_000_000 || data.Any(b => b > 127)) throw new PdfParseException("P01");
        var source = Regex.Replace(Encoding.ASCII.GetString(data), "%[^\\r\\n]*", "");
        var tokens = new List<string>();
        foreach (Match match in Regex.Matches(source, "<[^<>]*>|\\[|\\]|[^\\s<>\\[\\]]+"))
        { Consume(); tokens.Add(match.Value); }
        if (tokens.Contains("usecmap") || tokens.Contains("beginnotdefrange") || tokens.Contains("beginnotdefchar")) throw new PdfParseException("P01");
        var covered = new bool[65536]; int? codeBytes = null; var values = new Dictionary<int, string>(); var cursor = 0;
        string Next() { Consume(); return cursor < tokens.Count ? tokens[cursor++] : throw new PdfParseException("P01"); }
        bool RangeAvailable(int low, int high)
        {
            if (low > high) return false;
            for (var code = low; code <= high; code++) { Consume(); if (covered[code]) return false; }
            return true;
        }
        static byte[] Hex(string token)
        {
            if (token.Length < 4 || token[0] != '<' || token[^1] != '>') throw new PdfParseException("P01");
            var body = Regex.Replace(token[1..^1], @"\s", "");
            if (body.Length is < 2 or > 128 || body.Length % 2 != 0 || !body.All(Uri.IsHexDigit)) throw new PdfParseException("P01");
            return Convert.FromHexString(body);
        }
        static int Number(byte[] bytes) => bytes.Aggregate(0, (value, b) => value * 256 + b);
        int Code(string token) { var bytes = Hex(token); if (bytes.Length != codeBytes) throw new PdfParseException("P01"); return Number(bytes); }
        void Insert(int code, byte[] bytes)
        {
            Consume();
            if (bytes.Length % 2 != 0 || values.Count >= 65536 || code is < 0 or >= 65536 || !covered[code]) throw new PdfParseException("P01");
            string text;
            try { text = new UnicodeEncoding(true, false, true).GetString(bytes); } catch { throw new PdfParseException("P01"); }
            if (text.Length == 0 || text.Any(char.IsControl) || !values.TryAdd(code, text)) throw new PdfParseException("P01");
        }
        while (cursor < tokens.Count)
        {
            checkpoint();
            var token = Next();
            if (token == "/WMode" && Next() != "0") throw new PdfParseException("P01");
            if (token is not "begincodespacerange" and not "beginbfchar" and not "beginbfrange") continue;
            if (cursor < 2 || !int.TryParse(tokens[cursor - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 65536) throw new PdfParseException("P01");
            for (var index = 0; index < count; index++)
            {
                checkpoint();
                if (token == "begincodespacerange")
                {
                    var low = Hex(Next()); var high = Hex(Next());
                    if (low.Length is not 1 and not 2 || low.Length != high.Length || codeBytes is not null && codeBytes != low.Length) throw new PdfParseException("P01");
                    codeBytes = low.Length; var a = Number(low); var b = Number(high);
                    if (!RangeAvailable(a, b)) throw new PdfParseException("P01");
                    for (var code = a; code <= b; code++) { Consume(); covered[code] = true; }
                }
                else if (token == "beginbfchar") { var code = Code(Next()); Insert(code, Hex(Next())); }
                else
                {
                    var low = Code(Next()); var high = Code(Next());
                    if (high < low || high - low >= 65536) throw new PdfParseException("P01");
                    var destination = Next();
                    if (destination == "[")
                    {
                        for (var code = low; code <= high; code++) Insert(code, Hex(Next()));
                        if (Next() != "]") throw new PdfParseException("P01");
                    }
                    else
                    {
                        var bytes = Hex(destination);
                        for (var code = low; code <= high; code++)
                        {
                            Insert(code, bytes);
                            if (code == high) continue;
                            var carry = true;
                            for (var digit = bytes.Length - 1; digit >= 0 && carry; digit--)
                                if (bytes[digit] == 255) bytes[digit] = 0; else { bytes[digit]++; carry = false; }
                            if (carry) throw new PdfParseException("P01");
                        }
                    }
                }
            }
            if (Next() != token.Replace("begin", "end", StringComparison.Ordinal)) throw new PdfParseException("P01");
        }
        if (codeBytes is null || values.Count == 0) throw new PdfParseException("P01");
        checkpoint();
        return new(codeBytes.Value, values);
    }
}
