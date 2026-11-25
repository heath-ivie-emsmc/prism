using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>Tracks completion of feature-management startup (sidecar bootstrap or config-only marker) for readiness checks.</summary>
public sealed class FeatureManagementBootstrapState
{
    private readonly Lock _sync = new();
    private bool _bootstrapComplete;
    private Exception? _bootstrapFailure;

    public void MarkBootstrapComplete()
    {
        lock (_sync)
        {
            if (_bootstrapFailure is null)
                _bootstrapComplete = true;
        }
    }

    public void MarkBootstrapFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_sync)
        {
            _bootstrapFailure ??= exception;
            _bootstrapComplete = false;
        }
    }

    public bool IsBootstrapComplete
    {
        get
        {
            lock (_sync)
                return _bootstrapComplete;
        }
    }

    public Exception? BootstrapFailure
    {
        get
        {
            lock (_sync)
                return _bootstrapFailure;
        }
    }
}
