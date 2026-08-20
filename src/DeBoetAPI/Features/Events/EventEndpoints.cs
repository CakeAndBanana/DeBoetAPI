using System.Text.Json;
using DeBoetAPI.Common;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Features.Events;

/// <summary>
/// A live feed of changes, so a panel open in one place notices what was done in another.
/// </summary>
public static class EventEndpoints
{
    /// <summary>How long an idle stream waits before sending a comment to hold the line open.
    /// Well inside the 60 seconds a default proxy allows before deciding a connection is dead.</summary>
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/events", StreamAsync)
            .WithTags("Events")
            .WithName("StreamChanges")
            .WithSummary("Server-sent events, one per recorded change.")
            .RequireAuthorization(AuthPolicies.Editor)
            .ExcludeFromDescription();

        return app;
    }

    private static async Task StreamAsync(HttpContext context, ChangeStream changes)
    {
        var listener = context.User.GetUserId();
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = changes.Subscribe(out var reader);
        var cancellation = context.RequestAborted;

        await WriteAsync(context, ": connected\n\n", cancellation);

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var next = reader.WaitToReadAsync(cancellation).AsTask();
                var timer = Task.Delay(KeepAlive, cancellation);

                if (await Task.WhenAny(next, timer) == timer)
                {
                    await WriteAsync(context, ": keep-alive\n\n", cancellation);
                    continue;
                }

                if (!await next)
                {
                    return;
                }

                while (reader.TryRead(out var change))
                {
                    if (!ConcernsListener(change, listener))
                    {
                        continue;
                    }

                    var payload = JsonSerializer.Serialize(change, Json);
                    await WriteAsync(context, $"event: change\ndata: {payload}\n\n", cancellation);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The browser navigated away or the network dropped. Nothing to report.
        }
    }

    private static bool ConcernsListener(ChangeEvent change, Guid listener) =>
        change.EntityType != nameof(AuditedEntityType.User) || change.EntityId == listener;

    private static async Task WriteAsync(HttpContext context, string text, CancellationToken ct)
    {
        await context.Response.WriteAsync(text, ct);
        await context.Response.Body.FlushAsync(ct);
    }
}
