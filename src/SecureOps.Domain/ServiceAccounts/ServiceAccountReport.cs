namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Scoped account fact used by the single metric implementation.</summary>
/// <param name="Id">Account.</param>
/// <param name="Label">Human-readable account label.</param>
/// <param name="OwnerTeamId">Current confirmed owner team.</param>
/// <param name="ConfirmedPersonId">Current confirmed responsible person.</param>
/// <param name="NamedPersonId">Confirmed person, else the latest proposed person (legacy projection only).</param>
public sealed record AccountFact(Guid Id, string Label, Guid? OwnerTeamId, Guid? ConfirmedPersonId, Guid? NamedPersonId = null);

/// <summary>Scoped request fact.</summary>
/// <param name="Id">Request.</param>
/// <param name="AccountId">Account.</param>
/// <param name="ActionType">Expected action.</param>
/// <param name="Status">Open/closed.</param>
/// <param name="TargetTeamId">Target (counterpart) team.</param>
/// <param name="PlanStart">Plan start.</param>
/// <param name="PlanEnd">Plan end.</param>
/// <param name="FollowupPersonId">Follow-up person; never an owner.</param>
/// <param name="OrderKey">Deterministic ordering key (creation time, legacy row) for the legacy projection.</param>
public sealed record RequestFact(Guid Id, Guid AccountId, ServiceAccountActionType ActionType, ServiceAccountRequestStatus Status,
    Guid? TargetTeamId, DateOnly? PlanStart, DateOnly? PlanEnd, Guid? FollowupPersonId, string OrderKey);

/// <summary>Scoped action fact; one row per action identity.</summary>
/// <param name="Id">Action.</param>
/// <param name="AccountId">Account.</param>
/// <param name="Facts">Rule facts.</param>
/// <param name="Precision">Date precision.</param>
/// <param name="ActualAt">Real instant when known.</param>
/// <param name="PerformerTeamId">Performing team.</param>
public sealed record ActionFact(Guid Id, Guid AccountId, ActionFacts Facts, TimePrecision Precision, DateTimeOffset? ActualAt, Guid? PerformerTeamId);

/// <summary>Distinct communication; links never multiply the mail count.</summary>
/// <param name="Id">Communication.</param>
/// <param name="Direction">Direction.</param>
/// <param name="Kind">Kind.</param>
/// <param name="Precision">Date precision.</param>
/// <param name="OccurredOn">Business date.</param>
/// <param name="OccurredAt">Instant.</param>
/// <param name="AccountLinks">Number of linked accounts in scope.</param>
public sealed record CommunicationFact(Guid Id, CommunicationDirection Direction, CommunicationKind Kind, TimePrecision Precision,
    DateOnly? OccurredOn, DateTimeOffset? OccurredAt, int AccountLinks);

/// <summary>Handover fact.</summary>
/// <param name="AccountId">Account.</param>
/// <param name="Status">Status.</param>
/// <param name="HasAcceptanceEvidence">Accepted with decision date, decider and note.</param>
public sealed record HandoverFact(Guid AccountId, HandoverStatus Status, bool HasAcceptanceEvidence);

/// <summary>gMSA transition fact.</summary>
/// <param name="AccountId">Account.</param>
/// <param name="Suitability">Suitability.</param>
/// <param name="Completed">A valid completed gMSA action exists.</param>
public sealed record TransitionFact(Guid AccountId, GmsaSuitability Suitability, bool Completed);

/// <summary>Finding fact.</summary>
/// <param name="AccountId">Account.</param>
/// <param name="Status">Status.</param>
public sealed record FindingFact(Guid AccountId, FindingStatus Status);

/// <summary>All scoped inputs for one report computation.</summary>
/// <param name="Accounts">Accounts.</param>
/// <param name="Requests">Requests.</param>
/// <param name="Actions">Actions.</param>
/// <param name="Communications">Distinct communications.</param>
/// <param name="Handovers">Handovers.</param>
/// <param name="Transitions">gMSA transitions.</param>
/// <param name="Findings">Findings.</param>
/// <param name="TeamNames">Team labels.</param>
public sealed record ReportFacts(IReadOnlyList<AccountFact> Accounts, IReadOnlyList<RequestFact> Requests, IReadOnlyList<ActionFact> Actions,
    IReadOnlyList<CommunicationFact> Communications, IReadOnlyList<HandoverFact> Handovers, IReadOnlyList<TransitionFact> Transitions,
    IReadOnlyList<FindingFact> Findings, IReadOnlyDictionary<Guid, string> TeamNames);

