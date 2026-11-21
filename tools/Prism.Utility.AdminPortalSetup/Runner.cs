using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Utility.AdminPortalSetup;

internal static class Runner
{
    private const string Usage = """
        Prism.Utility.AdminPortalSetup: publishes Prism's permissions and core roles to the Admin Portal.
        The application must already be registered in the portal. The request bodies come from the same catalog the API uses.

        Usage: dotnet run --project tools/Prism.Utility.AdminPortalSetup -- [options]

          --environment dev|stage|prod   Portal to talk to (default dev).
          --application-id id            Registered application id. Required for stage and prod.
          --key-file path                PEM private key registered for the application.
                                         Or set PRISM_PORTAL_KEY_FILE. The key is read, never printed.
          --only-permissions             Publish only the permission catalog.
          --only-roles                   Publish only the core roles.
          --dry-run                      Print what would be sent. Sends nothing and needs no key.
          --get-permissions              GET app/permissions and print the answer.
          --print-public-key             Print the public key (PKCS#1 and SPKI) for registering the application.
          --help                         This text.
        """;

    public static async Task<int> RunAsync(string[] args, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        (SyncOptions? options, string? error) = SyncOptions.Parse(args);
        if (options is null)
        {
            return await Fail($"{error}\n\n{Usage}");
        }

        if (options.Command == Command.Help)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        PortalEnvironment? environment = PortalEnvironment.Find(options.Environment);
        if (environment is null)
        {
            return await Fail($"Unknown environment '{options.Environment}'. Use one of: {string.Join(", ", PortalEnvironment.GetNames())}.");
        }

        string? applicationId = options.ApplicationId ?? environment.DefaultApplicationId;
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return await Fail($"--application-id is required for {environment.Name}.");
        }

        if (options.Command == Command.Sync && options.DryRun)
        {
            DryRun(options, environment, applicationId, clock);
            return 0;
        }

        return await RunWithKeyAsync(options, environment, applicationId, clock);
    }

    private static async Task<int> RunWithKeyAsync(SyncOptions options, PortalEnvironment environment, string applicationId, TimeProvider clock)
    {
        string? keyFile = options.KeyFile ?? Environment.GetEnvironmentVariable("PRISM_PORTAL_KEY_FILE");
        if (string.IsNullOrWhiteSpace(keyFile) || !File.Exists(keyFile))
        {
            return await Fail("Give the private key with --key-file or PRISM_PORTAL_KEY_FILE (the file must exist).");
        }

        string privateKeyPem = await File.ReadAllTextAsync(keyFile);
        if (options.Command == Command.PrintPublicKey)
        {
            PrintPublicKey(privateKeyPem);
            return 0;
        }

        using HttpClient http = CreateClient(environment, applicationId, privateKeyPem, clock);
        return options.Command == Command.GetPermissions
            ? await GetPermissionsAsync(http)
            : await PublishAsync(http, environment, options, clock);
    }

    private static void DryRun(SyncOptions options, PortalEnvironment environment, string applicationId, TimeProvider clock)
    {
        Console.WriteLine($"Dry run for {environment.Name} ({environment.PortalApiBaseUrl}) as application '{applicationId}'. Nothing is sent.");
        foreach ((string path, string body) in Steps(options, clock))
        {
            Console.WriteLine($"\nPOST {path}\n{body}");
        }
    }

    private static void PrintPublicKey(string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        Console.WriteLine("=== SPKI (BEGIN PUBLIC KEY) ===");
        Console.WriteLine(rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("=== PKCS#1 (BEGIN RSA PUBLIC KEY), if the portal rejects the block above ===");
        Console.WriteLine(rsa.ExportRSAPublicKeyPem());
    }

    private static HttpClient CreateClient(PortalEnvironment environment, string applicationId, string privateKeyPem, TimeProvider clock)
    {
        IOptions<PrismAuthOptions> authOptions = Microsoft.Extensions.Options.Options.Create(new PrismAuthOptions
        {
            ApplicationId = applicationId,
            AdminPortalAudience = environment.Audience,
            ApplicationPrivateKeyPem = privateKeyPem,
        });
        string jwt = new PortalAppJwtFactory(authOptions, clock).Create();

        var http = new HttpClient
        {
            BaseAddress = new Uri(string.Concat(environment.PortalApiBaseUrl, '/')),
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Ems-Application-Id", applicationId);
        return http;
    }

    private static async Task<int> GetPermissionsAsync(HttpClient http)
    {
        using HttpResponseMessage response = await http.GetAsync(PortalSyncRequests.PermissionsPath);
        Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
        Console.WriteLine(await response.Content.ReadAsStringAsync());
        return response.IsSuccessStatusCode ? 0 : 1;
    }

    // Permissions go first: the roles refer to them. Stops at the first failure.
    private static async Task<int> PublishAsync(HttpClient http, PortalEnvironment environment, SyncOptions options, TimeProvider clock)
    {
        foreach ((string path, string body) in Steps(options, clock))
        {
            Console.WriteLine($"=== POST {environment.PortalApiBaseUrl}/{path} ===");
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync(path, content);
            Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
            string text = await response.Content.ReadAsStringAsync();
            Console.WriteLine(string.IsNullOrWhiteSpace(text) ? "(empty body)" : text);
            if (!response.IsSuccessStatusCode)
            {
                return 1;
            }
        }

        return 0;
    }

    private static List<(string Path, string Body)> Steps(SyncOptions options, TimeProvider clock)
    {
        var steps = new List<(string Path, string Body)>();
        if (!options.OnlyRoles)
        {
            steps.Add((PortalSyncRequests.PermissionsPath, PortalSyncRequests.Serialize(PortalSyncRequests.PermissionsBody())));
        }

        if (!options.OnlyPermissions)
        {
            steps.Add((PortalSyncRequests.CoreRolesPath, PortalSyncRequests.Serialize(PortalSyncRequests.CoreRolesBody(clock.GetUtcNow()))));
        }

        return steps;
    }

    private static async Task<int> Fail(string message)
    {
        await Console.Error.WriteLineAsync(message);
        return 2;
    }
}
