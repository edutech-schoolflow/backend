namespace EduTech.Shared.Identity;

/// <summary>
/// Tolerant name match for identity verification: the registered first AND last name must both
/// appear as whole words in the expected name (case-insensitive). Handles ordering and extra middle
/// names; deliberately does NOT handle typos (the official name should be entered correctly).
/// </summary>
internal static class NameMatcher
{
    private static readonly char[] Separators = { ' ', '-', '.', ',', '\'' };

    public static bool Matches(string expectedName, string? first, string? last)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            return false;
        }

        HashSet<string> expected = Tokenize(expectedName);
        return expected.Contains(first.Trim().ToLowerInvariant())
            && expected.Contains(last.Trim().ToLowerInvariant());
    }

    private static HashSet<string> Tokenize(string name)
        => name.ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}
