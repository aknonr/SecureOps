using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the optional-purpose rules for read-only Directory Explorer queries.
/// </summary>
/// <remarks>
/// These duplicate <c>DirectoryLookupPurpose</c> on the server deliberately, so a divergence shows up
/// as a failed test rather than as a request the operator cannot understand being refused. The server
/// stays the authority and re-validates everything.
/// </remarks>
public sealed class DirectoryPurposeInputTests
{
    [Fact]
    public void MaxLength_MatchesTheServerBound()
    {
        // Must stay equal to DirectoryExplorerOptions.MaxPurposeLength.
        DirectoryPurposeInput.MaxLength.Should().Be(256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  ")]
    public void Normalize_TreatsAbsentAndBlankAlike(string? purpose)
    {
        // Whitespace collapses to null rather than to an empty string: the two are equivalent to the
        // server, but null states plainly that no purpose was given.
        DirectoryPurposeInput.Normalize(purpose).Should().BeNull();
    }

    [Fact]
    public void Normalize_TrimsASuppliedValue()
    {
        DirectoryPurposeInput.Normalize("  Yetki incelemesi  ").Should().Be("Yetki incelemesi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AcceptsAnOmittedOrBlankPurpose(string? purpose)
    {
        // The whole point of the delta: a read-only lookup must not be blocked for want of a reason.
        DirectoryPurposeInput.Validate(purpose).Should().BeNull();
    }

    [Fact]
    public void Validate_AcceptsExactlyTheMaximumLength()
    {
        DirectoryPurposeInput.Validate(new string('a', DirectoryPurposeInput.MaxLength))
            .Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsOneCharacterBeyondTheMaximum()
    {
        DirectoryPurposeInput.Validate(new string('a', DirectoryPurposeInput.MaxLength + 1))
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_MeasuresLengthAfterTrimming()
    {
        // Trailing spaces must not push an otherwise valid sentence over the limit, because the
        // server trims before it measures too.
        string padded = "  " + new string('a', DirectoryPurposeInput.MaxLength) + "   ";

        DirectoryPurposeInput.Validate(padded).Should().BeNull();
    }

    [Theory]
    [InlineData(9)]   // tab
    [InlineData(10)]  // line feed
    [InlineData(13)]  // carriage return
    [InlineData(27)]  // escape
    [InlineData(0)]   // null
    public void Validate_RejectsControlCharacters(int codePoint)
    {
        // Usually arrives by pasting from a terminal or a spreadsheet cell. The server refuses these,
        // so refusing them here turns a round-trip into an inline message. Built at runtime rather
        // than embedded, so this source file stays plain text.
        string purpose = "Yetki" + (char)codePoint + "incelemesi";

        DirectoryPurposeInput.Validate(purpose).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_AcceptsOrdinaryTurkishText()
    {
        DirectoryPurposeInput.Validate("Şüpheli oturum açma incelemesi — İK talebi")
            .Should().BeNull();
    }
}
