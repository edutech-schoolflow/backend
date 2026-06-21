namespace EduTech.Shared.Security;

/// <summary>
/// Encrypts sensitive identifiers (NIN, BVN) for storage at rest. Values are never logged or returned
/// to clients; decryption is only for backend verification (e.g. Dojah).
/// </summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
