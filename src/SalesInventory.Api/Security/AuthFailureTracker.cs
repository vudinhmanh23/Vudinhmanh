using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace SalesInventory.Api.Security;

// "Security:AuthFailures" in appsettings.json
public class AuthFailureOptions
{
    // This many failed authentications (401) or refused requests (403) from one client address within the window raise an alert
    // (and again at every further multiple of it)
    public int AlertThreshold { get; set; } = 10;

    public int WindowMinutes { get; set; } = 5;
}

// Counts the 401 and 403 answers per client address over a sliding window, in memory, so that guessing passwords or probing for
// endpoints stands out from the occasional mistyped password. It remembers nothing but an address and some timestamps.
public sealed class AuthFailureTracker
{
    // Upper bound for the number of addresses remembered at once, so an attacker rotating addresses cannot grow the memory without limit
    private const int MaxTrackedClients = 10_000;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _failures = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private int _recordsSincePrune;

    public AuthFailureTracker(IOptions<AuthFailureOptions> options, TimeProvider time)
    {
        _time = time;
        _window = TimeSpan.FromMinutes(Math.Max(1, options.Value.WindowMinutes));
    }

    // Notes one more failure of this client and returns how many it has had within the window (this one included)
    public int Record(string client)
    {
        var now = _time.GetUtcNow();
        PruneSometimes(now);

        if (!_failures.TryGetValue(client, out var queue))
        {
            if (_failures.Count >= MaxTrackedClients)
            {
                return 1; // table full: this one is still logged by the caller, it just cannot be counted
            }

            queue = _failures.GetOrAdd(client, _ => new Queue<DateTimeOffset>());
        }

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > _window)
            {
                queue.Dequeue();
            }

            queue.Enqueue(now);
            return queue.Count;
        }
    }

    // Every so often, forget the clients whose failures are all older than the window
    private void PruneSometimes(DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _recordsSincePrune) < 1000)
        {
            return;
        }

        Interlocked.Exchange(ref _recordsSincePrune, 0);
        foreach (var (client, queue) in _failures)
        {
            lock (queue)
            {
                if (queue.Count == 0 || now - queue.Peek() > _window && now - queue.ToArray()[^1] > _window)
                {
                    _failures.TryRemove(client, out _);
                }
            }
        }
    }
}
