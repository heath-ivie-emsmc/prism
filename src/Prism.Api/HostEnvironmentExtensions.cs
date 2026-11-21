namespace Prism.Api;

public static class HostEnvironmentExtensions
{
    /// <summary>
    /// True for every environment except Development and Testing. Hardening that every cluster needs (secure cookies, HSTS,
    /// no http URLs) hangs off this, never off a name like "Production", so QA and Staging are covered too.
    /// </summary>
    public static bool IsDeployedEnvironment(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
    }
}
