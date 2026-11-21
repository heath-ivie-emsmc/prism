namespace Prism.Application.Auth;

public enum LoginTokenFailure
{
    Invalid,
    Expired,
}

public sealed class LoginTokenInvalidException(LoginTokenFailure failure) : Exception("The login token was rejected.")
{
    public LoginTokenFailure Failure { get; } = failure;
}

public sealed class LoginTokenReplayedException() : Exception("The login token was already used.");

public sealed class MissingRolesException(bool hasUserName, IReadOnlyList<string> roles) : Exception("The user has no role for this application.")
{
    public bool HasUserName { get; } = hasUserName;

    /// <summary>The role names the portal returned. Role names are identifiers, not personal data.</summary>
    public IReadOnlyList<string> Roles { get; } = roles;
}

public sealed class SessionTooLargeException(int length) : Exception("The session token does not fit in a cookie.")
{
    public int Length { get; } = length;
}
