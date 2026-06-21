namespace EduTech.Auth.Security;

/// <summary>
/// Hashes and verifies account passwords. Used by every actor's auth flow.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password for storage.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash.</summary>
    bool Verify(string password, string hash);
}