/// <summary>Reconciled weekly category counts: the parts always add up to the total.</summary>
/// <param name="InPeriod">In the report week.</param>
/// <param name="Earlier">Before the week.</param>
/// <param name="LaterBeforeCutoff">After the week, not after the cut-off.</param>
/// <param name="AfterCutoff">After the cut-off (excluded).</param>
/// <param name="UnknownDate">No real date.</param>
/// <param name="Total">Selected definition total.</param>
public sealed record PlacementCounts(int InPeriod, int Earlier, int LaterBeforeCutoff, int AfterCutoff, int UnknownDate, int Total)
{
    /// <summary>True when the categories reconcile to the total.</summary>
    public bool Reconciles => InPeriod + Earlier + LaterBeforeCutoff + AfterCutoff + UnknownDate == Total;
}

/// <summary>Headline counters.</summary>
public sealed record ReportSummary(
    int UniqueAccounts,
    int AccountsWithOwnerTeam,
    int AccountsWithAssignedPerson,
    int UniqueResponsiblePersons,
    int OpenRequests,
    int DatedOpenPlanRequests,
    int DatedOpenPlanAccounts,
    int AwaitingDateRequests,
    int OverdueRequests,
    int PerformedActionReports,
    int VerifiedClosureAccounts,
    int ValidMails,
    int UndatedMails,
    int DraftMailsExcluded,
    int OpenFindings);

/// <summary>One action line for the weekly detail.</summary>
public sealed record ReportActionLine(string Account, string Action, string Result, DateOnly? Date, string? PerformerTeam);

/// <summary>Weekly movement with reconciliation categories.</summary>
public sealed record WeeklyMovement(PlacementCounts Actions, PlacementCounts VerifiedClosures, PlacementCounts Mails,
    int OutgoingMailsInPeriod, int IncomingMailsInPeriod, IReadOnlyList<ReportActionLine> ActionsInPeriod);

/// <summary>Owner-team and target-team workload are separate relationships.</summary>
public sealed record TeamWorkloadRow(string Team, int OwnedAccounts, int OpenRequestsAsTarget, int OverdueAsTarget);

/// <summary>Dated open plan line.</summary>
public sealed record PlanLine(string Account, string Action, DateOnly Start, DateOnly End, string? TargetTeam, bool Overdue);

/// <summary>Handover and gMSA summary.</summary>
public sealed record HandoverSummary(int Reported, int Accepted, int Rejected, int GmsaTargeted, int GmsaCompleted, int GmsaPending,
    IReadOnlyList<NamedCount> PendingBySuitability);

/// <summary>Label with count.</summary>
public sealed record NamedCount(string Label, int Count);

/// <summary>
/// Labelled legacy projection for reconciliation only: the old combined view filled unassigned accounts
/// with a request follow-up person. That person is not the owner and never feeds the confirmed metric.
/// </summary>
public sealed record LegacyOwnershipProjection(int NamedAccounts, int FollowupFallbackAccounts, int CombinedAccounts, int CombinedPeople, string Label);

/// <summary>Immutable report payload; live views, snapshots, XLSX and PDF all use this one shape.</summary>
public sealed record ServiceAccountReport(
    string MetricDefinitionVersion,
    DateOnly WeekStart,
    DateOnly WeekEndExclusive,
    DateTimeOffset AsOf,
    string ScopeLabel,
    ReportSummary Summary,
    WeeklyMovement Weekly,
    IReadOnlyList<TeamWorkloadRow> TeamWorkload,
    IReadOnlyList<PlanLine> DatedPlans,
    HandoverSummary Handover,
    LegacyOwnershipProjection Legacy,
    IReadOnlyList<string> Notes,
    string Period = ReportPeriods.Week);

/// <summary>Report period kinds. The week stays the default; a month or a custom range uses the same metric rules.</summary>
public static class ReportPeriods
{
    /// <summary>Monday–Sunday week (Europe/Istanbul).</summary>
    public const string Week = "Week";
    /// <summary>Calendar month.</summary>
    public const string Month = "Month";
    /// <summary>Explicit inclusive date range (at most <see cref="MaxCustomDays"/> days).</summary>
    public const string Custom = "Custom";
    /// <summary>Longest custom range.</summary>
    public const int MaxCustomDays = 366;

    /// <summary>Normalizes a period to [start, endExclusive); null when the request is invalid.</summary>
    public static (DateOnly Start, DateOnly EndExclusive)? Resolve(string? period, DateOnly start, DateOnly? endInclusive) => (period ?? Week) switch
    {
        Week when endInclusive is null => (ReportCalendar.WeekStart(start), ReportCalendar.WeekStart(start).AddDays(7)),
        Month when endInclusive is null => (new DateOnly(start.Year, start.Month, 1), new DateOnly(start.Year, start.Month, 1).AddMonths(1)),
        Custom when endInclusive is { } end && end >= start && end.DayNumber - start.DayNumber < MaxCustomDays => (start, end.AddDays(1)),
        _ => null
    };
}
