using System.Security.Cryptography;
using EduTech.Auth.Security;

namespace EduTech.Auth.Otp;

/// <summary>
/// The single OTP component for the platform (see <see cref="IOtpService"/>). Codes are 6 digits,
/// cryptographically random, stored hashed (reuses <see cref="IPasswordHasher"/>), expire in 5
/// minutes, and allow at most 3 verification attempts. Per-IP request throttling is handled
/// separately by the HTTP `otp` rate-limit policy.
/// </summary>
internal sealed class OtpService : IOtpService
{
    private const int ExpiryMinutes = 5;
    private const int MaxAttempts = 3;

    private readonly IOtpRepository _repository;
    private readonly IPasswordHasher _hasher;

    public OtpService(IOtpRepository repository, IPasswordHasher hasher)
    {
        _repository = repository;
        _hasher = hasher;
    }

    public async Task<string> GenerateAsync(string purpose, Guid targetId, string phone,
        CancellationToken cancellationToken = default)
    {
        await _repository.InvalidateActiveAsync(purpose, targetId, cancellationToken);

        // 100000–999999 inclusive (GetInt32 upper bound is exclusive).
        string code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        string codeHash = _hasher.Hash(code);
        DateTime expiresAt = DateTime.UtcNow.AddMinutes(ExpiryMinutes);

        await _repository.InsertAsync(purpose, targetId, phone, codeHash, expiresAt, cancellationToken);
        return code;
    }

    public async Task<OtpVerifyResult> VerifyAsync(string purpose, Guid targetId, string code,
        CancellationToken cancellationToken = default)
    {
        OtpCodeRow? row = await _repository.GetLatestActiveAsync(purpose, targetId, cancellationToken);
        if (row is null)
        {
            return OtpVerifyResult.NotFound;
        }

        if (row.ExpiresAt < DateTime.UtcNow)
        {
            await _repository.MarkUsedAsync(row.Id, cancellationToken);
            return OtpVerifyResult.Expired;
        }

        if (row.Attempts >= MaxAttempts)
        {
            await _repository.MarkUsedAsync(row.Id, cancellationToken);
            return OtpVerifyResult.TooManyAttempts;
        }

        if (!_hasher.Verify(code, row.CodeHash))
        {
            await _repository.IncrementAttemptsAsync(row.Id, cancellationToken);
            return OtpVerifyResult.Invalid;
        }

        await _repository.MarkUsedAsync(row.Id, cancellationToken);
        return OtpVerifyResult.Success;
    }
}
