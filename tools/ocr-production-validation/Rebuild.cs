using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// Post-worker parser diagnosis. No image/model/gold/user values; no empty-cell
// or capture authorization. An empty rectangle fails instead of being assumed.
if(args.Length!=2)throw new ArgumentException("closed raw and diagnostic output");
using var writer=new StreamWriter(new FileStream(args[1],FileMode.CreateNew,FileAccess.Write));
foreach(var line in File.ReadLines(args[0]))
{
    using var row=JsonDocument.Parse(line);var r=row.RootElement;
    if(!r.GetProperty("id").GetString()!.Contains("positive"))continue;
    var pages=r.GetProperty("pageReports").EnumerateArray().Select(p=>new PdfPageLayout(p.GetProperty("Width").GetInt32(),p.GetProperty("Height").GetInt32(),
        p.GetProperty("glyphs").Deserialize<PdfGlyph[]>()!,p.GetProperty("rules").Deserialize<PdfRule[]>()!)).ToArray();
    try
    {
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Timetable,pages,(_,_)=>false,
            Enumerable.Range(1,pages.Length).ToHashSet(),allowStructureProposal:true);
        var raw=RecoveryRules.RecoverAll(doc);var fields=new List<object>();
        for(var i=0;i<doc.Cells.Count;i++)
        {
            if(raw[i] is not {} cell)continue;
            for(var n=0;n<cell.Lessons.Count;n++)
            foreach(var slot in doc.Cells[i].Slots)
            foreach(var (role,field) in new[]{("subject",cell.Lessons[n].Subject),("teacher",cell.Lessons[n].Teacher),("room",cell.Lessons[n].Room)})
                fields.Add(new {slot,lessonIndex=n,role,field.Value});
        }
        writer.WriteLine(JsonSerializer.Serialize(new {id=r.GetProperty("id").GetString(),requiredSlots=doc.RequiredSlots.Count,fields,
            scope="Zero-inference parser diagnosis with no empty/crop/user authorization; not final qualification"}));
        Console.WriteLine($"{r.GetProperty("id").GetString()}: {doc.RequiredSlots.Count} slots, {fields.Count} fields");
    }
    catch(Exception e)when(e is InvalidDataException or PdfParseException){Console.WriteLine(e.Message);}
}
