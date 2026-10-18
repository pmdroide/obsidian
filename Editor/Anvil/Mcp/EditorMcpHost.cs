using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anvil.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace Anvil.Mcp;

/// <summary>Application-owned HTTP host, independent of workspace tab visibility.</summary>
public sealed class EditorMcpHost(EditorAutomationService editor) : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SemaphoreSlim? _calls;
    public bool IsRunning => _app != null;
    public string? Endpoint { get; private set; }

    public async Task StartAsync(int port, string token, CancellationToken ct = default)
    {
        if (port is < 1024 or > 65535 || token.Length < 32) throw new ArgumentException("Use a port from 1024-65535 and a token of at least 32 characters.");
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_app != null) return;
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(), ApplicationName = typeof(EditorMcpHost).Assembly.FullName,
                ContentRootPath = AppContext.BaseDirectory,
            });
            // Host configuration must not load project-controlled appsettings/environment overrides.
            builder.Configuration.Sources.Clear();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
                options.Limits.MaxRequestBodySize = 256 * 1024;
                options.Limits.MaxConcurrentConnections = 32;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            });
            builder.Logging.ClearProviders(); // Never log HTTP authorization or untrusted request arguments.
            builder.Services.AddSingleton(editor);
            builder.Services.AddMcpServer().WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithTools<EditorReadTools>().WithTools<EditorWriteTools>()
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
                {
                    var result = await next(request, cancellationToken);
                    if (JsonSerializer.SerializeToUtf8Bytes(result).Length > 4 * 1024 * 1024)
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = "RESULT_TOO_LARGE: Result exceeds 4 MiB. Use a smaller page or filter. For a write, query editor_get_operation before retrying; the edit may already be applied." }],
                        };
                    return result;
                }));
            var app = builder.Build();
            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token));
            var calls = new SemaphoreSlim(16, 16);
            app.Use(async (context, next) =>
            {
                var host = context.Request.Host;
                if ((host.Host != "127.0.0.1" && !host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) || host.Port != port ||
                    context.Request.Headers.ContainsKey("Origin"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (context.Request.Path != "/mcp") { context.Response.StatusCode = 404; return; }
                byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()));
                if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                if (!await calls.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
                try { await next(context); }
                finally { calls.Release(); }
            });
            app.MapMcp("/mcp");
            editor.Start();
            try { await app.StartAsync(ct); }
            catch { editor.Stop(); await app.DisposeAsync(); calls.Dispose(); throw; }
            _app = app;
            _calls = calls;
            Endpoint = $"http://127.0.0.1:{port}/mcp";
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        editor.Stop(); // Cancel queued work immediately, before waiting for active HTTP requests to leave.
        await _lifecycle.WaitAsync();
        try
        {
            var app = _app;
            var calls = _calls;
            _app = null;
            _calls = null;
            Endpoint = null;
            if (app == null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await app.StopAsync(timeout.Token); }
            finally { await app.DisposeAsync(); calls?.Dispose(); }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
