namespace Prism.Api.Auth;

/// <summary>Sign-in log messages. They carry codes and counts only: never tokens, user names, or response bodies.</summary>
internal static partial class AuthLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Sign-in rejected: login token {Failure}.")]
    public static partial void LoginTokenRejected(ILogger logger, Exception exception, Prism.Application.Auth.LoginTokenFailure failure);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Sign-in rejected: login token replayed.")]
    public static partial void LoginTokenReplayed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Sign-in rejected: user has no identity or no role for this application. Identity present: {HasUserName}. Roles from portal: {RoleCount}.")]
    public static partial void NoRole(ILogger logger, Exception exception, bool hasUserName, int roleCount);

    /// <summary>The role names themselves come from outside, so they are Debug only: never in a deployed environment's default log level.</summary>
    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Roles the portal reported for the rejected user: {Roles}.")]
    public static partial void NoRoleNames(ILogger logger, string roles);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Sign-in rejected: session token of {Length} characters does not fit in a cookie.")]
    public static partial void SessionTooLarge(ILogger logger, Exception exception, int length);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Sign-in failed unexpectedly ({ExceptionType}).")]
    public static partial void SignInFailed(ILogger logger, Exception exception, string exceptionType);

    /// <summary>Counts and flags only. This is how a missing marker role is told apart from a portal that did not report it.</summary>
    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Signed in: {RoleCount} portal roles, {KnownPermissionCount} Prism permissions from them, internal: {IsInternal}.")]
    public static partial void SignedIn(ILogger logger, int roleCount, int knownPermissionCount, bool isInternal);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Roles the portal reported for the signed-in user: {Roles}.")]
    public static partial void SignedInRoleNames(ILogger logger, string roles);
}
