namespace Prism.Tests.Integration;

/// <summary>
/// Tests that read the captured log or trace output. Hosts started by other tests at the same time share process-wide logging and tracing state
/// (the static Serilog logger, activity listeners), so these run alone instead of being flaky.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class LogCaptureGroup
{
    public const string Name = "Log capture";
}
