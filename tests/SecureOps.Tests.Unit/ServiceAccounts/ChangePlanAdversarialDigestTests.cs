using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ChangePlanAdversarialDigestTests
{
    private static readonly ChangePlanPreviewRow _row = new(Guid.Parse("00000000-0000-4000-8000-000000000001"), null,
        "SYN-APP", "WindowsService", "SYN-COMPONENT", "SYN\\svc_old", "gmsaSyn", ChangePlanFlag.NoScan, null);

    [Theory]
    [InlineData("a|b", "c", "a", "b|c")]
    [InlineData("3:a|", "~|", "3:", "a|~|")]
    [InlineData("a\nb", "c", "a", "b\nc")]
    [InlineData("a\r\nb", "c", "a", "b\r\nc")]
    public void Digest_DelimiterInjection_CannotMoveTextBetweenFields(string first, string second, string otherFirst, string otherSecond)
    {
        ChangePlanPreviewRow a = _row with { ComponentName = first, CurrentIdentity = second };
        ChangePlanPreviewRow b = _row with { ComponentName = otherFirst, CurrentIdentity = otherSecond };
        ChangePlanRules.Digest([a]).Should().NotBe(ChangePlanRules.Digest([b]));
    }

    [Fact]
    public void Digest_NullEmptyAndLiteralNullMarker_AreDistinct()
    {
        string?[] inputs = [null, "", "~|", "0:|", "\n", "\0"];
        inputs.Select(value => ChangePlanRules.Digest([_row with { ComponentName = value }])).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Digest_EqualSortKeys_ReversedRowsAndDuplicateMultiplicity_AreCanonical()
    {
        ChangePlanPreviewRow first = _row with { CurrentIdentity = "SYN\\svc_a" };
        ChangePlanPreviewRow second = _row with { CurrentIdentity = "SYN\\svc_b" };
        string digest = ChangePlanRules.Digest([first, second, first]);
        ChangePlanRules.Digest([first, first, second]).Should().Be(digest);
        ChangePlanRules.Digest([second, first, first]).Should().Be(digest);
        ChangePlanRules.Digest([first, second]).Should().NotBe(digest);
    }
}
