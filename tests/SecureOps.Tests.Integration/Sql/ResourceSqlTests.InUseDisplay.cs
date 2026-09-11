using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUse_DisplayAcceptance_RetainsEncodedSnapshotsAndTrustedProfiles()
    {
        IConfiguration configuration = Configuration();
        await using var connection = new SqlConnection(configuration.GetConnectionString("SecureOpsDb"));
        Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(connection);
        var users = new SqlAccessRepository(configuration);
        ApplicationUser? first = null;
        for (int i = 0; i < 4; i++)
        {
            var principal = new CorporatePrincipal("oidc:synthetic-display-" + Guid.NewGuid().ToString("N"), "oidc",
                DisplayName: i < 2 ? "Deniz Örnek" : null);
            EnsureAccessUserResult pending = await users.EnsureUserAsync(principal, true, TimeSpan.Zero, _token);
            AccessMutationResult approved = await users.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved,
                pending.PendingRequest.Version, actor.UserId.ToString("D"), ["InUseReviewer"], "Synthetic display fixture", _token);
            first ??= approved.User;
        }
        var repository = new SqlInUseRepository(configuration);
        InUseSource seed = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        foreach (int count in new[] { 2, 4 })
        {
            InUseSource source = seed with
            {
                Id = "display-" + count,
                Code = "OR-DISPLAY-" + count,
                Title = "Görüntü ve kontrol sunucularının kullanıma alınması: farklı ortamlar ve uygulamalar için uzun sentetik kabul başlığı",
                Requester = new("ENVANTER KONTROL · G&#246;zlem &amp; Y&#246;netim", "TuruncuHat: KEY.p_rel_requester"),
                ServiceItemsState = count == 4 ? "Observed" : "Partial",
                Creator = null,
                Servers = Enumerable.Range(1, count).Select(i => new InUseServer((7000 + i).ToString(), new Dictionary<string, InUseEvidence>
                {
                    ["HOSTNAME"] = new("synthetic-display-" + i, "TuruncuHat: SET.p_name"),
                    ["SI_ENVIRONMENT"] = new(i == 1 ? "TEST" : "PROD", "TuruncuHat: KEY.environment"),
                    ["SERVICE NAME (ÜRÜN/UYGULAMA)"] = new(i == 1 ? "G&#246;r&#252;nt&#252; &amp; Kontrol" : "&lt;script&gt;alert(1)&lt;/script&gt;", "TuruncuHat: KEY.service")
                })).ToArray()
            };
            var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "InUseDisplayFixture" };
            await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([source], false, "SourceCompletenessUnverified"), null, audit, _token);
            InUseRecord record = (await repository.QueryAsync(new(Search: source.Code), actor.UserId, _token)).Items.Single();
            record.Source.Should().BeEquivalentTo(source);
            await repository.SaveAsync(record with { Version = record.Version + 1, AssigneeId = first!.Id, AssigneeLabel = first.CorporateIdentity }, record.Version, audit, _token);
        }
    }
}
