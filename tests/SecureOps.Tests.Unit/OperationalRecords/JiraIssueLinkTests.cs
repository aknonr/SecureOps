using FluentAssertions;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class JiraIssueLinkTests
{
    [Theory]
    [InlineData("https://jira.contoso.example/browse/", "https://jira.contoso.example/browse/SYN-42")]
    [InlineData("https://jira.contoso.example/browse", "https://jira.contoso.example/browse/SYN-42")]
    public void Corporate_WithHttpsBrowseBase_LinksTheKey(string baseUrl, string expected) =>
        JiraIssueLink.Create(Options("Corporate", baseUrl), "SYN-42").Should().Be(expected);

    [Theory]
    [InlineData("Corporate", "")]
    [InlineData("Corporate", "http://jira.contoso.example/browse/")]
    [InlineData("Corporate", "https://user:secret@jira.contoso.example/browse/")]
    [InlineData("Corporate", "https://jira.contoso.example/browse/?q=1")]
    [InlineData("Corporate", "https://jira.contoso.example/browse/#x")]
    [InlineData("Corporate", "/browse/")]
    [InlineData("Simulation", "https://jira.contoso.example/browse/")]
    [InlineData("Fake", "https://jira.contoso.example/browse/")]
    [InlineData("Disabled", "https://jira.contoso.example/browse/")]
    public void MissingUnsafeOrNonCorporateConfiguration_GivesNoLink(string provider, string baseUrl) =>
        JiraIssueLink.Create(Options(provider, baseUrl), "SYN-42").Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("syn-42")]
    [InlineData("SYN-")]
    [InlineData("SYN-42/../admin")]
    [InlineData("SYN-42?x=1")]
    public void InvalidKey_GivesNoLink(string? key) =>
        JiraIssueLink.Create(Options("Corporate", "https://jira.contoso.example/browse/"), key).Should().BeNull();

    private static JiraIntegrationOptions Options(string provider, string baseUrl) =>
        new() { Provider = provider, IssueBrowseBaseUrl = baseUrl };
}
