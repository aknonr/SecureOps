using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>Existing account state relevant to import planning.</summary>
public sealed record ContextAccount(Guid Id, string IdentityKey, string NameKey, string? DomainKey, string Name, Guid? OrganizationId,
    Guid? OwnerTeamId, Guid? OwnerPersonId, Guid? ConsumerTeamId, string? Notes, string RowVersion, DateOnly? LastObservedOn,
    IReadOnlyCollection<Guid> OpenRequestTeams, IReadOnlyCollection<Guid> HandoverTeams)
{
    /// <summary>Scope anchor for policy checks.</summary>
    public AccountScopeAnchor Anchor => new(OrganizationId, OwnerTeamId, OpenRequestTeams, HandoverTeams);
}

/// <summary>Dictionary entry (team or organization).</summary>
public sealed record ContextNamed(Guid Id, string Key, string Name, Guid? ParentId);

/// <summary>Person reference with exact alias keys.</summary>
public sealed record ContextPerson(Guid Id, string Key, string Name, IReadOnlyCollection<string> AliasKeys, bool Verified);

/// <summary>Latest observation of an account for one source profile.</summary>
public sealed record ContextObservation(Guid AccountId, string Profile, DateOnly? SourceReportDate, string Presence,
    IReadOnlyDictionary<string, string?> Values);

/// <summary>Existing communication identified by a legacy reference.</summary>
public sealed record ContextCommunication(Guid Id, IReadOnlyCollection<Guid> Accounts);

/// <summary>
/// Current database state used to plan (preview) and re-plan (commit) one import batch. SQL teams and the gMSA executing
/// team come from the module's team roles (SA-002); without an executing team no gMSA routing is planned.
/// </summary>
public sealed record ImportContext(
    IReadOnlyList<ContextAccount> Accounts,
    IReadOnlyList<ContextNamed> Teams,
    IReadOnlyList<ContextNamed> Organizations,
    IReadOnlyList<ContextPerson> People,
    IReadOnlySet<string> LegacyReferences,
    IReadOnlySet<string> SourceKeys,
    IReadOnlyDictionary<string, ContextCommunication> CommunicationsByLegacy,
    IReadOnlyList<ContextObservation> LatestObservations,
    IReadOnlySet<Guid> AccountsWithGmsaTransition,
    ServiceAccountScope Scope,
    IReadOnlySet<Guid>? SqlTeams = null,
    Guid? GmsaExecutorTeamId = null);
