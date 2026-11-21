namespace Prism.Utility.AdminPortalSetup;

internal enum Command
{
    Sync,
    GetPermissions,
    PrintPublicKey,
    Help,
}

internal sealed record SyncOptions(
    Command Command,
    string Environment,
    string? ApplicationId,
    string? KeyFile,
    bool OnlyPermissions,
    bool OnlyRoles,
    bool DryRun)
{
    /// <summary>Returns the options, or an error message. Unknown arguments are an error so a typo cannot silently change what runs.</summary>
    public static (SyncOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        var options = new SyncOptions(Command.Sync, "dev", null, null, false, false, false);
        var remaining = new Queue<string>(args);
        while (remaining.TryDequeue(out string? arg))
        {
            switch (arg)
            {
                case "--environment" or "--application-id" or "--key-file":
                    if (!remaining.TryDequeue(out string? value))
                    {
                        return (null, $"{arg} needs a value.");
                    }

                    options = arg switch
                    {
                        "--environment" => options with { Environment = value },
                        "--application-id" => options with { ApplicationId = value },
                        _ => options with { KeyFile = value },
                    };
                    break;
                case "--only-permissions":
                    options = options with { OnlyPermissions = true };
                    break;
                case "--only-roles":
                    options = options with { OnlyRoles = true };
                    break;
                case "--dry-run":
                    options = options with { DryRun = true };
                    break;
                case "--get-permissions":
                    options = options with { Command = Command.GetPermissions };
                    break;
                case "--print-public-key":
                    options = options with { Command = Command.PrintPublicKey };
                    break;
                case "--help" or "-h":
                    options = options with { Command = Command.Help };
                    break;
                default:
                    return (null, $"Unknown argument: {arg}");
            }
        }

        return options.OnlyPermissions && options.OnlyRoles
            ? (null, "--only-permissions and --only-roles cannot be combined. Omit both to publish both.")
            : (options, null);
    }
}
