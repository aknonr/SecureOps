using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Per-server legacy intent with explicit provenance. No external operations or inferred owners.</summary>
public sealed class InUsePolicy(IOptions<InUsePolicyOptions> options)
{
    private static readonly string[] _organizationFields = ["COUNTRY", "Department", "Sub_Department", "Contact_email", "ITMC_Event_Owner_Group", "Device_Type"];
    private static readonly string[] _alarmFields = ["check:NmsRequested", "check:MemoryAlarm", "check:CpuAlarm", "check:DiskAlarm"];

    /// <summary>Returns a content-bound proposal, including unresolved fields, for explicit review.</summary>
    public InUsePolicyProposal Propose(InUseRecord record)
    {
        InUsePolicyOptions policy = options.Value;
        if (string.IsNullOrWhiteSpace(policy.Revision) || policy.Revision.Length > 64
            || policy.Proposals.Any(p => !_organizationFields.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value)
                || p.Value.Length > 256 || p.Value.Any(char.IsControl)))
        { throw new InvalidOperationException("Invalid InUsePolicy configuration."); }
        List<InUsePolicyField> fields = [];
        foreach (InUseServer server in record.Source.Servers.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            string environment = server.Fields.GetValueOrDefault("SI_ENVIRONMENT")?.Value?.Trim().ToUpperInvariant() ?? "";
            string? desired = environment switch
            {
                "PROD" => "Evet",
                "DEV" or "TEST" or "UAT" or "NONPROD" or "NON-PROD" or "POC" or "PREPROD" => "Hayır",
                _ => null
            };
            fields.AddRange(_alarmFields.Select(field => new InUsePolicyField(server.Id, field, desired,
                desired is null ? "UnresolvedEnvironment" : "EnvironmentPolicy")));
            fields.Add(new(server.Id, "check:UpDownAlarm", "Evet", "MonitoringProposal"));
            foreach (string field in _organizationFields)
            {
                string? observed = server.Fields.GetValueOrDefault(field)?.Value;
                fields.Add(new(server.Id, field, string.IsNullOrWhiteSpace(observed) ? policy.Proposals.GetValueOrDefault(field) : observed,
                    string.IsNullOrWhiteSpace(observed) ? policy.Proposals.ContainsKey(field) ? "ConfiguredProposal" : "ConfigurationRequired" : "Source"));
            }
        }
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { policy.Revision, record.Source.Id, record.Source.IdentityScope, Fields = fields })));
        return new(policy.Revision, hash, fields);
    }
}
