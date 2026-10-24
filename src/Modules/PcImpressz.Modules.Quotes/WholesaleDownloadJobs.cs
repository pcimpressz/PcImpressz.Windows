using System.Collections.Concurrent;

namespace PcImpressz.Modules.Quotes;

public sealed record WholesaleJobState(string Stage, long Bytes, long? TotalBytes, int? Percent,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, int? Products, string? Error);

public static class WholesaleDownloadJobs
{
    private sealed class Job
    {
        public readonly object Sync = new();
        public WholesaleJobState State = new("queued", 0, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        public bool Running = true;
    }
    private static readonly ConcurrentDictionary<string, Job> Jobs = new(StringComparer.Ordinal);
    private static string Key(string directory) => Path.GetFullPath(directory);
    public static WholesaleJobState Status(string directory) => Jobs.TryGetValue(Key(directory), out var job)
        ? Snapshot(job) : new("idle", 0, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
    private static WholesaleJobState Snapshot(Job job) { lock (job.Sync) return job.State; }
    public static WholesaleJobState Start(string directory, CompMarketQuoteConnection connection)
    {
        if (!connection.Enabled) throw new InvalidOperationException("Előbb engedélyezd a CompMarket-kapcsolatot.");
        var key = Key(directory);
        while (true)
        {
            if (Jobs.TryGetValue(key, out var existing))
            {
                lock (existing.Sync) { if (existing.Running) return existing.State; }
                ((ICollection<KeyValuePair<string, Job>>)Jobs).Remove(new KeyValuePair<string, Job>(key, existing));
                continue;
            }
            var job = new Job();
            if (!Jobs.TryAdd(key, job)) continue;
            _ = Task.Run(async () =>
            {
                try
                {
                    void Progress(string stage, long bytes, long? total)
                    {
                        lock (job.Sync) job.State = new(stage, bytes, total,
                            total is > 0 ? (int)Math.Clamp(bytes * 100.0 / total.Value, 0, 100) : null,
                            job.State.StartedAt, DateTimeOffset.UtcNow, null, null);
                    }
                    var result = await CompMarketQuoteFeed.TestAsync(directory, connection, CancellationToken.None,
                        progress: Progress);
                    lock (job.Sync) job.State = new("completed", job.State.Bytes, job.State.TotalBytes,
                        100, job.State.StartedAt, DateTimeOffset.UtcNow, result.Products, null);
                }
                catch (Exception ex)
                {
                    var error = Explain(ex);
                    lock (job.Sync) job.State = job.State with { Stage = "failed", UpdatedAt = DateTimeOffset.UtcNow, Error = error };
                    try { WholesaleConnections.RecordHealth(directory, false, null, error); } catch (IOException) { }
                }
                finally { lock (job.Sync) job.Running = false; }
            });
            return Snapshot(job);
        }
    }

    public static string Explain(Exception ex)
    {
        var text = ex.GetBaseException().Message;
        if (ex is OperationCanceledException || text.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase)
            || text.Contains("task was canceled", StringComparison.OrdinalIgnoreCase))
            return "A CompMarket XML letöltése megszakadt, mielőtt a fájl megérkezett. Indítsd újra a letöltést; a folyamatjelző a háttérben fut, és nem a 90 másodperces parancskeret állítja le.";
        if (string.IsNullOrWhiteSpace(ex.Message)) return "A CompMarket XML letöltése ismeretlen hibával leállt.";
        return ex.Message;
    }
}
