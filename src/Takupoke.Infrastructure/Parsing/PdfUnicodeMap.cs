using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Infrastructure.Parsing;

public sealed record PdfUnicodeMap(int CodeBytes, IReadOnlyDictionary<int, string> Values)
{
    public static PdfUnicodeMap Read(byte[] data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (data.Length > 2_000_000 || data.Any(b => b > 127)) throw new PdfParseException("P01");
        var source = Regex.Replace(Encoding.ASCII.GetString(data), "%[^\\r\\n]*", "");
        var tokens = Regex.Matches(source, "<[^<>]*>|\\[|\\]|[^\\s<>\\[\\]]+").Select(m => m.Value).ToArray();
        if (tokens.Contains("usecmap") || tokens.Contains("beginnotdefrange") || tokens.Contains("beginnotdefchar")) throw new PdfParseException("P01");
        var coveredCodes = new bool[65536]; int? codeBytes = null; var values = new Dictionary<int, string>(); var cursor = 0; var work = 0;
        string Next() => cursor < tokens.Length ? tokens[cursor++] : throw new PdfParseException("P01");
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
            if (++work % 128 == 0) cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length % 2 != 0 || values.Count >= 65536 || !coveredCodes[code]) throw new PdfParseException("P01");
            string text;
            try { text = new UnicodeEncoding(true, false, true).GetString(bytes); } catch { throw new PdfParseException("P01"); }
            if (text.Length == 0 || !values.TryAdd(code, text)) throw new PdfParseException("P01");
        }
        while (cursor < tokens.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = Next();
            if (token == "/WMode" && Next() != "0") throw new PdfParseException("P01");
            if (token is not "begincodespacerange" and not "beginbfchar" and not "beginbfrange") continue;
            if (cursor < 2 || !int.TryParse(tokens[cursor - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 65536) throw new PdfParseException("P01");
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (token == "begincodespacerange")
                {
                    var low = Hex(Next()); var high = Hex(Next());
                    if (low.Length is not 1 and not 2 || low.Length != high.Length || codeBytes is not null && codeBytes != low.Length) throw new PdfParseException("P01");
                    codeBytes = low.Length; var a = Number(low); var b = Number(high);
                    if (a > b) throw new PdfParseException("P01");
                    for (var code = a; code <= b; code++)
                    {
                        if (++work % 128 == 0) cancellationToken.ThrowIfCancellationRequested();
                        if (coveredCodes[code]) throw new PdfParseException("P01"); coveredCodes[code] = true;
                    }
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
        return new(codeBytes.Value, values);
    }
}
