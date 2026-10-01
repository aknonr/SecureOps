using System.Net;
using System.Text;
using FluentAssertions;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the Service Accounts UI transport and wording: module codes map to operator text, the rule the API names is
/// explained, the commit carries its idempotency key, and navigation stays capability-gated.
/// </summary>
public sealed class ServiceAccountUiTests
{
    [Theory]
    [InlineData("ServiceAccountConcurrencyConflict", 409, UiProblemKind.Conflict)]
    [InlineData("ServiceAccountImportPreviewStale", 409, UiProblemKind.Conflict)]
    [InlineData("ServiceAccountAccessDenied", 403, UiProblemKind.Forbidden)]
    [InlineData("ServiceAccountsNotConfigured", 503, UiProblemKind.NotConfigured)]
    [InlineData("ServiceAccountImportFileRejected", 400, UiProblemKind.Validation)]
    public void ModuleCodes_MapToOperatorProblems(string code, int status, UiProblemKind kind)
    {
        UiProblem problem = UiProblemFactory.FromResponse(status, new ProblemDetailsPayload { Code = code });

        problem.Kind.Should().Be(kind);
        problem.Code.Should().Be(code);
        problem.Title.Should().NotBeNullOrWhiteSpace().And.NotContain("ServiceAccount");
    }

    [Fact]
    public async Task Failure_KeepsTheNamedRule_AndExplainsItInTurkish()
    {
        (ServiceAccountApiClient client, Handler _) = Create(HttpStatusCode.BadRequest,
            """{"code":"ServiceAccountValidationFailed","field":"OrRequiredForDeletion","status":400}""");

        Func<Task> act = () => client.SendAsync<AccountDetail>(HttpMethod.Post, "/accounts/x/actions", new { }, CancellationToken.None);

        SecureOpsApiException failure = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        failure.Problem.Fields.Should().Equal("OrRequiredForDeletion");
        ServiceAccountProblems.FieldMessage(failure.Problem).Should().Contain("OR numarası");
    }

    [Fact]
    public async Task Commit_SendsIdempotencyKeyHeader_UnderTheModulePrefix()
    {
        (ServiceAccountApiClient client, Handler handler) = Create(HttpStatusCode.OK, "{}");

        await client.SendAsync<Dictionary<string, object>>(HttpMethod.Post, "/imports/1/commit", new ImportCommitRequest(2, 3), CancellationToken.None,
            new Dictionary<string, string> { ["Idempotency-Key"] = "sa-import-key-0001" });

        handler.Last!.RequestUri!.AbsolutePath.Should().Be("/api/v1/service-accounts/imports/1/commit");
        handler.Last.Headers.GetValues("Idempotency-Key").Should().Equal("sa-import-key-0001");
        handler.Body.Should().Contain("\"previewVersion\":2").And.Contain("\"decisionVersion\":3");
    }

    [Fact]
    public async Task Upload_SendsFileAndOnlySuppliedFields()
    {
        (ServiceAccountApiClient client, Handler handler) = Create(HttpStatusCode.OK, "{}");

        await client.UploadAsync<Dictionary<string, object>>("/imports", "liste.csv", "text/csv", Encoding.UTF8.GetBytes("a;b"),
            new Dictionary<string, string?> { ["profile"] = "generic", ["sheet"] = null }, CancellationToken.None);

        handler.Body.Should().Contain("name=file").And.Contain("liste.csv").And.Contain("name=profile").And.NotContain("name=sheet");
    }

    [Fact]
    public void UnknownDates_AreStatedNotInvented()
    {
        ServiceAccountUiText.Date(null).Should().Be("Tarih bilinmiyor");
        ServiceAccountUiText.Date(new DateOnly(2026, 9, 28)).Should().Be("28.09.2026");
        ServiceAccountUiText.Action("GmsaHandover").Should().Be("gMSA ile devir");
        ServiceAccountUiText.Actions.Should().HaveCount(7);
    }

    [Fact]
    public void Navigation_IsGatedOnTheModuleViewCapability()
    {
        string nav = Ui("Shared", "NavMenu.razor");
        nav.Should().Contain("ServiceAccountCapabilities.View").And.Contain("Href=\"service-accounts\"");
        foreach (string page in Directory.GetFiles(Path.Combine(Root(), "src", "SecureOps.Ui", "Pages", "ServiceAccounts"), "*.razor"))
        {
            string text = File.ReadAllText(page);
            text.Should().Contain("@attribute [Authorize]").And.Contain("@inherits ServiceAccountPageBase", page);
        }
    }

    private static (ServiceAccountApiClient Client, Handler Handler) Create(HttpStatusCode status, string body)
    {
        Handler handler = new(status, body);
        HttpClient http = new(handler) { BaseAddress = new Uri("https://localhost:5001/") };
        return (new ServiceAccountApiClient(http, new FakeApiSessionContext()), handler);
    }

    private static string Ui(params string[] path) => File.ReadAllText(Path.Combine([Root(), "src", "SecureOps.Ui", .. path]));

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
