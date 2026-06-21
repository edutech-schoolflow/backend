using EduTech.Shared.Security;
using Microsoft.Extensions.Configuration;

namespace EduTech.Auth.Tests.Security;

/// <summary>NIN/BVN field encryption round-trips, and ciphertext is not the plaintext.</summary>
public class AesFieldEncryptorTests
{
    private static AesFieldEncryptor Create()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Crypto:Key"] = "test-secret-key-for-unit-tests" })
            .Build();
        return new AesFieldEncryptor(config);
    }

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips()
    {
        AesFieldEncryptor encryptor = Create();

        string cipher = encryptor.Encrypt("11122233344");

        Assert.NotEqual("11122233344", cipher);
        Assert.Equal("11122233344", encryptor.Decrypt(cipher));
    }

    [Fact]
    public void Encrypt_SameInput_ProducesDifferentCiphertext()
    {
        AesFieldEncryptor encryptor = Create();

        // Random nonce per call → ciphertexts differ even for identical input.
        Assert.NotEqual(encryptor.Encrypt("11122233344"), encryptor.Encrypt("11122233344"));
    }
}
