using System.Text.Json;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient : IInUseSourceClient
{
    /// <summary>Reads only In Use category 4241/group 68, using the existing bounded transport/session.</summary>
    public async Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken)
    {
        using JsonDocument response = await QueryAsync("SMSS_oRFF",
            ["#%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)"],
            _sourceSelects, "in-use-query", cancellationToken);
        ParsedSourceRecords parsed;
        try
        {
            parsed = TuruncuHatQueryParser.ParseSource(response.RootElement, _options.MaxDescriptionLength, _sourceSelects, requireSemanticKeys: true);
        }
        catch (TuruncuHatQueryResultException)
        {
            throw new InvalidDataException("In Use query reported an application failure.");
        }
        if (parsed.MalformedCount != 0 || parsed.Items.Count > 100)
        {
            throw new InvalidDataException("In Use batch is malformed or exceeds the local bound.");
        }
        // Request joins are known from the script; expanded response keys and completeness are not.
        // Do not use positions, query guessed joins, or infer identity from display names.
        return new(parsed.Items.Select(item => new InUseSource(item.SourceRecordId, item.OrCode, item.Title,
            new(item.Requester, "TuruncuHat: KEY.p_rel_requester (display only; not an application identity)"),
            new(null, "Unresolved: service-owner response contract"),
            new(null, "Unresolved: provisioning identity contract"), [],
            "Unresolved: rel m_tid=100049 request is known; expanded response-key and completeness evidence is required.", false)).ToArray(),
            false, "SourceCompletenessUnverified");
    }
}
