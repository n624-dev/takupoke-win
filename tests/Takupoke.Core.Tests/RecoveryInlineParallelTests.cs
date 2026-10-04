using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Core.Tests;

public partial class RecoveryTests
{
    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲・室乙")]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲")]
    [InlineData("科甲", "教甲・教乙", "室甲・室乙")]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲・室乙")]
    [InlineData("科甲･科乙", "教甲", "室甲･室乙")]
    public void StoredInlineTuplesCannotBeReusedOrRevalidatedAsOneCompoundLesson(string subject, string teacher, string room)
    {
        var (doc, result) = InlineValues(subject, teacher, room);
        Assert.Contains("inlineParallelEvidence", RecoveryValidator.Validate(doc, result).Errors);
        Assert.Contains("inlineParallelEvidence", RecoveryValidator.InputErrors(doc));
        Assert.False(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
        var oldResult = result with { Metadata = result.Metadata with { ValidatorVersion = 4 } };
        Assert.Contains("versions", RecoveryValidator.Validate(doc, oldResult).Errors);
        Assert.False(RecoveryValidator.CanReuse(Accept(doc, oldResult), doc, oldResult));
    }
    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲")]
    [InlineData("科甲", "教甲・教乙", "室甲")]
    [InlineData("科甲", "教甲", "室甲・室乙")]
    public void LegitimateOldInlineAuditNeedsCurrentRevalidationAndPreservesSingleCompoundField(string subject, string teacher, string room)
    {
        var (doc, result) = InlineValues(subject, teacher, room);
        var oldResult = result with { Metadata = result.Metadata with { ValidatorVersion = 4 } };
        Assert.False(RecoveryValidator.CanReuse(Accept(doc, oldResult), doc, oldResult));
        Assert.Empty(RecoveryValidator.Validate(doc, result).Errors);
        Assert.True(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
    }
    private static (RecoveryDocument, RecoveryResult) InlineValues(string subject, string teacher, string room)
    {
        var (doc, result) = Proposal();
        var text = new Dictionary<string, string> { ["subject"] = subject, ["teacher"] = teacher, ["room"] = room };
        doc = doc with { Sources = doc.Sources.Select(s => text.TryGetValue(s.Id, out var value) ? s with { Text = value } : s).ToArray() };
        var lesson = result.Cells[0].Lessons[0];
        result = result with { Cells = result.Cells.Select((c, i) => i == 0 ? c with {
            Lessons = [lesson with { Subject = lesson.Subject with { Value = subject }, Teacher = lesson.Teacher with { Value = teacher }, Room = lesson.Room with { Value = room } }]
        } : c).ToArray() };
        return (doc, result);
    }
    private static RecoveryAcceptance Accept(RecoveryDocument doc, RecoveryResult result) => new(doc.PdfHash,
        RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(doc), result.Metadata, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲・室乙")]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲")]
    [InlineData("科甲", "教甲・教乙", "室甲・室乙")]
    [InlineData("科甲・科乙", "教甲・教乙", "室甲・室乙")]
    public void LegacyFixedCompoundTupleIsRejectedEvenWithAValidCurrentAcceptance(string subject, string teacher, string room)
    {
        var (doc, result) = FixedValues(subject, teacher, room);
        Assert.Contains("fixedParallelEvidence", RecoveryValidator.Validate(doc, result).Errors);
        Assert.False(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
    }

    [Theory]
    [InlineData("科甲・科乙", "教甲", "室甲")]
    [InlineData("科甲", "教甲・教乙", "室甲")]
    [InlineData("科甲", "教甲", "室甲・室乙")]
    public void SingleCompoundFixedFieldRemainsAnExactLiteral(string subject, string teacher, string room)
    {
        var (doc, result) = FixedValues(subject, teacher, room);
        Assert.Empty(RecoveryValidator.Validate(doc, result).Errors);
        Assert.True(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
    }

    private static (RecoveryDocument, RecoveryResult) FixedValues(string subject, string teacher, string room)
    {
        var (doc, result) = Fixture();
        var values = new Dictionary<string, string> { ["subject"] = subject, ["teacher"] = teacher, ["room"] = room };
        doc = doc with { Sources = doc.Sources.Select(s => values.TryGetValue(s.Id, out var text) ? s with { Text = text } : s).ToArray() };
        var lesson = result.Cells[0].Lessons[0];
        result = result with { Cells = result.Cells.Select((c, i) => i == 0 ? c with { Lessons = [lesson with {
            Subject = lesson.Subject with { Value = subject }, Teacher = lesson.Teacher with { Value = teacher }, Room = lesson.Room with { Value = room }
        }] } : c).ToArray() };
        return (doc, result);
    }

    [Fact]
    public void CurrentStructureAndFieldProviderHistoriesAreValidatedIndependentlyWithoutRewritingEither()
    {
        var (doc, result) = Proposal();
        var structure = result.Metadata with { Provider = "structure-provider", ModelId = "structure-model", RuntimeVersion = "structure-runtime", PromptVersion = "3" };
        var field = result.Metadata with { Provider = "field-provider", ModelId = "field-model", RuntimeVersion = "field-runtime", PromptVersion = "4" };
        doc = doc with { StructureMetadata = structure }; result = result with { Metadata = field };
        Assert.Empty(RecoveryValidator.Validate(doc, result).Errors);
        Assert.True(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
        Assert.Equal(structure, doc.StructureMetadata); Assert.Equal(field, result.Metadata);
        Assert.Contains("structureMetadata", RecoveryValidator.Validate(doc with { StructureMetadata = structure with { ValidatorVersion = 4 } }, result).Errors);
        Assert.Contains("structureMetadata", RecoveryValidator.Validate(doc with { StructureMetadata = structure with { PromptVersion = "" } }, result).Errors);
        Assert.Contains("structureMetadata", RecoveryValidator.Validate(doc with { StructureMetadata = structure with { RecoverySchemaVersion = 99 } }, result).Errors);
    }

    [Fact]
    public void TwoIndependentlyBoundFixedTuplesRemainTwoLessons()
    {
        var (doc, result) = Fixture();
        var ids = new[] { "subject", "teacher", "room" };
        var second = ids.Select(id => doc.Sources.Single(s => s.Id == id) with { Id = id + "2", Text = "架空の第二" + id }).ToArray();
        var cell = doc.Cells[0] with { ParallelCount = 2, SourceIds = [.. doc.Cells[0].SourceIds, .. second.Select(s => s.Id)],
            LessonBindings = [doc.Cells[0].LessonBindings[0], new(["subject2"], ["teacher2"], ["room2"])] };
        doc = doc with { Sources = [.. doc.Sources, .. second], Cells = [cell, .. doc.Cells.Skip(1)] };
        var lesson = result.Cells[0].Lessons[0];
        RecoveryField Field(int i) => new(RecoveryValueState.Present, second[i].Text, [second[i].Id]);
        result = result with { Cells = [result.Cells[0] with { Lessons = [lesson, lesson with {
            Subject = Field(0), Teacher = Field(1), Room = Field(2)
        }] }, .. result.Cells.Skip(1)] };
        Assert.Empty(RecoveryValidator.Validate(doc, result).Errors);
        Assert.True(RecoveryValidator.CanReuse(Accept(doc, result), doc, result));
        Assert.Equal(2, result.Cells[0].Lessons.Count);
    }
}
