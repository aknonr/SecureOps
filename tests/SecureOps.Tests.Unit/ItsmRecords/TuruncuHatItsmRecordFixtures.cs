using System.Text.Json;

namespace SecureOps.Tests.Unit.ItsmRecords;

/// <summary>
/// Synthetic Turuncu Hat query responses for the ITSM lookup. Shapes follow the reviewed keyed envelope
/// (ADR-0012, docs/26); every number, id and code is a placeholder and no value comes from a real system.
/// </summary>
internal static class TuruncuHatItsmRecordFixtures
{
    public const string OcoNumber = "OCO-000123";
    public const string OrNumber = "OR-000456";

    public static readonly (string Key, string? Value)[] OcoRow =
    [
        ("SET.id", "700123"),
        ("SET.p_code", OcoNumber),
        ("SET.p_name", "Synthetic change &amp; window &lt;test&gt;"),
        ("SET.p_def_status", "8101"),
        ("SET.p_rel_stage", "8201"),
        ("SET.p_rel_group", "8301"),
        ("SET.p_proposed_start_date_time", "2026-01-05T01:00:00"),
        ("SET.p_proposed_finish_date_time", "2026-01-05T03:00:00"),
        ("num", "1")
    ];

    public static readonly (string Key, string? Value)[] OrRow =
    [
        ("SET.id", "700456"),
        ("SET.p_code", OrNumber),
        ("SET.p_name", "Synthetic request"),
        ("SET.p_rel_group", "8301"),
        ("num", "1")
    ];

    public static string Query(params (string Key, string? Value)[][] rows) => Envelope(rows, maxPages: 1, recordCount: rows.Length);

    public static string Envelope((string Key, string? Value)[][] rows, int maxPages, int recordCount) =>
        JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                ErrorDescription = "",
                ErrorDetails = "",
                ErrorNo = 0,
                TenantId = 1,
                Items = rows.Select(row => row.Select(cell => new { cell.Key, cell.Value }).ToArray()).ToArray(),
                MaxPages = maxPages,
                PageNo = 1,
                RecordCount = recordCount
            }
        });

    public static (string Key, string? Value)[] With(this (string Key, string? Value)[] row, string key, string? value) =>
        [.. row.Select(cell => cell.Key == key ? (key, value) : cell)];

    public static (string Key, string? Value)[] Without(this (string Key, string? Value)[] row, string key) =>
        [.. row.Where(cell => cell.Key != key)];

    public const string ApplicationError =
        """{"QueryResult":{"ErrorDescription":"synthetic failure","ErrorDetails":"","ErrorNo":5,"Items":[]}}""";

    // Legacy positional cells: values without keys must never be mapped by position.
    public const string PositionalRow =
        """{"QueryResult":{"ErrorNo":0,"Items":[[[{"Value":"700123"}],[{"Value":"OCO-000123"}],[{"Value":"title"}]]],"MaxPages":1,"PageNo":1,"RecordCount":1}}""";
}

/// <summary>Synthetic rows for the ADR-0032 tracking and user-match reads; every value is a placeholder.</summary>
internal static class TuruncuHatItsmTrackingFixtures
{
    public const string OrSourceId = "700456";
    public const string UserName = "synthetic.user";

    public static readonly (string Key, string? Value)[] OrHeaderRow =
    [
        ("SET.id", OrSourceId),
        ("SET.m_last_mod_dt", "2026-01-05T02:30:00"),
        ("num", "1")
    ];

    public static (string Key, string? Value)[] Activity(string id, string? created = "2026-01-05T01:00:00", string? completed = null) =>
    [
        ("SET.id", id),
        ("SET.m_group", "8301"),
        ("SET.m_status", "8501"),
        ("SET.m_created_dt", created),
        ("SET.m_completion_date", completed),
        ("num", "1")
    ];

    public static (string Key, string? Value)[] Comment(string id, string? created) =>
    [
        ("SET.id", id),
        ("SET.m_created_dt", created),
        ("num", "1")
    ];

    public static readonly (string Key, string? Value)[] UserRow =
    [
        ("SET.id", "880001"),
        ("SET.m_username", UserName),
        ("SET.p_obs_id", "8601"),
        ("SET.m_rel_home_lbs_id", "8701"),
        ("num", "1")
    ];

    /// <summary>Synthetic registration number as the product stores it (leading zero) and as the source searches it.</summary>
    public const string RegistrationNumber = "0770123";
    public const string SearchedRegistrationNumber = "770123";

    public static readonly (string Key, string? Value)[] RegistrationRow =
    [
        ("SET.id", "880001"),
        ("SET.c_registration_number", SearchedRegistrationNumber),
        ("SET.p_obs_id", "8601"),
        ("SET.m_rel_home_lbs_id", "8701"),
        ("num", "1")
    ];

    /// <summary>Vendor-style envelope: 0-based PageNo, MaxPages 0 on a single page, RecordCount the total.</summary>
    public static string VendorEnvelope(int pageNo, int maxPages, int recordCount, params (string Key, string? Value)[][] rows) =>
        JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                ErrorDescription = "",
                ErrorDetails = "",
                ErrorNo = 0,
                TenantId = 1,
                Items = rows.Select(row => row.Select(cell => new { cell.Key, cell.Value }).ToArray()).ToArray(),
                MaxPages = maxPages,
                PageNo = pageNo,
                RecordCount = recordCount
            }
        });
}
