using System.Text.Json;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;
public class RecoveryOutputDecoderTests
{
    private const string Field = "{\"state\":\"present\",\"value\":\"架空値\",\"evidence\":[\"synthetic\"]}";
    private static string Cell(string field) => "{\"lessons\":[{\"subject\":" + field + ",\"teacher\":" + Field + ",\"room\":" + Field + "}]}";
    [Fact] public void CompleteStructureDecodesForLaterValidation() => Assert.Single(RecoveryOutputDecoder.Decode(Cell(Field)));
    [Theory]
    [InlineData("{\"value\":\"架空値\",\"evidence\":[\"synthetic\"]}")]
    [InlineData("{\"state\":0,\"value\":\"架空値\",\"evidence\":[\"synthetic\"]}")]
    [InlineData("{\"state\":\"present\",\"value\":\"架空値\",\"evidence\":null}")]
    [InlineData("{\"state\":\"present\",\"value\":\"架空値\",\"evidence\":[\"synthetic\"],\"extra\":1}")]
    public void MissingNullNumericAndUnknownFieldsReject(string field) => Assert.ThrowsAny<Exception>(() => RecoveryOutputDecoder.Decode(Cell(field)));
    [Fact] public void DuplicateFieldsReject() => Assert.ThrowsAny<Exception>(() => RecoveryOutputDecoder.Decode(Cell(Field.Replace("\"state\":\"present\"", "\"state\":\"present\",\"state\":\"empty\""))));
}
