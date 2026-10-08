using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// Isolated recognition-only measurement: ordinal images, no literal answers,
// class list, table coordinates, dictionary suggestions or adoption capability.
if(args.Length != 2)throw new ArgumentException("Expected pinned model and pixel input list");
var model=File.ReadAllBytes(args[0]);
if(model.Length!=8967018 || Convert.ToHexStringLower(SHA256.HashData(model))!="ef7abd8bd3629ae57ea2c28b425c1bd258a871b93fd2fe7c433946ade9b5d9ea")
    throw new InvalidDataException("Pinned English recognition weights differ");
OrtEnv.Instance().DisableTelemetryEvents();
using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL};
using var reader=new InferenceSession(model,options);
CryptographicOperations.ZeroMemory(model);
// RapidOCR f65c7da00e72c19c258245e8e0e5f33af14488be CTCLabelDecode
// appends one space, then prepends CTC blank. The model-native metadata
// already contains a space; preserve both distinct indices without deduping.
var dictionary=new[]{""}.Concat(reader.ModelMetadata.CustomMetadataMap["character"].TrimEnd('\n').Split('\n')).Append(" ").ToArray();
if(dictionary.Length!=97)throw new InvalidDataException("Model-native alphabet does not match output shape");
var inputs=JsonSerializer.Deserialize<PixelInput[]>(File.ReadAllBytes(args[1])) ?? throw new InvalidDataException("Missing pixels");
if(inputs.Length!=64)throw new InvalidDataException("Expected exactly the blind fixed64-input probe");
var output=new List<object>();
foreach(var sample in inputs)
{
    var bgra=Convert.FromBase64String(sample.Bgra);
    try
    {
        var raster=new RecoveryRaster(sample.Width,sample.Height,bgra);
        var input=OcrInputTransform.Recognition(raster,new RecoveryBox(0,0,sample.Width,sample.Height));
        using var result=reader.Run([NamedOnnxValue.CreateFromTensor("x",input.Tensor)]);
        var logits=result.First().AsTensor<float>();
        if(logits.Rank!=3 || logits.Dimensions[0]!=1 || logits.Dimensions[2]!=97)throw new InvalidDataException("Unexpected native output shape");
        foreach(var score in logits)if(!float.IsFinite(score) || score is <0 or >1)throw new InvalidDataException("Invalid native probability");
        var pieces=new List<(string Text,int Start,int End,float Score)>();var previous=-1;
        for(var t=0;t<logits.Dimensions[1];t++)
        {
            var best=0;var score=logits[0,t,0];
            for(var c=1;c<dictionary.Length;c++)if(logits[0,t,c]>score){best=c;score=logits[0,t,c];}
            if(best!=0 && best!=previous)pieces.Add((dictionary[best],t,t+1,score));
            else if(best!=0 && pieces.Count>0){var p=pieces[^1];pieces[^1]=(p.Text,p.Start,t+1,Math.Max(p.Score,score));}
            previous=best;
        }
        output.Add(new{sample.Index,text=string.Concat(pieces.Select(p=>p.Text)),minimumConfidence=pieces.Count==0 ? (float?)null:pieces.Min(p=>p.Score),
            input.ValidWidth,input.InputWidth,timeCount=logits.Dimensions[1],
            paddingSupported=pieces.All(p=>(long)p.End*input.InputWidth<=(long)input.ValidWidth*logits.Dimensions[1]),
            pieces=pieces.Select(p=>new{p.Text,p.Start,p.End,confidence=p.Score})});
    }
    finally{CryptographicOperations.ZeroMemory(bgra);}
}
Console.WriteLine(JsonSerializer.Serialize(new{runtime="Microsoft.ML.OnnxRuntime1.23.2 CPU",calls=inputs.Length,qualified=false,outputs=output}));
sealed record PixelInput(int Index,int Width,int Height,string Bgra);
