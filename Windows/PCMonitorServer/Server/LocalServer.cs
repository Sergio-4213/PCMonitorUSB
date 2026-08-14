using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCMonitorUSB.Commands;
using PCMonitorUSB.Config;
using PCMonitorUSB.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PCMonitorUSB.Localization;

namespace PCMonitorUSB.Server;

public sealed class ConnectionTracker
{
    private long _lastContactTicks;
    public DateTimeOffset? LastContact => Interlocked.Read(ref _lastContactTicks) is var ticks && ticks > 0
        ? new DateTimeOffset(ticks, TimeSpan.Zero)
        : null;
    public bool IsPanelConnected => LastContact is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(5);
    public void MarkContact() => Interlocked.Exchange(ref _lastContactTicks, DateTimeOffset.UtcNow.UtcTicks);
}

public sealed class LocalServer : IAsyncDisposable
{
    private readonly IStatsProvider _stats;
    private readonly ConfigStore _config;
    private readonly CommandService _commands;
    private readonly byte[] _apiTokenBytes;
    private WebApplication? _app;
    private long _lastCommandTick;

    public LocalServer(IStatsProvider stats, ConfigStore config, CommandService commands)
    {
        _stats = stats;
        _config = config;
        _commands = commands;
        ApiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _apiTokenBytes = System.Text.Encoding.ASCII.GetBytes(ApiToken);
    }

    public string ApiToken { get; }
    public ConnectionTracker Connection { get; } = new();
    public bool IsRunning => _app is not null;
    public int Port => _config.Current.Port;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(LocalServer).Assembly.FullName
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, Port);
            options.Limits.MaxRequestBodySize = 8 * 1024;
            options.AddServerHeader = false;
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (context.Request.Path.StartsWithSegments("/api") && !IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (context.Request.Path.StartsWithSegments("/api")) Connection.MarkContact();
            await next();
        });

        app.MapGet("/", () => Results.Content(DashboardHtml, "text/html; charset=utf-8"));
        app.MapGet("/api/stats", (HttpContext context) =>
        {
            return Results.Ok(_stats.Current);
        });
        app.MapGet("/api/system", (HttpContext context) =>
        {
            return Results.Ok(_stats.Profile);
        });
        app.MapGet("/api/config", (HttpContext context) =>
        {
            var config = _config.Current;
            var buttons = config.Buttons
                .Where(x => x.Enabled)
                .Select(x => new ApiButton(x.Id, x.BuiltIn ? AppLanguage.BuiltInButtonLabel(x.Id, x.Label) : x.Label, x.Icon, _commands.IsAvailable(x)))
                .ToArray();
            return Results.Ok(new PanelConfigResponse(
                config.UpdateIntervalMs,
                config.ShowCpu,
                config.ShowGpu,
                config.ShowRam,
                config.ShowVram,
                config.ShowNetwork,
                config.ShowDisk,
                config.ShowFps,
                new TemperatureConfig(config.CpuElevatedTemperature, config.CpuCriticalTemperature,
                    config.GpuElevatedTemperature, config.GpuCriticalTemperature),
                buttons,
                AppLanguage.CurrentCode,
                WakeOnLanService.Detect(config.EnableWakeOnLan)));
        });
        app.MapGet("/api/ping", (HttpContext context) =>
        {
            return Results.Ok(new { ok = true, server = "PC Monitor USB" });
        });
        app.MapPost("/api/command", async (HttpContext context) =>
        {
            if (context.Request.ContentType is null ||
                !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { ok = false, error = AppLanguage.T("Content-Type inválido.", "Invalid Content-Type.") });

            var now = Environment.TickCount64;
            var previous = Interlocked.Exchange(ref _lastCommandTick, now);
            if (previous > 0 && now - previous < 75)
                return Results.Json(new { ok = false, error = AppLanguage.T("Muitos comandos em sequência.", "Too many commands in quick succession.") }, statusCode: 429);

            CommandRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<CommandRequest>(cancellationToken: context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { ok = false, error = AppLanguage.T("JSON inválido.", "Invalid JSON.") });
            }

            var result = _commands.Execute(request?.Command ?? "");
            return result.Success
                ? Results.Ok(new { ok = true })
                : Results.Json(new { ok = false, error = result.Error }, statusCode: 400);
        });

        await app.StartAsync(cancellationToken);
        _app = app;
        SimpleLog.Info($"Servidor iniciado em 127.0.0.1:{Port}.");
    }

    private bool IsAuthorized(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-PCMonitor-Token", out var provided) || provided.Count != 1)
            return false;
        var token = provided[0];
        if (token is null || token.Length != ApiToken.Length) return false;
        return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token), _apiTokenBytes);
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        if (app is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await app.StopAsync(timeout.Token);
        await app.DisposeAsync();
        SimpleLog.Info("Servidor parado.");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static string DashboardHtml => AppLanguage.T("""
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>PC Monitor USB</title><style>body{font:16px system-ui;background:#0c0d0f;color:#eee;margin:0;padding:24px}main{max-width:800px;margin:auto}h1{font-size:24px}pre{background:#16181c;border:1px solid #30343b;border-radius:10px;padding:18px;overflow:auto;min-height:300px}.ok{color:#56d889}</style></head>
<body><main><h1>PC Monitor USB <span class="ok">● ATIVO</span></h1><p>Servidor local em execução. A API escuta somente em 127.0.0.1 e exige o token temporário enviado ao aplicativo Android pelo ADB.</p><pre>Os sensores permanecem disponíveis no aplicativo Windows e no painel Android autenticado.</pre></main></body></html>
""", """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>PC Monitor USB</title><style>body{font:16px system-ui;background:#0c0d0f;color:#eee;margin:0;padding:24px}main{max-width:800px;margin:auto}h1{font-size:24px}pre{background:#16181c;border:1px solid #30343b;border-radius:10px;padding:18px;overflow:auto;min-height:300px}.ok{color:#56d889}</style></head>
<body><main><h1>PC Monitor USB <span class="ok">● ACTIVE</span></h1><p>Local server running. The API listens only on 127.0.0.1 and requires the temporary token delivered to the Android app through ADB.</p><pre>Sensors remain available in the Windows application and the authenticated Android panel.</pre></main></body></html>
""");
}

public sealed record CommandRequest(string Command);
public sealed record ApiButton(string Id, string Label, string Icon, bool Available);
public sealed record TemperatureConfig(float CpuElevated, float CpuCritical, float GpuElevated, float GpuCritical);
public sealed record PanelConfigResponse(
    int UpdateIntervalMs,
    bool ShowCpu,
    bool ShowGpu,
    bool ShowRam,
    bool ShowVram,
    bool ShowNetwork,
    bool ShowDisk,
    bool ShowFps,
    TemperatureConfig Temperatures,
    IReadOnlyList<ApiButton> Buttons,
    string Language,
    WakeOnLanInfo WakeOnLan);
