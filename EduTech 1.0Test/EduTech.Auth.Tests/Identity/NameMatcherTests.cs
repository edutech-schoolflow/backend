using EduTech.Shared.Identity;

namespace EduTech.Auth.Tests.Identity;

/// <summary>
/// The identity-match gate: a valid NIN/BVN only passes if its registry name matches the person on
/// file. This is what makes verification "it's really them" rather than "the number is real."
/// </summary>
public class NameMatcherTests
{
    [Theory]
    [InlineData("Grace Okafor", "Grace", "Okafor", true)]
    [InlineData("grace okafor", "GRACE", "OKAFOR", true)]        // case-insensitive
    [InlineData("Grace Mary Okafor", "Grace", "Okafor", true)]   // extra middle name on file
    [InlineData("Okafor Grace", "Grace", "Okafor", true)]        // order doesn't matter
    [InlineData("Mary-Grace Okafor", "Grace", "Okafor", true)]   // hyphenated split
    [InlineData("Grace Okafor", "John", "Okafor", false)]        // first-name mismatch
    [InlineData("Grace Okafor", "Grace", "Eze", false)]          // last-name mismatch
    [InlineData("Grace", "Grace", "Okafor", false)]              // last name absent on file
    public void Matches_ReturnsExpected(string expectedName, string first, string last, bool expected)
    {
        Assert.Equal(expected, NameMatcher.Matches(expectedName, first, last));
    }

    [Theory]
    [InlineData(null, "Okafor")]
    [InlineData("Grace", null)]
    [InlineData("", "Okafor")]
    public void Matches_MissingRegistryName_ReturnsFalse(string? first, string? last)
    {
        Assert.False(NameMatcher.Matches("Grace Okafor", first, last));
    }
}
