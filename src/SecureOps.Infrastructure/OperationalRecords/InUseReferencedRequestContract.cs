using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Operator-supplied source-owner contract for one diagnostic RFC hop, never runtime mapping approval.</summary>
public sealed record InUseReferencedRequestContract(string RfcProperty, string ReferenceCellKind, string ReferenceKind, string? ReporterProperty = null)
{
    /// <summary>Rejects incomplete representation templates before any transport call.</summary>
    public void Validate(IReadOnlyDictionary<string, string> dictionary)
    {
        if (!Direct(RfcProperty) || ReporterProperty is not (null or "p_rel_requester")
            || ReferenceCellKind is not ("SET" or "KEY") || ReferenceKind is not ("SourceId" or "OrCode")
            || !dictionary.TryGetValue("RFC Kaydı", out string? rfc) || rfc != RfcProperty)
        { throw new InvalidDataException("Verified RFC representation required; the observed reporter selector is p_rel_requester."); }
    }

    private static bool Direct(string? value) => value is not null
        && Regex.IsMatch(value, @"\A(?:p_|c_)[A-Za-z0-9_]{1,100}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && !new[] { "password", "token", "session", "secret", "authorization" }.Any(s => value.Contains(s, StringComparison.OrdinalIgnoreCase));
}
