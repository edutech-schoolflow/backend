namespace EduTech.Auth.SchoolOwner;


public sealed class LoginRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
