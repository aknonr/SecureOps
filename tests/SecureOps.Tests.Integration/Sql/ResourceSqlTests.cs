using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [LocalResourceSqlFact]
    public async Task Catalogue_RoundTripConcurrencyOwnershipVisibilityAndSafeAudit()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlResourceRepository(configuration);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        ResourceActor other = await CreateActorAsync(connection);
        var categoryId = Guid.NewGuid();
        ResourceCategory category = (await repository.SaveCategoryAsync(categoryId, new("Synthetic SQL category"), actor, _token)).Value!;
        var linkId = Guid.NewGuid();
        var request = new SaveResourceLinkRequest(categoryId, "Synthetic SQL link", "https://example.invalid/sql?view=summary", "Synthetic purpose", Tags: ["sql"]);
        ResourceLink link = (await repository.SaveLinkAsync(linkId, request, actor, _token)).Value!;
        (await repository.GetAsync(linkId, false, false, _token)).Should().BeEquivalentTo(link);
        ResourcePage page = await repository.QueryAsync(new(CategoryId: categoryId, Tag: "SQL", Search: "synthetic"), false, _token);
        page.Total.Should().Be(1);
        page.Items.Single().Should().BeEquivalentTo(link);

        ResourceResult<ResourceLink>[] edits = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.SaveLinkAsync(linkId,
            request with { ExpectedVersion = 1, Notes = "Changed" }, actor, _token)));
        edits.Count(r => r.IsSuccess).Should().Be(1);
        edits.Count(r => r.ErrorCode == ResourceErrors.Conflict).Should().Be(3);

        var setId = Guid.NewGuid();
        var personal = new ResourcePreferences(0, [linkId], [new(setId, "Synthetic start", [linkId])], setId);
        ResourcePreferences stored = (await repository.SavePreferencesAsync(personal, 0, actor, _token)).Value!;
        (await repository.PreferencesAsync(actor.UserId, _token)).Should().BeEquivalentTo(stored);
        (await repository.PreferencesAsync(other.UserId, _token)).Should().BeEquivalentTo(ResourcePreferences.Empty);
        (await repository.SavePreferencesAsync(personal, 0, actor, _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        await repository.SaveLinkAsync(linkId, request with { ExpectedVersion = 2, Archived = true }, actor, _token);
        (await repository.ResolveAsync([linkId], false, _token)).Should().BeEmpty();
        (await repository.GetAsync(linkId, true, true, _token)).Should().NotBeNull();
        await repository.SaveCategoryAsync(categoryId, new("Synthetic SQL category", ManagersOnly: true, ExpectedVersion: category.Version), actor, _token);
        (await repository.GetAsync(linkId, false, true, _token)).Should().BeNull();
        string[] audits = [.. (await connection.QueryAsync<string>("SELECT DetailsJson FROM audit.AuditLog WHERE Actor = @Actor", new { Actor = actor.UserId.ToString("D") }))];
        audits.Should().HaveCount(6);
        foreach (string audit in audits)
        {
            audit.Should().NotContain("example.invalid").And.NotContain("Synthetic").And.NotContain("view=");
        }
    }

    [LocalResourceSqlFact]
    public async Task SqlAuditFailure_RollsBackCatalogueMutation()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlResourceRepository(configuration);
        var id = Guid.NewGuid();
        string trigger = "TR_ResourceTest_" + Guid.NewGuid().ToString("N");
        // Only a new test-owned trigger in the guarded disposable database is created/dropped.
        await connection.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{actor.UserId:D}') THROW 51090, 'Synthetic audit failure.', 1; END;");
        try
        {
            Func<Task> save = () => repository.SaveCategoryAsync(id, new("Must roll back"), actor, _token);
            await save.Should().ThrowAsync<SqlException>();
            (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM resources.Categories WHERE Id = @Id", new { Id = id })).Should().Be(0);
            (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @Actor", new { Actor = actor.UserId.ToString("D") })).Should().Be(0);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];");
        }
    }

    [LocalResourceSqlFact]
    public async Task SdmEvaluation_RoundTripsAndUnchangedInputDoesNotDuplicateHistory()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlOperationalRecordRepository(configuration);
        var source = new OperationalRecordSourceItem(Guid.NewGuid().ToString("N"), "OR-123", "Synthetic upgrade check",
            "Synthetic description", null, null, null, null, null);
        OperationalRecord record = await repository.UpsertImportedAsync(source, "synthetic-sdm", _token);
        SdmEvaluationInput input = SdmEvaluationEvidence.FromSource(source, true, true);
        var context = new OperationalRecordCommandContext("synthetic", "synthetic-sdm", null);
        OperationalRecord evaluated = await repository.EvaluateAsync(record.Id, input, context, new InMemoryAuditWriter(), _token);
        evaluated.SdmEvaluation.Should().NotBeNull();
        evaluated.JiraEligible.Should().BeFalse();
        evaluated.Classification.Should().Be(OperationalRecordClassification.NeedsManualReview);
        (await repository.GetAsync(record.Id, _token))!.SdmEvaluation.Should().BeEquivalentTo(evaluated.SdmEvaluation);
        await repository.EvaluateAsync(record.Id, input, context, new InMemoryAuditWriter(), _token);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId = @Id AND SdmEvaluationJson IS NOT NULL", new { record.Id })).Should().Be(1);
        OperationalRecordSourceItem changed = source with { Title = "Synthetic changed" };
        await repository.UpsertImportedAsync(changed, "synthetic-sdm", _token);
        OperationalRecord stale = await repository.EvaluateAsync(record.Id, SdmEvaluationEvidence.FromSource(changed, true, true), context, new InMemoryAuditWriter(), _token);
        stale.SdmEvaluation!.Result.SourceChanged.Should().BeTrue();
        stale.SdmEvaluation.Result.EvaluationStale.Should().BeTrue();
        // Trusted synthetic attestations exercise persistence, not corporate mapping acceptance.
        SdmEvaluationInput approved = SdmEvaluationEvidence.FromSource(changed, true, true) with
        {
            ValidId = true,
            GroupInScope = true,
            DccAllowed = true,
            Category = OperationalRecordClassification.ServerRequest,
            RequesterPresent = true,
            RequesterResolved = true,
            ReporterResolved = true,
            ApprovalGranted = true,
            PolicyApproved = true,
            MappingComplete = true
        };
        string actor = "synthetic-pilot-" + Guid.NewGuid().ToString("N");
        context = context with { Actor = actor };
        string trigger = "TR_PilotTest_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{actor}') THROW 51090, 'Synthetic audit failure.', 1; END;");
        try
        {
            Func<Task> evaluate = () => repository.EvaluateAsync(record.Id, approved, context, new InMemoryAuditWriter(), _token);
            await evaluate.Should().ThrowAsync<SqlException>();
            (await repository.GetAsync(record.Id, _token))!.Should().BeEquivalentTo(stale);
        }
        finally { await connection.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
        OperationalRecord positive = await repository.EvaluateAsync(record.Id, approved, context, new InMemoryAuditWriter(), _token);
        OperationalRecord persisted = (await new SqlOperationalRecordRepository(configuration).GetAsync(record.Id, _token))!;
        persisted.JiraEligible.Should().BeTrue();
        persisted.SdmEvaluation!.Result.JiraEligible.Should().BeTrue();
        persisted.SdmEvaluation.Result.ExternalWriteEligible.Should().BeFalse();
        persisted.Version.Should().Be(positive.Version);
        Func<Task> wrongPolicy = () => connection.ExecuteAsync("UPDATE ops.OperationalRecords SET SdmEvaluationJson=JSON_MODIFY(SdmEvaluationJson,'$.Result.RuleSetVersion','unapproved-version') WHERE OperationalRecordId=@Id", new { record.Id });
        await wrongPolicy.Should().ThrowAsync<SqlException>();
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor=@Actor AND Action=@Action", new { Actor = actor, Action = SdmEvaluationEvidence.AuditAction })).Should().Be(1);
        await repository.UpsertImportedAsync(changed with { Description = "Synthetic changed after approval" }, "synthetic-sdm", _token);
        SdmEvaluationEvidence.Project((await repository.GetAsync(record.Id, _token))!).JiraEligible.Should().BeFalse();
    }

    [LocalResourceSqlFact]
    public async Task Constraints_RejectInvalidPreferencesAndPreserveAppendOnlyAudit()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        Func<Task> invalid = () => connection.ExecuteAsync("INSERT INTO resources.PersonalPreferences(UserId, Version, PreferencesJson, UpdatedAt) VALUES(@Id,1,N'{}',SYSUTCDATETIME())", new { Id = actor.UserId });
        await invalid.Should().ThrowAsync<SqlException>();
        await connection.ExecuteAsync("INSERT INTO audit.AuditLog(OccurredAt,Actor,Action) VALUES(SYSUTCDATETIME(),@Actor,'SyntheticResourceTest')", new { Actor = actor.UserId.ToString("D") });
        Func<Task> overwrite = () => connection.ExecuteAsync("UPDATE audit.AuditLog SET Action='NotPermitted' WHERE Actor=@Actor", new { Actor = actor.UserId.ToString("D") });
        await overwrite.Should().ThrowAsync<SqlException>();
        (await connection.ExecuteScalarAsync<string>("SELECT Action FROM audit.AuditLog WHERE Actor=@Actor", new { Actor = actor.UserId.ToString("D") })).Should().Be("SyntheticResourceTest");
    }

    private static async Task<ResourceActor> CreateActorAsync(SqlConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus) VALUES(@Id,@Identity,'test','Approved')", new { Id = id, Identity = "synthetic:resource:" + id.ToString("N") });
        return new(id, "synthetic-resource-sql");
    }

    private static IConfiguration Configuration()
    {
        string value = Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION") ?? throw new InvalidOperationException("Explicit disposable SQL connection required.");
        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.DataSource, "(localdb)\\SecureOpsResourcesV1", StringComparison.OrdinalIgnoreCase)
            || !builder.InitialCatalog.StartsWith("SecureOps_ResourcesV1_", StringComparison.Ordinal)
            || !builder.IntegratedSecurity || builder.UserID.Length != 0 || builder.Password.Length != 0)
        {
            throw new InvalidOperationException("Only the isolated Resource V1 LocalDB test facility is permitted.");
        }
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = value }).Build();
    }
}

public sealed class LocalResourceSqlFactAttribute : FactAttribute
{
    public LocalResourceSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION")))
        {
            Skip = "NOT RUN: explicit isolated LocalDB connection is unavailable.";
        }
    }
}
