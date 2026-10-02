using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Api;

public static class ApiPayloads
{
    public static bool ValidRevision(string value) => Regex.IsMatch(value, "^[A-Za-z0-9_-]{43}$");
    public static bool HexDigest(string value) => Regex.IsMatch(value, "^[a-f0-9]{64}$");
    public static bool ValidETag(string value)
    {
        var tag = value.StartsWith("W/", StringComparison.Ordinal) ? value[2..] : value;
        return tag.Length is >= 3 and <= 256 && tag[0] == '"' && tag[^1] == '"' && tag[1..^1].All(c => c is >= ' ' and <= '~' && c != '"');
    }
    public static bool SourceETag(string value) => value.Length is >= 3 and <= 256 && value[0] == '"' && value[^1] == '"' && !value[1..^1].Any(c => c is '"' or '\r' or '\n');
    public static string OpaqueETag(string tag) => tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
    public static JsonDocument Json(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length == 0 || bytes.Length > maximumBytes) throw new ApiException(ApiFailure.InvalidResponse);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        try { DataCodec.RejectDuplicates(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    public static void ExactKeys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(keys))
            throw new ApiException(ApiFailure.InvalidResponse);
    }
    private static T Decode<T>(JsonElement root) => root.Deserialize<T>(DataCodec.Options) ?? throw new ApiException(ApiFailure.InvalidResponse);
    public static LinksPayload Links(byte[] bytes)
    {
        using var document = Json(bytes, 3_000_000);
        // Swift Codable requires every nonoptional field, including false booleans and zero sort orders.
        RequiredKeys(document.RootElement, "version", "linksVersion", "categories");
        foreach (var category in document.RootElement.GetProperty("categories").EnumerateArray())
        {
            RequiredKeys(category, "id", "label", "sortOrder", "buttons");
            foreach (var item in category.GetProperty("buttons").EnumerateArray())
                RequiredKeys(item, "id", "categoryId", "label", "href", "color", "visible", "sortOrder", "recommended", "recommendationOrder", "searchAliases", "searchTerms");
        }
        return Decode<LinksPayload>(document.RootElement).Validated();
    }
    private static void RequiredKeys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object || keys.Any(key => !value.TryGetProperty(key, out var property) || property.ValueKind == JsonValueKind.Null))
            throw new ApiException(ApiFailure.InvalidResponse);
    }
    public static ScheduleTimes Times(byte[] bytes)
    {
        using var document = Json(bytes, 128 * 1024);
        var root = document.RootElement; ExactKeys(root, "schemaVersion", "days");
        foreach (var day in root.GetProperty("days").EnumerateArray())
        {
            ExactKeys(day, "date", "periods");
            foreach (var period in day.GetProperty("periods").EnumerateArray()) ExactKeys(period, "period", "start", "end");
        }
        return Decode<ScheduleTimes>(root).Validated();
    }
    public static EventsPayload Events(byte[] bytes, int schoolYear)
    { using var document = Json(bytes, 1_000_000); return Decode<EventsPayload>(document.RootElement).Validated(schoolYear); }
    public static SavedMapping Mapping(byte[] bytes, string version, string revision, string etag, DateTimeOffset at, CancellationToken token = default)
    {
        if (!Regex.IsMatch(version, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$") || !ValidRevision(revision) || !ValidETag(etag)) throw new ApiException(ApiFailure.InvalidResponse);
        using var zip = new BoundedZip(bytes, 8 * 1024 * 1024, 2, 4 * 1024 * 1024, 8 * 1024 * 1024);
        if (!zip.Names.ToHashSet().SetEquals(["manifest.json", "mappings.json"])) throw new ApiException(ApiFailure.InvalidResponse);
        var mappingBytes = zip.Read("mappings.json", 4 * 1024 * 1024, token);
        using var manifest = Json(zip.Read("manifest.json", 4 * 1024 * 1024, token), 4 * 1024 * 1024);
        var root = manifest.RootElement; ExactKeys(root, "schemaVersion", "version", "publishedAt", "mappings");
        var digest = root.GetProperty("mappings"); ExactKeys(digest, "sha256", "bytes");
        var schema = root.GetProperty("schemaVersion").GetInt32();
        var published = root.GetProperty("publishedAt").GetString()!;
        if (schema is not 1 and not 2 || root.GetProperty("version").GetString() != version || digest.GetProperty("bytes").GetInt32() != mappingBytes.Length
            || digest.GetProperty("sha256").GetString() != NotificationDiff.Digest(mappingBytes)
            || !DateTimeOffset.TryParseExact(published, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            throw new ApiException(ApiFailure.InvalidResponse);
        using var mapping = Json(mappingBytes, 4 * 1024 * 1024);
        var data = mapping.RootElement;
        ExactKeys(data, schema == 1 ? ["subjects", "teachers", "rooms"] : ["subjects", "teachers", "rooms", "teacherContexts"]);
        foreach (var name in new[] { "subjects", "teachers", "rooms" })
            foreach (var row in data.GetProperty(name).EnumerateArray())
            {
                var allowed = new HashSet<string> { "alias", "fullName", "classes", "internationalStudent" };
                if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Any(p => !allowed.Contains(p.Name)
                    || p.Name is "classes" or "internationalStudent" && p.Value.ValueKind == JsonValueKind.Null)) throw new ApiException(ApiFailure.InvalidResponse);
            }
        if (schema == 2)
            foreach (var row in data.GetProperty("teacherContexts").EnumerateArray()) ExactKeys(row, "alias", "fullName", "subject", "className", "schoolYear");
        var rules = Decode<MappingRules>(data);
        ValidateRules(rules, schema);
        return new(revision, version, schema, etag, NotificationDiff.Digest(bytes), published, at, rules);
    }
    private static void ValidateRules(MappingRules rules, int schema)
    {
        bool Text(string value) => value.Length is > 0 and <= 512 && value == value.Trim();
        var contexts = rules.TeacherContexts ?? [];
        if (rules.Subjects.Count + rules.Teachers.Count + rules.Rooms.Count + contexts.Count > 10000 || schema == 1 && contexts.Count > 0) throw new ApiException(ApiFailure.InvalidResponse);
        foreach (var group in new[] { (Subject: true, Rules: rules.Subjects), (Subject: false, Rules: rules.Teachers), (Subject: false, Rules: rules.Rooms) })
        {
            var seen = new HashSet<(string, string)>();
            foreach (var rule in group.Rules)
            {
                if (!Text(rule.Alias) || !Text(rule.FullName) || rule.InternationalStudent == false
                    || !group.Subject && rule.Classes is not null || rule.InternationalStudent == true && !rule.Alias.StartsWith("留 ", StringComparison.Ordinal)
                    || rule.Classes is { } classes && (classes.Count is < 1 or > 100 || classes.Any(c => c.Length is < 1 or > 128))) throw new ApiException(ApiFailure.InvalidResponse);
                foreach (var cls in rule.Classes ?? [""]) if (!seen.Add((rule.Alias, cls))) throw new ApiException(ApiFailure.InvalidResponse);
            }
        }
        var seenContexts = new HashSet<(string, int, string, string)>();
        foreach (var rule in contexts)
            if (!Text(rule.Alias) || !Text(rule.FullName) || !Text(rule.Subject) || rule.ClassName != rule.ClassName.Trim()
                || rule.SchoolYear is < 2000 or > 2099 || !ClassSelection.Candidates.Contains(rule.ClassName)
                || !seenContexts.Add((rule.Alias, rule.SchoolYear, rule.ClassName, MappingRules.Comparable(rule.Subject)))) throw new ApiException(ApiFailure.InvalidResponse);
    }
}
