using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryOcrDotFailureClassificationTests
{
    [Fact]
    public void OriginalOcrDotRefusalHasBoundedSourceContractClassification()
    {
        var source = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        var page = source with { Glyphs = source.Glyphs.Select(g => g.Text == "架空科目A" ? g with { Text = "架空甲·架空乙" } : g).ToArray() };
        var before = JsonSerializer.SerializeToUtf8Bytes(page);
        var error = Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a',64), MaterialKind.Timetable, [page],
            (_,box) => !page.Glyphs.Any(g => box.Contains(new(g.X,g.Y,g.Width,g.Height))), ocrPages:new HashSet<int>{1}, allowStructureProposal:true));
        Assert.Equal("sourceContractRefusal",error.Data["RecoveryErrorKind"]);
        Assert.Equal("ocrSeparatorAmbiguity",error.Data["RecoveryErrorCode"]);
        Assert.False(RecoveryWorkLimits.IsExceeded(error));
        Assert.Equal(before,JsonSerializer.SerializeToUtf8Bytes(page));
    }
}
