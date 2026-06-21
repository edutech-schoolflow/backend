namespace EduTech.Auth.SchoolOwner;

/// <summary>
/// Registration payload for a school owner (Actor 1). Phone-first: phone + password required,
/// email optional. School name/subdomain are NOT collected here — captured later during KYC.
/// </summary>
public sealed class RegisterSchoolOwnerRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Email { get; init; }
}
