using System.Text.Json;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient : IInUseSourceClient
{
    /// <summary>Reads only In Use category 4241/group 68, using the existing bounded transport/session.</summary>
    public async Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        cancellationToken = deadline.Token;
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
        List<InUseSource> records = [];
        var reporters = new Dictionary<string, InUseRelatedRequestReporter>(StringComparer.Ordinal);
        foreach (OperationalRecordSourceItem item in parsed.Items)
        {
            if (!long.TryParse(item.SourceRecordId, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long id) || id <= 0)
            { throw new InvalidDataException("Exact numeric root identity is required."); }
            InUseSource source = new(item.SourceRecordId, item.OrCode, item.Title,
            new(item.Requester, "TuruncuHat: KEY.p_rel_requester (display only; not an application identity)"),
            new(null, "Unresolved: service-owner response contract"),
            new(null, "Unresolved: provisioning identity contract"), [], "", false);
            try
            {
                using JsonDocument related = await QueryAsync("rel", [$"#%m_tid%#=100049 and #%m_lid%#={id}"],
                    InUseServiceItemParser.ReporterSelects, "in-use-service-items", cancellationToken, 65536);
                source = source with
                {
                    Servers = await EnrichReportersAsync(item.SourceRecordId,
                        InUseServiceItemParser.Parse(related.RootElement, includeRfc: true), reporters, cancellationToken),
                    ServiceItemsState = "Observed",
                    RelationshipEvidence = "TuruncuHat rel m_tid=100049 / m_lid=source OR; exact KEY/SET projection. Observed rows only; completeness unverified."
                };
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or TuruncuHatQueryResultException)
            { source = source with { ServiceItemsState = "Ambiguous", RelationshipEvidence = "Service-item response is missing, malformed or ambiguous; prior evidence retained." }; }
            catch (ExternalIntegrationException ex) when (ex.ErrorCode == SecureOps.Shared.Contracts.Api.OperationalErrorCodes.OperationalSourceAuthenticationFailed)
            { source = source with { ServiceItemsState = "Forbidden", RelationshipEvidence = "Source authentication or relationship access failed; prior evidence retained." }; }
            catch (ExternalIntegrationException)
            { source = source with { ServiceItemsState = "Failed", RelationshipEvidence = "Service-item read failed; prior evidence retained. Check approved source access and transport." }; }
            records.Add(source);
        }
        return new(records, false, "SourceCompletenessUnverified");
    }

    private async Task<IReadOnlyList<InUseServer>> EnrichReportersAsync(string parentId, IReadOnlyList<InUseServer> servers,
        Dictionary<string, InUseRelatedRequestReporter> cache, CancellationToken token)
    {
        List<InUseServer> result = [];
        foreach (InUseServer server in servers)
        {
            string? reference = server.Fields["RFC Kaydı"].Value;
            InUseRelatedRequestReporter reporter = new(parentId, server.Id, reference, "OrCode",
                null, null, null, null, "MissingRfc", "Omitted", "Omitted", null);
            if (server.Fields["RFC Kaydı"].Source.StartsWith("Missing response cell:", StringComparison.Ordinal))
            { reporter = reporter with { State = "Stale" }; }
            if (reference is not null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(reference, @"\AOR-[0-9]{1,20}\z",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                { reporter = reporter with { RfcReference = reference.Length <= 254 ? reference : null, State = "IdentityMismatch" }; }
                else if (cache.TryGetValue(reference, out InUseRelatedRequestReporter? cached))
                { reporter = cached; }
                else if (cache.Count >= 10)
                { reporter = reporter with { State = "NotQueried" }; }
                else
                {
                    try
                    {
                        (reporter, _) = await ReadReferencedRequestAsync(reference, new("c_rfc_record", "SET", "OrCode"), token);
                        if (reporter.State == "ExactMatch" && (reporter.DisplayState == "Omitted" || reporter.ReferenceState == "Omitted"))
                        { reporter = reporter with { State = "Stale", LastVerifiedAt = null }; }
                    }
                    catch (ExternalIntegrationException ex)
                    { reporter = reporter with { State = ex.ErrorCode == SecureOps.Shared.Contracts.Api.OperationalErrorCodes.OperationalSourceAuthenticationFailed ? "Forbidden" : "Failed" }; }
                    catch (Exception ex) when (ex is InvalidDataException or JsonException or TuruncuHatQueryResultException)
                    { reporter = reporter with { State = "AmbiguousMatch" }; }
                    cache.Add(reference, reporter);
                }
            }
            result.Add(server with { RelatedRequestReporter = reporter with { ParentId = parentId, ServiceItemId = server.Id } });
        }
        return result;
    }
}
