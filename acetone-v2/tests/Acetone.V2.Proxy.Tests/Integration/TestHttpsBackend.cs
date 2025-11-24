using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Acetone.V2.Proxy.Tests.Integration;

/// <summary>
/// Spins up a minimal HTTPS backend using a certificate thumbprint to validate proxy forwarding.
/// </summary>
internal sealed class TestHttpsBackend : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Task _runTask;

    public int Port { get; }
    public string Url => $"https://localhost:{Port}";

    private TestHttpsBackend(WebApplication app, Task runTask, int port)
    {
        _app = app;
        _runTask = runTask;
        Port = port;
    }

    public static async Task<TestHttpsBackend> StartAsync(string? thumbprint)
    {
        // Prefer a supplied certificate on Windows, but fall back to a self-signed localhost cert
        // so CI can run without pre-provisioned certs.
        X509Certificate2 cert = (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(thumbprint))
            ? (FindCertificate(thumbprint) ?? CreateSelfSignedLocalhostCertificate())
            : CreateSelfSignedLocalhostCertificate();

        int port = GetFreeTcpPort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(cert));
        });

        var app = builder.Build();
        app.MapGet("/weatherforecast", () => Results.Json(new[]
        {
            new { date = DateTime.UtcNow.Date.AddDays(1), temperatureC = 10, summary = "Test" }
        }));
        app.MapGet("/", () => Results.Ok("backend ok"));

        var runTask = app.RunAsync();
        await WaitForBackendReadyAsync($"https://localhost:{port}/");

        return new TestHttpsBackend(app, runTask, port);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
            await _runTask;
        }
    }

    private static X509Certificate2? FindCertificate(string thumbprint)
    {
        string normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        foreach (var location in new[] { StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                if (string.Equals(cert.Thumbprint?.Replace(" ", string.Empty), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return cert;
                }
            }
        }
        return null;
    }

    private static X509Certificate2 CreateSelfSignedLocalhostCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var eku = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }; // Server Authentication
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        req.CertificateExtensions.Add(sanBuilder.Build());

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return cert;
    }

    private static async Task WaitForBackendReadyAsync(string baseUrl)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var response = await client.GetAsync($"{baseUrl}weatherforecast");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Backend not ready yet; retry.
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException("HTTPS test backend failed to start within timeout.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
