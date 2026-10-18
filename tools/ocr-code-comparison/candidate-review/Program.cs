using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;

// Closed fictional OCR strings and a reference roster only; no gold or models.
if (args.Length != 2) throw new ArgumentException("input new-output");
using var input = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var rooms = input.RootElement.GetProperty("rooms").EnumerateArray()
    .Select(code => new MappingRule(code.GetString()!, code.GetString()!)).ToArray();
using var output = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var row in input.RootElement.GetProperty("rows").EnumerateArray())
{
    var value = row.GetProperty("text").GetString()!;
    output.WriteLine(JsonSerializer.Serialize(new { id = row.GetProperty("id").GetString(), text = value,
        before = PreviousRecoveryCodeCandidates.Find(value, rooms, ["1_1"]),
        after = RecoveryCodeCandidates.Find(value, rooms, ["1_1"]) }));
}
