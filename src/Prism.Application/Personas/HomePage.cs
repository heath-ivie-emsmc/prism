using Prism.Application.SavedViews;

namespace Prism.Application.Personas;

public static class HomeSources
{
    /// <summary>No usable Persona or template: the client shows the Service Line default.</summary>
    public const string Default = "default";

    /// <summary>The Persona's current published View Template, followed live.</summary>
    public const string Persona = "persona";

    /// <summary>The user's own copy, made when they customized it.</summary>
    public const string Custom = "custom";
}

/// <summary>Where a user's home page comes from. <paramref name="Layout"/> is set for <c>persona</c>, <paramref name="SavedViewId"/> for <c>custom</c>.</summary>
public sealed record HomeResolution(string Source, SavedViewLayout? Layout = null, int? SavedViewId = null);

public enum HomeCustomizeStatus
{
    /// <summary>The copy was made, or already existed.</summary>
    Customized,

    /// <summary>There is no Persona template to copy.</summary>
    NothingToCustomize,

    /// <summary>The user already has too many Saved Views to make another.</summary>
    LimitReached,
}

public sealed record HomeCustomizeResult(HomeCustomizeStatus Status, int? SavedViewId = null);

/// <summary>The home page of the signed-in user in one Provider. The actor was authorized for that Provider by the caller.</summary>
public interface IHomePage
{
    Task<HomeResolution> ResolveAsync(SavedViewActor actor, CancellationToken cancellationToken);

    /// <summary>Forks the Persona's template into the user's Saved Views as their home page.</summary>
    Task<HomeCustomizeResult> CustomizeAsync(SavedViewActor actor, CancellationToken cancellationToken);

    /// <summary>Removes the user's copy so the home page follows the Persona's template again. True when a copy was removed.</summary>
    Task<bool> ResetAsync(SavedViewActor actor, CancellationToken cancellationToken);
}
