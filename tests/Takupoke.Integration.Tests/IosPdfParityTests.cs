using System.Text.Json;
using System.Text.Json.Nodes;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class IosPdfParityTests
{
    [Fact]
    public void IdenticalDrawingGeometryMatchesTheOriginalSwiftParser()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var reference = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ios-parity.json")))!;
        foreach (var item in reference["pdf"]!.AsArray())
        {
            var pages = item!["pages"]!.Deserialize<PdfPageLayout[]>(options)!;
            JsonNode? actual;
            try
            {
                var analysis = PdfScheduleParser.Timetable(pages);
                actual = JsonSerializer.SerializeToNode(new { schoolYear = analysis.SchoolYear, term = analysis.Term, lessons = analysis.Lessons.Select(l => new { l.ClassName, l.Weekday, l.Period, names = new { l.Names.Subject, l.Names.Teacher, l.Names.Room }, l.SourceText, l.Page }) }, options);
            }
            catch (PdfParseException error) { actual = JsonSerializer.SerializeToNode(new { error = error.Stage }, options); }
            Assert.True(JsonNode.DeepEquals(item["expected"], actual), $"{item["id"]}: Swift {item["expected"]}, C# {actual}");
        }
    }
}
