using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Prism.Tests.Configuration;

/// <summary>
/// Drives the real <see cref="AwsSecretStringReader"/> and the real AWSSDK Secrets Manager client against a loopback
/// HTTP stub that speaks the Secrets Manager JSON protocol. Every other secrets-loader test substitutes a fake reader,
/// so nothing else would notice an AWSSDK upgrade changing the request shape, signing, or error mapping the loader
/// depends on. No container, no AWS account, no network beyond 127.0.0.1.
/// </summary>
public sealed class AwsSecretStringReaderWireTests : IDisposable
{
    private const string SecretId = "nemsis/test/db";
    private const int SdkStandardModeMaxAttempts = 3;

    private readonly HttpListener _listener = new();
    private readonly string _serviceUrl;
    private readonly List<CapturedRequest> _requests = [];
    private readonly Queue<Func<CapturedRequest, StubResponse>> _responses = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serveLoop;

    public AwsSecretStringReaderWireTests()
    {
        int port = FreeLoopbackPort();
        _serviceUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_serviceUrl);
        _listener.Start();
        _serveLoop = Task.Run(ServeAsync, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        try
        {
            _serveLoop.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        catch (AggregateException)
        {
            // The listener closing under a pending GetContext is the expected way the loop ends.
        }

        _stop.Dispose();
    }

    [Fact]
    public async Task GetSecretStringAsync_ReturnsTheSecretStringFromTheWire()
    {
        Enqueue(_ => Json(200, new { Name = SecretId, SecretString = "{\"password\":\"p@ss\"}", VersionId = "v1" }));
        using var reader = CreateReader("Development");

        string? value = await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken);

        Assert.Equal("{\"password\":\"p@ss\"}", value);
    }

    [Fact]
    public async Task GetSecretStringAsync_SendsASignedGetSecretValueRequestForTheSecretId()
    {
        Enqueue(_ => Json(200, new { Name = SecretId, SecretString = "x" }));
        using var reader = CreateReader("Development");

        await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken);

