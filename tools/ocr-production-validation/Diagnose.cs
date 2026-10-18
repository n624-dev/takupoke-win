using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

if(args.Length!=3)throw new ArgumentException("native BGRA, model directory, output");
var image=new RecoveryRaster(2400,445,File.ReadAllBytes(args[0]));
using var ocr=new OnnxJapaneseOcr(Path.Combine(args[1],"det.onnx"),Path.Combine(args[1],"rec.onnx"),
    Path.Combine(AppContext.BaseDirectory,"../../../../../src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json"),18710);
IReadOnlyList<RecoveryBox> boxes=[];ocr.DetectionObserver=b=>boxes=b.ToArray();
try{ocr.ReadForManualCapture(image);}
catch(InvalidDataException e)
{
    var rules=image.Rules();
    File.WriteAllText(args[2],JsonSerializer.Serialize(new { boxes,rules,maskCount=image.RuleMask(rules).Count(b=>b),
        failure=e.Message,details=e.Data.Cast<System.Collections.DictionaryEntry>().ToDictionary(d=>d.Key.ToString()!,d=>d.Value) }));
    Console.WriteLine(JsonSerializer.Serialize(new { failure=e.Message,details=e.Data.Cast<System.Collections.DictionaryEntry>().ToDictionary(d=>d.Key.ToString()!,d=>d.Value) }));
}
