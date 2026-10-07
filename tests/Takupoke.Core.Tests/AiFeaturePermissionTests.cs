using Takupoke.Core.Recovery;
using Xunit;
namespace Takupoke.Core.Tests;
public sealed class AiFeaturePermissionTests
{
    [Fact]
    public void OffOnOffRevokesOldOperationsWithoutRevivingThem()
    {
        var policy = new AiFeaturePermission(); Assert.False(policy.Enabled);
        var initial = policy.Capture(); Assert.Throws<OperationCanceledException>(() => policy.Check(initial.Generation));
        Assert.Throws<OperationCanceledException>(() => policy.Check(initial.Generation, true));
        policy.SetEnabled(true); Assert.True(initial.Token.IsCancellationRequested);
        var enabled = policy.Capture(); policy.Check(enabled.Generation, true);
        policy.SetEnabled(false); Assert.True(enabled.Token.IsCancellationRequested);
        policy.SetEnabled(true); Assert.Throws<OperationCanceledException>(() => policy.Check(enabled.Generation, true));
        policy.Check(policy.Capture().Generation, true);
    }
}
