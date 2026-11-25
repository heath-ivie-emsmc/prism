using Prism.Application.SavedViews;

namespace Prism.Application.Admin;

/// <summary>
/// Delivers pushed View Templates to the user as clones in their own Saved Views. Called when the user opens their views, with the Provider
/// from the checked route and the user from the session. Once per Assignment, user and Provider; a deleted copy is not delivered again.
/// </summary>
public interface ITemplateDelivery
{
    /// <returns>How many views were created.</returns>
    Task<int> DeliverPendingAsync(SavedViewActor actor, CancellationToken cancellationToken);
}
