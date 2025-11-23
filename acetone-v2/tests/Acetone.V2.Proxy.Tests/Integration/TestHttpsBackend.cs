using System.Net;
using System.Net.Sockets;
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

    public static async Task<TestHttpsBackend> StartAsync(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new ArgumentException("Thumbprint is required for HTTPS backend", nameof(thumbprint));
        }

        var cert = FindCertificate(thumbprint) ??
                   throw new InvalidOperationException($"Certificate with thumbprint '{thumbprint}' not found in CurrentUser/My or LocalMachine/My.");

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
        // Give Kestrel a moment to bind
        await Task.Delay(200);

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
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
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

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
