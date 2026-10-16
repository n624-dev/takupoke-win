using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualInputStateTests
{
    private static Dictionary<string,string> Fields(int count) => Enumerable.Range(1,count)
        .ToDictionary(i => "fictional-cell:" + i + ":Subject", i => "架空未確定" + i);
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void PrefillRequiresEveryAcknowledgementAndEditingResetsIt(int count)
    {
        var state = new RecoveryManualInputState(); var fields = Fields(count);
        state.Bind("original-source/lease/snapshot", fields);
        Assert.False(state.Ready); Assert.Throws<InvalidOperationException>(() => state.Submission());
        foreach (var key in fields.Keys) state.Acknowledge(key, true);
        Assert.True(state.Ready);
        var first = fields.Keys.First(); state.Edit(first, "架空確認済全文");
        Assert.False(state.Ready); Assert.False(state.IsAcknowledged(first));
        state.Acknowledge(first, true);
        Assert.Equal("架空確認済全文", state.Submission()[first]);
    }
    [Fact]
    public void RebindingSameSessionKeepsTypedTextAndAcksButNewIdentityClearsBoth()
    {
        var state = new RecoveryManualInputState(); var fields = Fields(3);
        state.Bind("same-session", fields);
        foreach (var key in fields.Keys) { state.Edit(key, "架空入力" + key); state.Acknowledge(key, true); }
        for (var i = 0; i < 5; i++) state.Bind("same-session", fields); // Refresh, resize, tab or idle return.
        Assert.True(state.Ready); Assert.StartsWith("架空入力", state.Values[fields.Keys.First()]);
        state.Bind("new-source/hash/lease/snapshot", fields);
        Assert.False(state.Ready); Assert.Equal(fields, state.Values);
        state.Clear(); Assert.Empty(state.Values); Assert.False(state.Ready);
    }
    [Fact]
    public void FourFieldsMissingFieldBlankAndOverLimitCannotSubmit()
    {
        var state = new RecoveryManualInputState(); state.Bind("four", Fields(4));
        Assert.Empty(state.Values); Assert.False(state.Ready);
        var fields = Fields(1); var key = fields.Keys.Single(); state.Bind("one", fields);
        state.Edit(key, " "); state.Acknowledge(key, true); Assert.False(state.Ready);
        state.Edit(key, new string('x',257)); state.Acknowledge(key, true); Assert.False(state.Ready);
        state.Edit(key, string.Concat(Enumerable.Repeat("🧪",128))); state.Acknowledge(key, true); Assert.True(state.Ready);
        var submitted = state.Submission(); state.Edit(key,"架空次入力");
        Assert.Equal(256, submitted[key].Length); // Submission is a detached immutable snapshot.
        Assert.Throws<InvalidOperationException>(() => state.Edit("foreign-role", "架空"));
    }
}
