namespace Prism.Tests.Configuration;

// Environment variables are process-wide, including the PRISM_SECRET_OVERRIDE__ ones several loader tests set and clear.
[CollectionDefinition(nameof(SecretsLoaderEnvironment), DisableParallelization = true)]
public sealed class SecretsLoaderEnvironment;
