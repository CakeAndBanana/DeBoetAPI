using System.Threading.Channels;
using DeBoetAPI.Data.Entities;

namespace DeBoetAPI.Common;

/// <summary>One change, as broadcast to the sessions that should hear about it.</summary>
/// <param name="EntityType">Category, Item or User.</param>
/// <param name="EntityId">The record that changed.</param>
/// <param name="Action">What happened to it.</param>
/// <param name="ChangedAt">When, so a client can ignore anything it has already seen.</param>
public sealed record ChangeEvent(
    string EntityType,
    Guid EntityId,
    string Action,
    DateTimeOffset ChangedAt);

public sealed class ChangeStream
{
    private readonly List<Channel<ChangeEvent>> _subscribers = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Starts watching. The reader completes when the caller stops enumerating, which happens
    /// when the HTTP connection drops.
    /// </summary>
    public IDisposable Subscribe(out ChannelReader<ChangeEvent> reader)
    {
        var channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        reader = channel.Reader;
        return new Subscription(this, channel);
    }

    /// <summary>Fans a change out to every watcher. Never throws: a broadcast failure must not
    /// fail the request that caused the change.</summary>
    public void Publish(ChangeEvent change)
    {
        Channel<ChangeEvent>[] current;
        lock (_gate)
        {
            current = [.. _subscribers];
        }

        foreach (var subscriber in current)
        {
            subscriber.Writer.TryWrite(change);
        }
    }

    /// <summary>Convenience for the audit rows a save has just written.</summary>
    public void Publish(IEnumerable<AuditEntry> entries)
    {
        foreach (var entry in entries)
        {
            Publish(new ChangeEvent(
                entry.EntityType.ToString(),
                entry.EntityId,
                entry.Action.ToString(),
                entry.ChangedAt));
        }
    }

    /// <summary>
    /// Unscribes a channel from the change stream. This method is called when a subscription is disposed.
    /// </summary>
    /// <param name="channel">The channel to unsubscribe.</param>
    private void Unsubscribe(Channel<ChangeEvent> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Subscription class that represents a subscription to the change stream. 
    /// When disposed, it unsubscribes the channel from the change stream.
    /// </summary>
    /// <param name="stream">The change stream to which the subscription belongs.</param>
    /// <param name="channel">The channel to unsubscribe.   </param>
    private sealed class Subscription(ChangeStream stream, Channel<ChangeEvent> channel) : IDisposable
    {
        public void Dispose() => stream.Unsubscribe(channel);
    }
}
