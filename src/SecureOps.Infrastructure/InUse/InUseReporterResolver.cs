using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Exact reviewed crosswalk over persisted RFC evidence. Never queries by a person's display name.</summary>
public sealed class InUseReporterResolver(IOptions<InUseReporterMappingOptions> options, IAccessRepository users)
{
    /// <summary>Resolves current eligibility; every caller still requires record/assignment authorization.</summary>
    public async Task<InUseReporterSuggestion> ResolveAsync(InUseRecord record, DateTimeOffset now, CancellationToken token)
    {
        InUseRelatedRequestReporter[] relations = record.Source.Servers.Select(s => s.RelatedRequestReporter)
            .OfType<InUseRelatedRequestReporter>().ToArray();
        InUseReporterSuggestion Result(string state, InUseAssignee? candidate = null, InUseReporterIdentityLink? link = null)
        {
            string? fingerprint = link is null ? null : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { record.Id, record.Version, record.SourceVersion, record.SourceHash, record.Source.IdentityScope, relations, options.Value.Revision, link })));
            return new(state, relations.Length == 0 ? null : InUseDisplayText.Decode(relations[0].Display), candidate,
                record.Version, record.SourceVersion, fingerprint, link is null ? null : options.Value.Revision, link?.ReviewReference, relations)
            { IdentityScope = record.Source.IdentityScope, MappingValidUntil = link?.ValidUntil };
        }
        if (relations.Length == 0 || string.IsNullOrWhiteSpace(record.Source.IdentityScope))
        { return Result("ReporterUnverified"); }
        if (relations.Length != record.Source.Servers.Count || relations.Any(r => r.EffectiveState(now) != "ExactMatch"
            || r.ParentId != record.Source.Id || r.ReferenceState != "Returned" || string.IsNullOrWhiteSpace(r.UserReference)
            || string.IsNullOrWhiteSpace(r.RequestId) || string.IsNullOrWhiteSpace(r.RfcReference))
            || record.Source.Servers.Any(s => s.RelatedRequestReporter?.ServiceItemId != s.Id))
        { return Result("ReporterUnverified"); }
        if (relations.Select(r => r.UserReference).Distinct(StringComparer.Ordinal).Count() != 1)
        { return Result("Ambiguous"); }
        InUseReporterMappingOptions config = options.Value;
        if (string.IsNullOrWhiteSpace(config.Revision) || config.Revision.Length > 100 || config.Links is null
            || config.Links.Length is 0 or > 1000 || config.Links.Any(l => l is null))
        { return Result("MappingNotConfigured"); }
        InUseReporterIdentityLink[] links = config.Links.Where(l => l.IdentityScope == record.Source.IdentityScope
            && l.UserReference == relations[0].UserReference).ToArray();
        if (links.Length == 0)
        { return Result("NoMatch"); }
        if (links.Length != 1)
        { return Result("Ambiguous"); }
        InUseReporterIdentityLink link = links[0];
        if (link.ApplicationUserId == Guid.Empty || string.IsNullOrWhiteSpace(link.ReviewReference)
            || link.ReviewReference.Length > 200 || link.ValidUntil <= now)
        { return Result("MappingUnverified"); }
        ApplicationUser? user = await users.GetUserAsync(link.ApplicationUserId, token);
        if (user is null)
        { return Result("NoMatch"); }
        if (user.Status != AccessStatus.Approved || !user.Capabilities.Contains(Capabilities.InUseView)
            || !user.Capabilities.Contains(Capabilities.InUseReview))
        { return Result("Ineligible"); }
        string label = (string.IsNullOrWhiteSpace(user.DisplayName) ? user.LoginName ?? "Registered reviewer" : user.DisplayName)
            + " \u00b7 " + user.Id.ToString("N")[..8];
        return Result("Matched", new(user.Id, label), link);
    }
}