        CapturedRequest request = Assert.Single(_requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("secretsmanager.GetSecretValue", request.Target);
        Assert.Equal(SecretId, JsonElement.Parse(request.Body).GetProperty("SecretId").GetString());
        Assert.StartsWith("AWS4-HMAC-SHA256", request.Authorization, StringComparison.Ordinal);
        Assert.Contains("Credential=test-access-key/", request.Authorization, StringComparison.Ordinal);
        Assert.Contains("/us-east-1/secretsmanager/aws4_request", request.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSecretStringAsync_SignsForTheConfiguredRegion()
    {
        Enqueue(_ => Json(200, new { Name = SecretId, SecretString = "x" }));
        using var reader = CreateReader("Development", region: "eu-west-1");

        await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken);

        Assert.Contains("/eu-west-1/secretsmanager/aws4_request", Assert.Single(_requests).Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSecretStringAsync_ReturnsNullWhenTheSecretHasNoStringValue()
    {
        Enqueue(_ => Json(200, new { Name = SecretId }));
        using var reader = CreateReader("Development");

        Assert.Null(await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSecretStringAsync_RetriesAThrottlingErrorThenSucceeds()
    {
        Enqueue(_ => Json(400, new { __type = "ThrottlingException", message = "Rate exceeded" }));
        Enqueue(_ => Json(200, new { Name = SecretId, SecretString = "after-retry" }));
        using var reader = CreateReader("Development");

        string? value = await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken);

        Assert.Equal("after-retry", value);
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task GetSecretStringAsync_RetriesAServerErrorThenSucceeds()
    {
        Enqueue(_ => Json(500, new { __type = "InternalServiceError", message = "boom" }));
        Enqueue(_ => Json(200, new { Name = SecretId, SecretString = "recovered" }));
        using var reader = CreateReader("Development");

        string? value = await reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken);

        Assert.Equal("recovered", value);
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task GetSecretStringAsync_DoesNotRetryAMissingSecret()
    {
        Enqueue(_ => Json(400, new { __type = "ResourceNotFoundException", message = "Secrets Manager can't find the specified secret." }));
        using var reader = CreateReader("Development");

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken));

        Assert.Single(_requests);
    }

    [Fact]
    public async Task GetSecretStringAsync_DoesNotRetryAnAccessDeniedError()
    {
        Enqueue(_ => Json(400, new { __type = "AccessDeniedException", message = "not allowed" }));
        using var reader = CreateReader("Development");

        await Assert.ThrowsAnyAsync<Amazon.Runtime.AmazonServiceException>(() =>
            reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken));

        Assert.Single(_requests);
    }

    [Fact]
    public async Task GetSecretStringAsync_GivesUpAfterTheMaximumNumberOfAttempts()
    {
        for (int i = 0; i < AwsSecretsRetryPolicy.MaxAttempts + 2; i++)
        {
            Enqueue(_ => Json(400, new { __type = "ThrottlingException", message = "Rate exceeded" }));
        }

        using var reader = CreateReader("Development");

        await Assert.ThrowsAnyAsync<Amazon.Runtime.AmazonServiceException>(() =>
            reader.GetSecretStringAsync(SecretId, TestContext.Current.CancellationToken));

        // The loader's policy makes MaxAttempts calls, and each call is itself retried by the AWSSDK's built-in
        // retry handler (standard mode, 3 attempts), so persistent throttling costs up to MaxAttempts x 3 requests.
        // Pinned as a band: at least one request per policy attempt, never more than both layers allow.
        Assert.InRange(_requests.Count, AwsSecretsRetryPolicy.MaxAttempts, AwsSecretsRetryPolicy.MaxAttempts * SdkStandardModeMaxAttempts);
    }

    [Fact]
    public void Constructor_RequiresARegionWhenNoLocalServiceUrlIsHonoured()
    {
        var options = new AwsSecretsLoaderOptions { Enabled = true, Region = null, ServiceUrl = _serviceUrl };

        // ServiceUrl is ignored outside Development/Testing, so the missing region must be rejected rather than
        // resolved ambiently.
        Assert.Throws<AwsSecretsLoaderException>(() => new AwsSecretStringReader(options, Environment("Production")));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Constructor_HonoursTheServiceUrlOnlyInDevelopmentAndTesting(string environmentName, bool honoured)
    {
        var options = new AwsSecretsLoaderOptions { Enabled = true, Region = "us-east-1", ServiceUrl = _serviceUrl, AccessKey = "test", SecretKey = "test" };

        using var reader = new AwsSecretStringReader(options, Environment(environmentName));

        // Read the configuration the real client was built with rather than sending a request: outside
        // Development/Testing a request would go to the real regional endpoint.
        AmazonSecretsManagerConfig config = ClientConfigOf(reader);
        if (honoured)
        {
            Assert.Equal(_serviceUrl, config.ServiceURL);
        }
        else
        {
            Assert.Null(config.ServiceURL);
            Assert.Equal("us-east-1", config.RegionEndpoint?.SystemName);
        }

        Assert.Empty(_requests);
    }

    private static AmazonSecretsManagerConfig ClientConfigOf(AwsSecretStringReader reader)
    {
        FieldInfo field = typeof(AwsSecretStringReader).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((AmazonSecretsManagerClient)field.GetValue(reader)!).Config is AmazonSecretsManagerConfig config
            ? config
            : throw new InvalidOperationException("Unexpected client config type.");
    }

    private AwsSecretStringReader CreateReader(string environmentName, string region = "us-east-1")
    {
        var options = new AwsSecretsLoaderOptions
        {
            Enabled = true,
            Region = region,
            ServiceUrl = _serviceUrl,
            AccessKey = "test-access-key",
            SecretKey = "test-secret-key",
        };

        return new AwsSecretStringReader(options, Environment(environmentName));
    }

    private static TestHostEnvironment Environment(string name)
    {
        return new TestHostEnvironment { EnvironmentName = name };
    }

    private void Enqueue(Func<CapturedRequest, StubResponse> response)
    {
        lock (_responses)
        {
            _responses.Enqueue(response);
        }
    }

    private static StubResponse Json(int status, object body)
    {
        return new StubResponse(status, JsonSerializer.Serialize(body));
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            using var bodyReader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            string body = await bodyReader.ReadToEndAsync(_stop.Token);
            var captured = new CapturedRequest(
                context.Request.HttpMethod,
                context.Request.Headers["X-Amz-Target"] ?? string.Empty,
                context.Request.Headers["Authorization"] ?? string.Empty,
                body);

            Func<CapturedRequest, StubResponse>? next = null;
            lock (_responses)
            {
                _requests.Add(captured);
                if (_responses.Count > 0)
                {
                    next = _responses.Dequeue();
                }
            }

            StubResponse response = next is null
                ? new StubResponse(500, "{\"__type\":\"InternalFailure\",\"message\":\"stub exhausted\"}")
                : next(captured);

            byte[] payload = Encoding.UTF8.GetBytes(response.Body);
            context.Response.StatusCode = response.Status;
            context.Response.ContentType = "application/x-amz-json-1.1";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, _stop.Token);
            context.Response.Close();
        }
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private sealed record CapturedRequest(string Method, string Target, string Authorization, string Body);

    private sealed record StubResponse(int Status, string Body);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Prism.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
