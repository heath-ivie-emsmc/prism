namespace Prism.Api;

public static class ApiRoutes
{
    /// <summary>
    /// The current version of the JSON API. A breaking change gets a new prefix (<c>/api/v2</c>) beside this one, never an edit to it.
    /// The OpenAPI snapshot test makes any change to this surface a deliberate act. See docs/adr/0002.
    /// </summary>
    public const string V1 = "/v1";
}
