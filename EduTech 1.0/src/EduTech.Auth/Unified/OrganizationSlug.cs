using System.Text;

namespace EduTech.Auth.Unified;

/// <summary>
/// Turns a school name into a URL slug for /o/{slug}: ASCII lowercase, non-alphanumerics become
/// single hyphens, trimmed and length-capped. Returns empty when the name has no usable characters
/// (the caller then keeps the existing placeholder slug rather than minting a blank one).
/// </summary>
internal static class OrganizationSlug
{
    private const int MaxLength = 40;

    public static string From(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);
        bool lastWasHyphen = false;

        foreach (char raw in name.Trim().ToLowerInvariant())
        {
            if (raw is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(raw);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug;
    }
}
