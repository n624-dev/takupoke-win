using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class RecoveryCodeCandidatesTests
{
    [Fact]
    public void JapaneseRoomNamesKeepEveryNonconfusableCharacterAndLiteralSpace()
    {
        MappingRule[] rooms = [new("仮設室101", "別の表示名"), new("Alias", "仮設室I0I"),
            new("他の室101", "Other"), new("仮設室 101", "Spaced")];
        Assert.Equal(new[] { "仮設室101", "仮設室I0I" }, RecoveryCodeCandidates.Find("仮設室I01", rooms, ["1_1"]));
        Assert.Equal(new[] { "仮設室 101" }, RecoveryCodeCandidates.Find("仮設室 I01", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("仮設宝I01", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("仮設室I01", [new("101", "Room")], ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("仮設室I01\n", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("仮設室I01", [new(" 仮設室101", "Room")], ["1_1"]));
    }
    [Fact]
    public void KnownRosterSuppliesOnlyLiteralConfusionsAndKeepsMultipleAlternatives()
    {
        MappingRule[] rooms = [new("I01", "First"), new("101", "Second"), new("l01", "Third"),
            new("I0I", "Fourth"), new("I0l", "Fifth"), new("I01", "Duplicate"), new("1010", "Other length")];
        // An exact known code does not establish that the OCR chose it correctly.
        Assert.Equal(new[] { "101", "I0I", "I0l" }, RecoveryCodeCandidates.Find("I01", rooms, ["1_1"]));
        Assert.Equal("I01", rooms[0].Alias);
    }

    [Fact]
    public void UnrelatedNamesUnknownCodesCaseAndTooManyChangesAreNotInvented()
    {
        MappingRule[] rooms = [new("Lab-101", "Lab-102"), new("lab-101", "Other"),
            new("lO1", "Name"), new("O01", "Name")];
        Assert.Empty(RecoveryCodeCandidates.Find("Unseen-42", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("LAB-101", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("1010", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("教室101", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("１01", rooms, ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("O1O0", [new("010O", "Name")], ["1_1"]));
    }

    [Fact]
    public void EveryClassInMergedCellMustBeCoveredAndGlobalNamesRemainAvailable()
    {
        MappingRule[] rooms = [new("101", "Room", ["1_1"]), new("I0l", "Room"),
            new("I0I", "Room", ["1_1", "1_2"]), new("l01", "Room", [])];
        Assert.Equal(new[] { "I0I", "I0l" }, RecoveryCodeCandidates.Find("I01", rooms, ["1_1", "1_2"]));
        Assert.Empty(RecoveryCodeCandidates.Find("I01", rooms, []));
    }

    [Fact]
    public void FullNameMayBeCodeButWorkAndCancellationStayBounded()
    {
        Assert.Equal(new[] { "O10" }, RecoveryCodeCandidates.Find("010", [new("Lecture hall", "O10")], ["1_1"]));
        Assert.Empty(RecoveryCodeCandidates.Find("I01", Enumerable.Repeat(new MappingRule("101", "Room"),
            RecoveryCodeCandidates.MaximumRules + 1).ToArray(), ["1_1"]));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => RecoveryCodeCandidates.Find("I01", [], ["1_1"], cancelled.Token));
    }
}
