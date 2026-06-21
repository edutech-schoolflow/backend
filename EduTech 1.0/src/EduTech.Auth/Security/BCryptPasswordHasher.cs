using BCryptNet = BCrypt.Net.BCrypt;

namespace EduTech.Auth.Security;

/// <summary>
/// BCrypt password hasher. Cost factor 12 per the spec (§1.5 etc.).
/// The 6-digit parent payment PIN is hashed the same way (it is also a secret).
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        return BCryptNet.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        return BCryptNet.Verify(password, hash);
    }
}
