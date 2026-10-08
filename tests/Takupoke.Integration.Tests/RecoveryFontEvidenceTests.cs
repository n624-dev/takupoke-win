using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryFontEvidenceTests
{
    [Theory]
    [InlineData("valid")] [InlineData("reader")] [InlineData("resource")] [InlineData("hash")]
    [InlineData("cidHash")] [InlineData("code")] [InlineData("cid")] [InlineData("gid")]
    [InlineData("scalar")] [InlineData("ocr")]
    public async Task OptionalDirectFontProofCannotConflictWithItsSource(string changed)
    {
        var document=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Timetable,[RecoveryPipelineTests.Layout(MaterialKind.Timetable)],(_,_)=>true);
        var run=await RecoveryEngine.RunAsync(document,"windows",10,true,[],_=>null);
        var result=Assert.IsType<RecoveryResult>(run.Result);Assert.True(RecoveryValidator.Validate(document,result).CanAdopt);
        var sources=document.Sources.ToArray();var index=Array.FindIndex(sources,s=>s.Text=="月");Assert.True(index>=0);
        var proof=new RecoveryFontEvidence("F1",new string('a',64),"identity",1,1,1,'月');
        proof=changed switch
        {
            "reader"=>proof with { ReaderVersion=2 },"resource"=>proof with { Resource="" },"hash"=>proof with { FontHash="bad" },
            "cidHash"=>proof with { CidMapHash="bad" },"code"=>proof with { Code=65536 },"cid"=>proof with { Cid=2 },
            "gid"=>proof with { GlyphId=0 },"scalar"=>proof with { Scalar='火' },_=>proof
        };
        sources[index]=sources[index] with { FontEvidence=proof,FromOcr=changed=="ocr" };
        var validation=RecoveryValidator.Validate(document with { Sources=sources },result);
        if(changed=="valid")Assert.True(validation.CanAdopt);
        else Assert.Contains("fontEvidence",validation.Errors);
    }
}
