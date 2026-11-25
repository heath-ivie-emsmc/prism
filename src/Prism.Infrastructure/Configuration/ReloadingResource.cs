namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Keeps new operations on current credentials and old operations on their original resource. Retired
/// resources are disposed as soon as their last lease ends. At most two generations may remain leased;
/// another rotation fails closed until an older operation drains instead of retaining resources forever.
/// </summary>
public sealed class ReloadingResource<T>(Func<string> readIdentity, Func<string, T> create) : IDisposable
    where T : class, IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<Generation> _generations = [];
    private Generation? _current;
    private bool _disposed;

    public Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string identity = readIdentity();
            if (string.IsNullOrWhiteSpace(identity))
                throw new InvalidOperationException("Current secret-backed resource configuration is missing.");

            if (_current is null || !string.Equals(identity, _current.Identity, StringComparison.Ordinal))
            {
                if (_generations.Count >= 2 && _current?.References > 0)
                    throw new InvalidOperationException("Secret rotation is waiting for an earlier resource lease to drain.");

                // Connect before switching. A failure rejects this operation; it does not silently use stale credentials.
                var replacement = new Generation(identity, create(identity));
                Generation? previous = _current;
                _current = replacement;
                _generations.Add(replacement);
                if (previous is { References: 0 })
                    Retire(previous);
            }

            _current.References++;
            return new Lease(this, _current);
        }
    }

    private void Release(Generation generation)
    {
        lock (_gate)
        {
            generation.References--;
            if (generation.References == 0 && (_disposed || generation != _current))
                Retire(generation);
        }
    }

    private void Retire(Generation generation)
    {
        _generations.Remove(generation);
        generation.Resource.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (Generation generation in _generations.Where(g => g.References == 0).ToArray())
                Retire(generation);
            _current = null;
        }
    }

    internal sealed class Generation(string identity, T resource)
    {
        internal string Identity { get; } = identity;
        internal T Resource { get; } = resource;
        internal int References { get; set; }
    }

    public sealed class Lease : IDisposable
    {
        private ReloadingResource<T>? _owner;
        private readonly Generation _generation;

        internal Lease(ReloadingResource<T> owner, Generation generation)
        {
            _owner = owner;
            _generation = generation;
        }

        public T Resource => _generation.Resource;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_generation);
    }
}
