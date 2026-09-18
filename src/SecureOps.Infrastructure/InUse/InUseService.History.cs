using System.Security.Claims;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>History uses the current record's trusted identity, never a caller-supplied tenant or hostname.</summary>
    public Task<InUseResult<InUseServerHistory>> HistoryAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, string serverId, string? search, int page, int pageSize, CancellationToken token) =>
        RunAsync(principal, context, Capabilities.InUseReview, async _ =>
        {
            if (page is < 1 or > 100000 || pageSize is < 1 or > 50 || search?.Length > 100 || serverId.Length > 100)
            { return InUseResult<InUseServerHistory>.Fail("InUseInvalid"); }
            InUseRecord? record = await repository.GetAsync(id, token);
            InUseServer? server = record?.Source.Servers.SingleOrDefault(s => s.Id == serverId);
            if (record is null || server is null)
            { return InUseResult<InUseServerHistory>.Fail("InUseNotFound"); }
            string? key = InUseReviewHistory.Identity(record.Source, server);
            (IReadOnlyList<InUseServerReview> rows, int total) = key is null ? (Array.Empty<InUseServerReview>(), 0)
                : await repository.HistoryAsync(key, search, page, pageSize, token);
            return new(new(server, record.LastSeenAt, key, rows, total, page, pageSize,
                rows.Select(r => InUseReviewHistory.Proposal(record, server, r, DateTimeOffset.UtcNow)).ToArray()));
        }, token);

    private async Task<IReadOnlyList<InUseAnswer>?> NormalizeAnswersAsync(InUseRecord old, SaveInUseDraftRequest request,
        ApplicationUser actor, CancellationToken token)
    {
        List<InUseAnswer> result = [];
        foreach (InUseAnswer answer in request.Answers)
        {
            InUseAnswer? previous = old.Draft?.Answers.SingleOrDefault(a => a.ServerId == answer.ServerId && a.Check == answer.Check);
            if (answer.Value == "Unknown" && answer.Origin is null && previous?.Origin is null)
            { result.Add(answer); continue; }
            if (previous?.Value == answer.Value && (answer.Origin is null || answer.Origin == previous.Origin))
            { result.Add(answer with { Origin = previous.Origin }); continue; }
            InUseAnswerOrigin input = answer.Origin ?? new("Individual");
            if (input.Kind == "Bulk")
            {
                if (input.SourceServerId == answer.ServerId || !old.Source.Servers.Any(s => s.Id == input.SourceServerId)
                    || input.CopiedValue != answer.Value
                    || answer.Value is not ("Yes" or "No"))
                { return null; }
            }
            else if (input.Kind == "PreviousReview")
            {
                InUseServerReview? review = input.ReviewId is Guid reviewId ? await repository.ReviewAsync(reviewId, token) : null;
                InUseServer server = old.Source.Servers.Single(s => s.Id == answer.ServerId);
                if (review is null || !InUseReviewHistory.Proposal(old, server, review, DateTimeOffset.UtcNow).CanReuse
                    || !review.Answers.Any(a => a.Check == answer.Check && a.Value == answer.Value && a.Origin?.AcceptedBy is not null)
                    || answer.Value is not ("Yes" or "No"))
                { return null; }
            }
            else if (input.Kind != "Individual")
            { return null; }
            result.Add(answer with
            {
                Origin = new(input.Kind, input.Kind == "Bulk" ? input.SourceServerId : null,
                input.Kind == "PreviousReview" ? input.ReviewId : null, actor.Id, ActorLabel(actor), DateTimeOffset.UtcNow)
                { CopiedValue = input.Kind == "Bulk" ? answer.Value : null }
            });
        }
        return result;
    }
}
