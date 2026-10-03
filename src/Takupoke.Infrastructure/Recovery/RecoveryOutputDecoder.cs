using System.Text.Json;
using System.Text.Json.Serialization;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// Provider outputs have stricter rules than backward compatible application storage.
public static class RecoveryOutputDecoder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    private sealed record GeneratedLesson(RecoveryField Subject, RecoveryField Teacher, RecoveryField Room);
    private sealed record GeneratedCell(IReadOnlyList<GeneratedLesson> Lessons);
    public static IReadOnlyList<RecoveryLesson> Decode(string text)
    {
        try { return DecodeChecked(text); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or NullReferenceException) { throw new InvalidRecoveryOutputException(error); }
    }
    private static IReadOnlyList<RecoveryLesson> DecodeChecked(string text)
    {
        if (text.Length > 16384) throw new InvalidDataException("復旧出力の上限を超えています。");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        DataCodec.RejectDuplicates(document.RootElement);
        var cell = document.RootElement.Deserialize<GeneratedCell>(Options) ?? throw new InvalidDataException("復旧出力を確認できません。");
        if (cell.Lessons.Count is < 1 or > 4 || cell.Lessons.Any(l => l is null || new[] { l.Subject, l.Teacher, l.Room }.Any(f => f is null || f.Value is null || f.Evidence is null || f.Value.Length > 1024 || f.Evidence.Count > 1024 || f.Evidence.Any(string.IsNullOrWhiteSpace))))
            throw new InvalidDataException("復旧項目を確認できません。");
        return cell.Lessons.Select(l => new RecoveryLesson(l.Subject, l.Teacher, l.Room, [], [])).ToArray();
    }
}
