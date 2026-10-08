using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// Isolated recognition-only measurement: ordinal images, no literal answers,
// class list, table coordinates, dictionary suggestions or adoption capability.
if(args.Length==2 && args[0]=="--audit-inputs-v1")
{
    // Pixel-only audit of the real app transform. No weights, ORT session,
    // expected glyph bounds, reader output, confidence or adoption call.
    var samples=JsonSerializer.Deserialize<PixelInput[]>(File.ReadAllBytes(args[1]))
        ?? throw new InvalidDataException("Missing audit pixels");
    if(samples.Length!=64 || samples.Where((p,i)=>p.Index!=i).Any())
        throw new InvalidDataException("Expected the fixed 64 ordinal audit inputs");
    var rows=new List<object>();
    foreach(var sample in samples)
    {
        var bgra=Convert.FromBase64String(sample.Bgra);
        try
        {
            if(sample.Width is <1 or >2048 || sample.Height is <1 or >2048 || bgra.Length!=checked(sample.Width*sample.Height*4))
                throw new InvalidDataException("Invalid audit pixel dimensions");
            var input=OcrInputTransform.Recognition(new RecoveryRaster(sample.Width,sample.Height,bgra),
                new RecoveryBox(0,0,sample.Width,sample.Height));
            var pixels=new byte[checked(input.ValidWidth*48*3)];
            for(var y=0;y<48;y++)for(var x=0;x<input.ValidWidth;x++)for(var c=0;c<3;c++)
                pixels[(y*input.ValidWidth+x)*3+c]=checked((byte)Math.Round(
                    (input.Tensor[0,c,y,x]+1d)*127.5,MidpointRounding.AwayFromZero));
            rows.Add(new{sample.Index,sourceWidth=sample.Width,sourceHeight=sample.Height,input.ValidWidth,input.InputWidth,
                sourceBgraSHA256=Convert.ToHexStringLower(SHA256.HashData(bgra)),
                tensorFloat32SHA256=Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(input.Tensor.Buffer.Span))),
                validBgr=Convert.ToBase64String(pixels),height=48,channelOrder="BGR",paddingCompared=false});
        }
        finally{CryptographicOperations.ZeroMemory(bgra);}
    }
    Console.WriteLine(JsonSerializer.Serialize(new{recipe="actual-app-input-tensor-v1",calls=0,qualified=false,outputs=rows}));
    return;
}
if(args.Length is not (2 or 3 or 4))throw new ArgumentException("Expected pinned model, pixel input list, optional pinned native alphabet and explicit diagnostic mode");
var physical=args.Length==4 && args[3]=="physical-class-crops-v1";
var digitLetter=args.Length==4 && args[3]=="blind-digit-letter-v1";
if(args.Length==4 && !physical && !digitLetter)throw new ArgumentException("Unknown diagnostic mode");
var model=File.ReadAllBytes(args[0]);
var hash=Convert.ToHexStringLower(SHA256.HashData(model));
var v5=model.Length==7848423 && hash=="b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f";
var v3=model.Length==8967018 && hash=="ef7abd8bd3629ae57ea2c28b425c1bd258a871b93fd2fe7c433946ade9b5d9ea";
if(!v3 && !v5 || v5 && args.Length is not (3 or 4) || v3 && args.Length!=2)
    throw new InvalidDataException("Pinned English recognition weights differ");
OrtEnv.Instance().DisableTelemetryEvents();
using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL};
using var reader=new InferenceSession(model,options);
CryptographicOperations.ZeroMemory(model);
// RapidOCR f65c7da00e72c19c258245e8e0e5f33af14488be CTCLabelDecode
// appends one space, then prepends CTC blank. The model-native metadata
// already contains a space; preserve both distinct indices without deduping.
string[] dictionary;
if(v5)
{
    var alphabet=File.ReadAllBytes(args[2]);
    // Official inference.yml character_dict in its original order, preceded
    // by CTC blank and followed by the native space. No case folding/deduping.
    if(Convert.ToHexStringLower(SHA256.HashData(alphabet))!="3a3e5abea6e7d403209043c8d67b052414ae59ece49922cc7e3ac205d59060a8")
        throw new InvalidDataException("Pinned official alphabet differs");
    dictionary=JsonSerializer.Deserialize<string[]>(alphabet) ?? throw new InvalidDataException("Missing alphabet");
}
else dictionary=new[]{""}.Concat(reader.ModelMetadata.CustomMetadataMap["character"].TrimEnd('\n').Split('\n')).Append(" ").ToArray();
if(dictionary.Length!=(v5 ? 438:97))throw new InvalidDataException("Model-native alphabet does not match output shape");
var inputs=JsonSerializer.Deserialize<PixelInput[]>(File.ReadAllBytes(args[1])) ?? throw new InvalidDataException("Missing pixels");
if(inputs.Length!=(physical ? 18:digitLetter ? 16:64) || inputs.Select(p=>p.Index).Where((index,order)=>index!=order).Any())
    throw new InvalidDataException("Expected the complete fixed ordinal input inventory");
var output=new List<object>();
foreach(var sample in inputs)
{
    var bgra=Convert.FromBase64String(sample.Bgra);
    try
    {
        if(sample.Width is <1 or >2048 || sample.Height is <1 or >2048 || bgra.Length!=checked(sample.Width*sample.Height*4))
            throw new InvalidDataException("Invalid original pixel dimensions");
        var raster=new RecoveryRaster(sample.Width,sample.Height,bgra);
        var input=OcrInputTransform.Recognition(raster,new RecoveryBox(0,0,sample.Width,sample.Height));
        using var result=reader.Run([NamedOnnxValue.CreateFromTensor("x",input.Tensor)]);
        var logits=result.First().AsTensor<float>();
        if(logits.Rank!=3 || logits.Dimensions[0]!=1 || logits.Dimensions[2]!=dictionary.Length)throw new InvalidDataException("Unexpected native output shape");
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
Console.WriteLine(JsonSerializer.Serialize(new{runtime="Microsoft.ML.OnnxRuntime1.23.2 CPU",modelId=v5 ? "en_PP-OCRv5_mobile_rec":"en_PP-OCRv3_rec",calls=inputs.Length,qualified=false,outputs=output}));
sealed record PixelInput(int Index,int Width,int Height,string Bgra);
