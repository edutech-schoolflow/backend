namespace EduTech.Auth.Staff;

/// <summary>
/// Standalone staff self-registration (Path A). Phone-first: phone + password required, email
/// optional. No school is attached — affiliations come later via invite or (future) marketplace.
/// </summary>
public sealed class RegisterStaffRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Email { get; init; }
}
