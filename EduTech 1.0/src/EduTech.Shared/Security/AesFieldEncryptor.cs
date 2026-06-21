using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace EduTech.Shared.Security;

/// <summary>
/// AES-GCM field encryptor. The 256-bit key is derived (SHA-256) from <c>Crypto:Key</c>, so any
/// sufficiently strong secret works. Output is base64( nonce | tag | ciphertext ).
/// </summary>
public sealed class AesFieldEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12; // AES-GCM standard
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesFieldEncryptor(IConfiguration configuration)
    {
        string secret = configuration["Crypto:Key"]
            ?? throw new InvalidOperationException("Crypto:Key is not configured.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret)); // 32 bytes
    }

    public string Encrypt(string plaintext)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plainBytes.Length];
        byte[] tag = new byte[TagSize];

        using (AesGcm aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        byte[] output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        byte[] input = Convert.FromBase64String(ciphertext);
        byte[] nonce = input[..NonceSize];
        byte[] tag = input[NonceSize..(NonceSize + TagSize)];
        byte[] cipher = input[(NonceSize + TagSize)..];
        byte[] plain = new byte[cipher.Length];

        using (AesGcm aes = new AesGcm(_key, TagSize))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }
}
