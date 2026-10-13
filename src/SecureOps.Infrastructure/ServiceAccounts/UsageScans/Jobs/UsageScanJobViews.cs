using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

/// <summary>Builds the API views of a job from its stored record. Pure; every text comes from <see cref="UsageScanJobRules"/>.</summary>
public static class UsageScanJobViews
{
    /// <summary>Most new items returned by one poll.</summary>
    public const int MaxItemsPerPoll = 500;

    /// <summary>The live view. <paramref name="items"/> are the items finished after <paramref name="after"/>.</summary>
    public static UsageScanJobView View(UsageScanJobRecord job, IReadOnlyList<UsageScanJobFoundItem> items, int after, bool moreItems, DateTimeOffset now,
        Guid callerId, bool canCancel, bool canRetry)
    {
        UsageScanJobServerView[] servers = [.. job.Servers.OrderBy(s => s.Ordinal).Select(Server)];
        UsageScanJobCounts counts = Counts(job);
        bool finished = !UsageScanJobRules.IsActive(job.State);
        bool linked = job.Accounts.Any(a => a.LinkOutcome is "Attached" or "AlreadyAttached");
        TimeSpan? remaining = job.State == UsageScanJobState.Running
            ? UsageScanJobRules.EstimateRemaining(job.StartedAt, now, counts.Done, counts.Planned) : null;
        return new UsageScanJobView(job.Id, job.State.ToString(), UsageScanJobRules.StateLabel(job.State), job.Purpose, job.ExpectedAccount, job.Collector,
            job.Collector == "Fake", job.CreatedAt, job.StartedAt, job.FinishedAt, job.CancelRequestedAt is not null, job.FailureCode,
            job.FailureCode is null ? null : UsageScanJobRules.FailureLabel(job.FailureCode), job.RetryOfJobId, job.ScanId, job.StartedBy == callerId,
            [.. job.Accounts.Select(a => new UsageScanJobAccountView(a.AccountId, a.SearchedName, a.LinkOutcome, LinkText(a.LinkOutcome)))],
            servers, counts, remaining is { } r ? (int)Math.Min(r.TotalSeconds, int.MaxValue) : null,
            [.. items.Select(i => new UsageScanJobItemView(i.FinishSeq, i.Item.ServerName, i.Item.Role, i.Item.MatchedAccount, i.Item.ComponentType,
                UsageScanOutcomes.ComponentLabel(i.Item.ComponentType), i.Item.ComponentName, i.Item.Identity, i.Item.State, i.Item.Detail))],
            items.Count == 0 ? after : items.Max(i => i.FinishSeq), moreItems,
            finished ? UsageScanJobRules.Summary(counts.Found, counts.Matches, counts.NotFound, counts.Uncertain, counts.NotCovered) : null,
            UsageScanJobRules.NextStep(job.State, counts.Matches, counts.Retryable, linked),
            new UsageScanJobPermissions(canCancel && !finished, canRetry && finished && counts.Retryable > 0));
    }

    /// <summary>A short line for an account's list of scans.</summary>
    public static UsageScanJobSummaryView Summary(UsageScanJobRecord job, Guid callerId)
    {
        UsageScanJobCounts counts = Counts(job);
        return new UsageScanJobSummaryView(job.Id, job.State.ToString(), UsageScanJobRules.StateLabel(job.State), UsageScanJobRules.IsActive(job.State),
            job.CreatedAt, job.FinishedAt, counts.Planned, counts.Done, counts.Matches, counts.NotCovered, job.Collector == "Fake", job.StartedBy == callerId);
    }

    /// <summary>Aggregates over the planned servers.</summary>
    public static UsageScanJobCounts Counts(UsageScanJobRecord job)
    {
        UsageScanTileKind[] tiles = [.. job.Servers.Select(Tile)];
        return new UsageScanJobCounts(job.Servers.Count,
            job.Servers.Count(s => s.State == UsageScanJobServerState.Pending),
            job.Servers.Count(s => s.State == UsageScanJobServerState.Connecting),
            job.Servers.Count(s => s.State == UsageScanJobServerState.Done),
            tiles.Count(t => t == UsageScanTileKind.Found),
            tiles.Count(t => t == UsageScanTileKind.NotFound),
            tiles.Count(t => t == UsageScanTileKind.Uncertain),
            tiles.Count(t => t == UsageScanTileKind.NotCovered),
            job.Servers.Sum(s => s.FormerMatches ?? 0),
            job.Servers.Count(s => s.State == UsageScanJobServerState.Done && UsageScanJobRules.NeedsRetry(s.Status, s.DocumentResult)));
    }

    /// <summary>Servers a retry repeats, in plan order.</summary>
    public static IReadOnlyList<string> RetryServers(UsageScanJobRecord job) =>
        [.. job.Servers.OrderBy(s => s.Ordinal).Where(s => s.State == UsageScanJobServerState.Done && UsageScanJobRules.NeedsRetry(s.Status, s.DocumentResult))
            .Select(s => s.ServerName)];

    /// <summary>Turkish text of an evidence link outcome.</summary>
    public static string? LinkText(string? outcome) => outcome switch
    {
        null => null,
        "Attached" => "Sonuç bu hesaba bağlandı; hesabın Kullanım taraması bölümünde.",
        "AlreadyAttached" => "Bu sonuç bu hesaba zaten bağlıydı.",
        "Unavailable" => "Bağlanmadı: hesap artık kapsamınızda değil veya bu hesapta sorumlu değilsiniz.",
        "Failed" => "Bağlanamadı (kayıt hatası); taramayı yeniden başlatabilirsiniz.",
        _ => "Bağlanmadı: kanıt yazılmadı (hiçbir sunucu bitmedi veya iş tamamlanamadı)."
    };

    private static UsageScanTileKind Tile(UsageScanJobServer server) =>
        UsageScanJobRules.Tile(server.State, server.Status, server.DocumentResult, server.FormerMatches ?? 0);

    private static UsageScanJobServerView Server(UsageScanJobServer server)
    {
        UsageScanTileKind tile = Tile(server);
        return new UsageScanJobServerView(server.Ordinal, server.ServerName, server.State.ToString(), server.Status?.ToString(), tile.ToString(),
            UsageScanJobRules.TileLabel(tile, server.Status, server.FormerMatches ?? 0), server.FormerMatches ?? 0, server.ExpectedMatches ?? 0, server.ElapsedMs,
            server.DocumentResult?.ToString());
    }
}
