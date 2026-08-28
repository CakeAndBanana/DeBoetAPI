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
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maps the event endpoints to the given <see cref="IEndpointRouteBuilder"/>.
    /// This includes a single endpoint for streaming server-sent events of recorded changes.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated endpoint route builder.</returns>
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

    /// <summary>
    /// Streams the recorded changes to the client as server-sent events. 
    /// Each event is a JSON-serialized <see cref="ChangeEvent"/> object.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="changes">The change stream.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Corners the given change event to the given listener. 
    /// If the change event is about a user, it only concerns the listener if it is about that user.
    /// </summary>
    /// <param name="change">The change event.</param>
    /// <param name="listener">The listener.</param>
    /// <returns><c>true</c> if the change event concerns the listener; otherwise, <c>false</c>.</returns>
    private static bool ConcernsListener(ChangeEvent change, Guid listener) =>
        change.EntityType != nameof(AuditedEntityType.User) || change.EntityId == listener;

    /// <summary>
    /// Writes the given text to the response body and flushes it. This is used to send server-sent events to the client.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task WriteAsync(HttpContext context, string text, CancellationToken ct)
    {
        await context.Response.WriteAsync(text, ct);
        await context.Response.Body.FlushAsync(ct);
    }
}
