using System.Net;
using DicomAgent.Application.Models;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Application.Services;
using DicomAgent.Host.Controllers;
using DicomAgent.Infrastructure.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DicomAgent.Host.Api;

public sealed class LocalApiServer(LocalApiOptions options, ICaptureRequestService captures, ManagementService management, ManagementAccess access) : BackgroundService
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async Task StartAsync(CancellationToken ct)
    {
        await base.StartAsync(ct); await ready.Task.WaitAsync(ct);
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
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, options.Port);
            server.Limits.MaxRequestBodySize = 65536;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.MaxConcurrentConnections = 32;
        });
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.AddPolicy("local", context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("local", _ => new()
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Services.AddSingleton(captures);
        builder.Services.AddSingleton(management);
        builder.Services.AddSingleton(options);
        builder.Services.AddControllers().AddApplicationPart(typeof(CaptureRequestsController).Assembly);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Port != options.Port || context.Request.Host.Host is not ("127.0.0.1" or "localhost"))
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            var origin = context.Request.Headers.Origin.ToString();
            if (context.Request.Path.StartsWithSegments("/api/management", StringComparison.OrdinalIgnoreCase))
            {
                if (origin.Length > 0) { context.Response.StatusCode = 403; return; }
                if (!access.Authorize(context.Request.Headers[ManagementAccess.Header].ToString())) { context.Response.StatusCode = 401; return; }
            }
            if (origin.Length > 0)
            {
                if (!CaptureRules.OriginAllowed(options, origin)) { context.Response.StatusCode = 403; return; }
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.Vary = "Origin";
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, TenantName, Content-Type, Idempotency-Key";
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            }
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = origin.Length > 0 ? 204 : 403; return; }
            try { await next(context); }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or BadHttpRequestException)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid headers or request body. Check required fields and UUID values." }); }
            catch (CaptureConflictException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
            catch (UnauthorizedAccessException) { context.Response.StatusCode = 401; }
            catch (KeyNotFoundException) { context.Response.StatusCode = 404; }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Local request storage is unavailable." }); }
        });
        app.UseRateLimiter();
        app.MapControllers().RequireRateLimiting("local");
        await app.StartAsync(stoppingToken); ready.TrySetResult();
        await app.WaitForShutdownAsync(stoppingToken);
    }
}
