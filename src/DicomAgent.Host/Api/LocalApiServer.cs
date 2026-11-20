using System.Net;
using DicomAgent.Application.Models;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Application.Services;
using DicomAgent.Host.Controllers;
using DicomAgent.Infrastructure.Management;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DicomAgent.Host.Api;

// Runs the localhost-only HTTP API: web browsers talk to /api/capture-requests
// (Authorization + TenantName), the Desktop talks to /api/management (local key).
public sealed class LocalApiServer(LocalApiOptions options, ICaptureRequestService captures, ManagementService management, ManagementAccess access) : BackgroundService
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task StartAsync(CancellationToken ct)
    {
        await base.StartAsync(ct);
        await ready.Task.WaitAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RunServerAsync(stoppingToken); }
        catch (Exception ex) { ready.TrySetException(ex); throw; }
    }

    private async Task RunServerAsync(CancellationToken stoppingToken)
    {
        CaptureRules.Validate(options);
        await access.InitializeAsync(options.Port, stoppingToken);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        // Avoid recording patient data, auth headers, query strings or tenant request bodies in access logs.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(ConfigureKestrel);
        builder.Services.AddRateLimiter(ConfigureRateLimiter);
        builder.Services.AddSingleton(captures);
        builder.Services.AddSingleton(management);
        builder.Services.AddSingleton(options);
        builder.Services.AddControllers().AddApplicationPart(typeof(CaptureRequestsController).Assembly);
        await using var app = builder.Build();
        app.Use(async (context, next) => await RequestGuardAsync(context, next));
        app.UseRateLimiter();
        app.MapControllers().RequireRateLimiting("local");
        await app.StartAsync(stoppingToken);
        ready.TrySetResult();
        await app.WaitForShutdownAsync(stoppingToken);
    }

    private void ConfigureKestrel(KestrelServerOptions server)
    {
        server.Listen(IPAddress.Loopback, options.Port);
        server.Limits.MaxRequestBodySize = 65536;
        server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        server.Limits.MaxConcurrentConnections = 32;
    }

    private void ConfigureRateLimiter(RateLimiterOptions limits)
    {
        limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limits.AddPolicy("local", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("local", _ => new()
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    }

    // One guard for every request, in order:
    //   1. loopback host + correct port;
    //   2. /api/management rejects browser origins and requires the local key;
    //   3. browser origins must be allowlisted (CORS);
    //   4. exception mapping to HTTP status codes.
    private async Task RequestGuardAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Host.Port != options.Port || context.Request.Host.Host is not ("127.0.0.1" or "localhost"))
        {
            context.Response.StatusCode = 403;
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Path.StartsWithSegments("/api/management", StringComparison.OrdinalIgnoreCase))
        {
            if (origin.Length > 0)
            {
                context.Response.StatusCode = 403;
                return;
            }
            if (!access.Authorize(context.Request.Headers[ManagementAccess.Header].ToString()))
            {
                context.Response.StatusCode = 401;
                return;
            }
        }
        if (origin.Length > 0)
        {
            if (!CaptureRules.OriginAllowed(options, origin))
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
            context.Response.Headers.AccessControlAllowHeaders = "Authorization, TenantName, Content-Type, Idempotency-Key";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        }
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = origin.Length > 0 ? 204 : 403;
            return;
        }
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException)
        {
            await Reject(context, 400, "Invalid headers or request body. Check required fields and UUID values.");
        }
        catch (CaptureConflictException ex)
        {
            await Reject(context, 409, ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = 401;
        }
        catch (KeyNotFoundException)
        {
            context.Response.StatusCode = 404;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception)
        {
            await Reject(context, 503, "Local request storage is unavailable.");
        }
    }

    private static async Task Reject(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
