using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureManagementBootstrapStateTests
{
    [Fact]
    public void InitialState_IsNotCompleteOrFailed()
    {
        var state = new FeatureManagementBootstrapState();

        Assert.False(state.IsBootstrapComplete);
        Assert.Null(state.BootstrapFailure);
    }

    [Fact]
    public void MarkBootstrapComplete_SetsCompleteWithoutFailure()
    {
        var state = new FeatureManagementBootstrapState();

        state.MarkBootstrapComplete();

        Assert.True(state.IsBootstrapComplete);
        Assert.Null(state.BootstrapFailure);
    }

    [Fact]
    public void MarkBootstrapComplete_Idempotent()
    {
        var state = new FeatureManagementBootstrapState();
        state.MarkBootstrapComplete();
        state.MarkBootstrapComplete();

        Assert.True(state.IsBootstrapComplete);
        Assert.Null(state.BootstrapFailure);
    }

    [Fact]
    public void MarkBootstrapFailed_RetainsOriginalFailureAndKeepsStateIncomplete()
    {
        var state = new FeatureManagementBootstrapState();
        var failure = new TimeoutException("sidecar bootstrap timed out");

        state.MarkBootstrapFailed(failure);
        state.MarkBootstrapFailed(new InvalidOperationException("later failure"));

        Assert.False(state.IsBootstrapComplete);
        Assert.Same(failure, state.BootstrapFailure);
    }
}
