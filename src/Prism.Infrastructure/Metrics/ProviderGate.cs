namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Limits how many warehouse computations run at once, per Provider and overall, so one tenant cannot use every slot and starve the others.
/// A request first waits for its own Provider's slot, and only then for a global one, so a Provider that is over its share queues on its own gate
/// and never holds a global slot while it waits. The global ceiling keeps the total below the warehouse connection pool. A Provider's gate exists only while
/// someone is using or waiting on it, so the table is bounded by the requests in flight and never grows with the number of Providers seen.
/// Waiting honors the caller's cancellation token and gives back whatever it had already taken.
/// </summary>
internal sealed class ProviderGate(int globalLimit, int perProviderLimit) : IDisposable
{
    private readonly SemaphoreSlim global = new(globalLimit, globalLimit);
    private readonly Dictionary<int, Slot> slots = [];
    private readonly Lock sync = new();

    /// <summary>The number of Providers that currently hold or wait for a gate.</summary>
    internal int TrackedProviders
    {
        get
        {
            lock (sync)
            {
                return slots.Count;
            }
        }
    }

    public async Task<IDisposable> EnterAsync(int providerId, CancellationToken cancellationToken)
    {
        Slot slot = Take(providerId);
        try
        {
            await slot.Gate.WaitAsync(cancellationToken);
        }
        catch
        {
            Return(providerId, slot);
            throw;
        }

        try
        {
            await global.WaitAsync(cancellationToken);
        }
        catch
        {
            slot.Gate.Release();
            Return(providerId, slot);
            throw;
        }

        return new Lease(this, providerId, slot);
    }

    public void Dispose()
    {
        global.Dispose();
        lock (sync)
        {
            foreach (Slot slot in slots.Values)
            {
                slot.Gate.Dispose();
            }

            slots.Clear();
        }
    }

    private Slot Take(int providerId)
    {
        lock (sync)
        {
            if (!slots.TryGetValue(providerId, out Slot? slot))
            {
                slot = new Slot(new SemaphoreSlim(perProviderLimit, perProviderLimit));
                slots[providerId] = slot;
            }

            slot.Users++;
            return slot;
        }
    }

    private void Return(int providerId, Slot slot)
    {
        lock (sync)
        {
            if (--slot.Users > 0)
            {
                return;
            }

            slots.Remove(providerId);
            slot.Gate.Dispose();
        }
    }

    private sealed class Slot(SemaphoreSlim gate)
    {
        public SemaphoreSlim Gate { get; } = gate;

        public int Users { get; set; }
    }

    private sealed class Lease(ProviderGate owner, int providerId, Slot slot) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            owner.global.Release();
            slot.Gate.Release();
            owner.Return(providerId, slot);
        }
    }
}
