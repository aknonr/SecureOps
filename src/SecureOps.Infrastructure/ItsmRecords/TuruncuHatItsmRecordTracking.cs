using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Read-only Turuncu Hat adapter for <see cref="IItsmRecordTracking"/> (ADR-0032). One OR is read with up to three
/// <c>/query</c> calls on the shared query path: the OR row (last update, configured status and stage fields), its
/// workflow activities, and its comments reduced to a count and the newest creation time. Every part is mapped
/// independently, so one failed part is Unknown without hiding the others. It never selects person fields, comment
/// text or the completing person, and never logs the session, credentials, base URL, record id or any value read.
/// </summary>
public sealed class TuruncuHatItsmRecordTracking(
    HttpClient httpClient,
    ITuruncuHatSessionManager sessions,
    IOptions<TuruncuHatOptions> turuncuHat,
    IOptions<ItsmRecordLookupOptions> lookup,
    EnterpriseIntegrationTelemetry telemetry,
    ILogger<TuruncuHatItsmRecordTracking> logger) : IItsmRecordTracking
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "TuruncuHat";

    /// <summary>Most activities returned; more is an incomplete answer, never a silent cut.</summary>
    public const int MaxActivities = 50;

    private const string _lastModifiedField = "m_last_mod_dt";
    private const string _activityObject = "BPM_Actvty";
    private const string _commentObject = "cmn_comment";
    private const int _maxTextLength = 128;

    // m_done_by is a person field and is deliberately absent from the activity selects.
    private static readonly string[] _activitySelects = ["id", "m_group", "m_status", "m_created_dt", "m_completion_date"];
    private static readonly string[] _activityOptional = ["m_group", "m_completion_date"];

    private readonly TuruncuHatItsmQueryClient _query = new(httpClient, sessions, turuncuHat.Value, logger);

    /// <inheritdoc />
    public async Task<ItsmRecordTrackingResult> GetOrTrackingAsync(string? sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? id = ItsmSourceId.Normalize(sourceId);
        if (id is null)
        {
            return ItsmRecordTrackingResult.Unknown(null, ItsmRecordLookupReason.InvalidIdentifier, ProviderName);
        }

        ItsmRecordLookupOptions options = lookup.Value;
        if (!TuruncuHatItsmQueryClient.IsSafeIdentifier(options.OrBaseObject))
        {
            return Finish("itsm-tracking-or",
                ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.NotConfigured, ProviderName), Stopwatch.StartNew());
        }

        var stopwatch = Stopwatch.StartNew();
        ItsmRecordTrackingResult header;
        try
        {
            header = await ReadHeaderAsync(id, options, cancellationToken);
        }
        catch (ItsmQueryFailure failure)
        {
            header = ItsmRecordTrackingResult.Unknown(id, failure.Reason, ProviderName);
        }

        header = Finish("itsm-tracking-or", header, stopwatch);
        if (header.Status != ItsmRecordLookupStatus.Found)
        {
            return header;
        }

        stopwatch = Stopwatch.StartNew();
        ItsmTrackingPart<IReadOnlyList<ItsmActivity>> activities = Finish("itsm-tracking-activities", stopwatch,
            await ReadActivitiesAsync(id, options, cancellationToken));
        stopwatch = Stopwatch.StartNew();
        ItsmTrackingPart<ItsmCommentSummary> comments = Finish("itsm-tracking-comments", stopwatch,
            await ReadCommentsAsync(id, options, cancellationToken));
        return header with { Activities = activities, Comments = comments };
    }

    private async Task<ItsmRecordTrackingResult> ReadHeaderAsync(string id, ItsmRecordLookupOptions options, CancellationToken cancellationToken)
    {
        string? statusField = ConfiguredField(options.OrStatusField);
        string? stageField = ConfiguredField(options.OrStageField);
        if ((!string.IsNullOrEmpty(options.OrStatusField) && statusField is null) || (!string.IsNullOrEmpty(options.OrStageField) && stageField is null))
        {
            return ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.NotConfigured, ProviderName);
        }

        string[] selects = [.. new[] { "id", _lastModifiedField, statusField, stageField }.OfType<string>().Distinct(StringComparer.Ordinal)];
        string[] optional = [.. selects.Where(select => select != "id")];
        string[] filters = [string.Concat("#%m_active%#='True' AND #%id%#=", id)];
        TuruncuHatItsmRecordRows? rows = await _query.QueryAsync(options.OrBaseObject, filters, selects, optional, cancellationToken);
        if (rows is null)
        {
            return ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.AuthenticationFailed, ProviderName);
        }

        if (!rows.Complete || rows.Malformed > 0)
        {
            return ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.IncompleteResponse, ProviderName);
        }

        foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
        {
            // A row for another id means the filter was not honoured; nothing in this response is trusted.
            if (!string.Equals(row["SET.id"]?.Trim(), id, StringComparison.Ordinal))
            {
                return ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.InvalidResponse, ProviderName);
            }
        }

        if (rows.Rows.Count == 0)
        {
            return ItsmRecordTrackingResult.NotFound(id, ProviderName);
        }

        if (rows.Rows.Count > 1)
        {
            return ItsmRecordTrackingResult.Ambiguous(id, ProviderName);
        }

        IReadOnlyDictionary<string, string?> single = rows.Rows[0];
        if (!TryText(single, "SET." + _lastModifiedField, out string? modified)
            || !TryText(single, statusField is null ? null : "SET." + statusField, out string? status)
            || !TryText(single, stageField is null ? null : "SET." + stageField, out string? stage))
        {
            return ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.InvalidResponse, ProviderName);
        }

        return ItsmRecordTrackingResult.Found(id, new ItsmOrState(modified, status, stage),
            ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.None),
            ItsmTrackingPart<ItsmCommentSummary>.Unknown(ItsmRecordLookupReason.None), ProviderName);
    }

    private async Task<ItsmTrackingPart<IReadOnlyList<ItsmActivity>>> ReadActivitiesAsync(
        string id, ItsmRecordLookupOptions options, CancellationToken cancellationToken)
    {
        string? mainObject = string.IsNullOrEmpty(options.ActivityMainObject) ? null : options.ActivityMainObject;
        if (mainObject is not null && !TuruncuHatItsmQueryClient.IsSafeIdentifier(mainObject))
        {
            return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.NotConfigured);
        }

        string filter = "#%m_bpe_actvty_task_model.m_action_type.m_code%#!='WorkflowSystemAction' AND #%m_process.m_main_object_id%#=" + id
            + (mainObject is null ? string.Empty : " AND #%m_process.m_main_object%#=" + FilterToken(mainObject));
        try
        {
            TuruncuHatItsmRecordRows? rows = await _query.QueryAsync(_activityObject, [filter], _activitySelects, _activityOptional, cancellationToken);
            if (rows is null)
            {
                return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.AuthenticationFailed);
            }

            if (!rows.Complete || rows.Malformed > 0 || rows.Rows.Count > MaxActivities)
            {
                return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.IncompleteResponse);
            }

            List<(ulong Order, ItsmActivity Activity)> activities = new(rows.Rows.Count);
            foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
            {
                string? activityId = ItsmSourceId.Normalize(row["SET.id"]);
                if (activityId is null || !ulong.TryParse(activityId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong order)
                    || !TryText(row, "SET.m_group", out string? group)
                    || !TryText(row, "SET.m_status", out string? status)
                    || !TryText(row, "SET.m_created_dt", out string? created)
                    || !TryText(row, "SET.m_completion_date", out string? completed))
                {
                    return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.InvalidResponse);
                }

                activities.Add((order, new ItsmActivity(activityId, group, status, created, completed)));
            }

            return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Known(
                [.. activities.OrderBy(entry => entry.Order).Select(entry => entry.Activity)]);
        }
        catch (ItsmQueryFailure failure)
        {
            return ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(failure.Reason);
        }
    }

    private async Task<ItsmTrackingPart<ItsmCommentSummary>> ReadCommentsAsync(
        string id, ItsmRecordLookupOptions options, CancellationToken cancellationToken)
    {
        if (!TuruncuHatItsmQueryClient.IsSafeIdentifier(options.CommentMainObject)
            || !TuruncuHatItsmQueryClient.IsSafeIdentifier(options.CommentTimeField))
        {
            return ItsmTrackingPart<ItsmCommentSummary>.Unknown(ItsmRecordLookupReason.NotConfigured);
        }

        string field = options.CommentTimeField;
        string[] selects = [.. new[] { "id", field }.Distinct(StringComparer.Ordinal)];
        string filter = "#%m_active%#='True' AND #%m_mob%#=" + FilterToken(options.CommentMainObject) + " AND #%m_mid%#=" + id;
        try
        {
            TuruncuHatItsmRecordRows? rows = await _query.QueryAsync(
                _commentObject, [filter], selects, [.. selects.Where(select => select != "id")], cancellationToken);
            if (rows is null)
            {
                return ItsmTrackingPart<ItsmCommentSummary>.Unknown(ItsmRecordLookupReason.AuthenticationFailed);
            }

            if (!rows.Complete || rows.Malformed > 0)
            {
                return ItsmTrackingPart<ItsmCommentSummary>.Unknown(ItsmRecordLookupReason.IncompleteResponse);
            }

            // The highest comment id is the newest comment (Turuncu Hat team); times are never compared as text.
            ulong newest = 0;
            string? newestTime = null;
            foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
            {
                string? commentId = ItsmSourceId.Normalize(row["SET.id"]);
                if (commentId is null || !ulong.TryParse(commentId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong order)
                    || !TryText(row, "SET." + field, out string? time))
                {
                    return ItsmTrackingPart<ItsmCommentSummary>.Unknown(ItsmRecordLookupReason.InvalidResponse);
                }

                if (order > newest)
                {
                    newest = order;
                    newestTime = time;
                }
            }

            return ItsmTrackingPart<ItsmCommentSummary>.Known(new ItsmCommentSummary(rows.Rows.Count, newestTime));
        }
        catch (ItsmQueryFailure failure)
        {
            return ItsmTrackingPart<ItsmCommentSummary>.Unknown(failure.Reason);
        }
    }

    // A raw text cell is bounded; an oversized value is an invalid answer, not a truncated one.
    private static bool TryText(IReadOnlyDictionary<string, string?> row, string? key, out string? value)
    {
        value = null;
        if (key is null)
        {
            return true;
        }

        string? text = row.TryGetValue(key, out string? raw) ? raw?.Trim() : null;
        if (text is { Length: > _maxTextLength })
        {
            return false;
        }

        value = string.IsNullOrEmpty(text) ? null : text;
        return true;
    }

    private static string? ConfiguredField(string? name) =>
        TuruncuHatItsmQueryClient.IsSafeIdentifier(name) ? name : null;

    // Numeric object ids are sent as numbers like the other readers; names are quoted. Both pass the identifier allow-list.
    private static string FilterToken(string value) => value.All(char.IsAsciiDigit) ? value : "'" + value + "'";

    private ItsmRecordTrackingResult Finish(string operation, ItsmRecordTrackingResult result, Stopwatch stopwatch)
    {
        string outcome = result.Status == ItsmRecordLookupStatus.Unknown
            ? "unknown-" + result.Reason.ToString().ToLowerInvariant()
            : result.Status.ToString().ToLowerInvariant();
        telemetry.RecordOperation(ProviderName, operation, outcome, stopwatch.Elapsed);
        if (result.Status == ItsmRecordLookupStatus.Unknown)
        {
            logger.LogWarning("Turuncu Hat ITSM tracking could not prove an answer. Reason: {Reason}.", result.Reason);
        }

        return result;
    }

    private ItsmTrackingPart<T> Finish<T>(string operation, Stopwatch stopwatch, ItsmTrackingPart<T> part)
    {
        telemetry.RecordOperation(ProviderName, operation,
            part.State == ItsmTrackingPartState.Known ? "known" : "unknown-" + part.Reason.ToString().ToLowerInvariant(),
            stopwatch.Elapsed);
        if (part.State == ItsmTrackingPartState.Unknown)
        {
            logger.LogWarning("Turuncu Hat ITSM tracking part could not be proven. Part: {Part}. Reason: {Reason}.",
                operation, part.Reason);
        }

        return part;
    }
}
