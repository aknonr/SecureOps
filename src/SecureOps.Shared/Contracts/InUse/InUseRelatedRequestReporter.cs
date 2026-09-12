namespace SecureOps.Shared.Contracts.InUse;

/// <summary>One exact related-request observation, never server ownership or local assignment.</summary>
public sealed record InUseRelatedRequestReporter(string ParentId, string ServiceItemId, string? RfcReference,
    string ReferenceKind, string? RequestId, string? RequestCode, string? Display, string? UserReference,
    string State, string DisplayState, string ReferenceState, DateTimeOffset? LastVerifiedAt)
{
    /// <summary>Observed business label; Requester compatibility names do not establish Istem Sahibi.</summary>
    public const string Label = "\u0130lgili talebi bildiren";

    /// <summary>RFC-only resolution/freshness wording shared by UI and new workbook evidence.</summary>
    public string StateText(DateTimeOffset now) => EffectiveState(now) switch
    {
        "ExactMatch" => "RFC e\u015fle\u015fti",
        "MissingRfc" => "RFC bo\u015f",
        "Stale" => "G\u00fcncel de\u011fil",
        "NotFoundOrNotVisible" => "Bulunamad\u0131 veya g\u00f6r\u00fcn\u00fcr de\u011fil",
        "AmbiguousMatch" => "Belirsiz e\u015fle\u015fme",
        "IdentityMismatch" => "Kimlik uyu\u015fmazl\u0131\u011f\u0131",
        "Forbidden" => "Eri\u015fim reddedildi",
        "Failed" => "Okuma ba\u015far\u0131s\u0131z",
        _ => "Sorgulanmad\u0131"
    };

    /// <summary>Clock-based freshness never mutates stored evidence or source fingerprints.</summary>
    public string EffectiveState(DateTimeOffset now) => State != "ExactMatch" ? State
        : LastVerifiedAt is null || LastVerifiedAt > now || LastVerifiedAt < now.AddHours(-24) ? "Stale" : State;

    /// <summary>Plain text only; decode raw source display once at presentation, never in persistence.</summary>
    public string DisplayText(DateTimeOffset now) => EffectiveState(now) == "ExactMatch" && DisplayState == "Returned"
        ? InUseDisplayText.Decode(Display) : EffectiveState(now) == "ExactMatch" ? DisplayState : EffectiveState(now);
}
