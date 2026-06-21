using EduTech.Shared.Phone;

namespace EduTech.Auth.Tests.Shared;

/// <summary>
/// Phone normalization is the spine of our phone-first identity: every register/login/verify path
/// runs the raw input through <see cref="PhoneNumber.Normalize"/> first. If this drifts, accounts
/// created one way can't log in another (the real bug we hit with 0813… vs +23481…).
/// </summary>
public class PhoneNumberTests
{
    [Theory]
    [InlineData("08137729210", "+2348137729210")]   // local 0-prefixed
    [InlineData("8137729210", "+2348137729210")]    // 10-digit national
    [InlineData("2348137729210", "+2348137729210")] // country code, no +
    [InlineData("+2348137729210", "+2348137729210")] // already canonical
    [InlineData("+234 813 772 9210", "+2348137729210")] // spaces stripped
    [InlineData("0701-234-5678", "+2347012345678")] // separators stripped
    public void Normalize_ValidNigerianForms_ReturnsCanonicalE164(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]           // too short
    [InlineData("1234567890")]    // 10 digits but national starts with 1 (not a mobile prefix)
    [InlineData("06012345678")]   // 11 digits but national starts with 6
    public void Normalize_InvalidOrUnsupported_ReturnsNull(string? input)
    {
        Assert.Null(PhoneNumber.Normalize(input));
    }
}
