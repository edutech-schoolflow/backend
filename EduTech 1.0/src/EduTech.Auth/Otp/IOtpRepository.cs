namespace EduTech.Auth.Otp;

/// <summary>
/// Data access for the global <c>otp_codes</c> table (no school_id — OTP targets are keyed by
/// actor record, not tenant).
/// </summary>
internal interface IOtpRepository
{
    /// <summary>Invalidates any unused codes for (purpose, targetId) before a new one is issued.</summary>
    Task InvalidateActiveAsync(string purpose, Guid targetId, CancellationToken cancellationToken);

    Task InsertAsync(string purpose, Guid targetId, string phone, string codeHash,
        DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>The most recent unused code for (purpose, targetId), or null.</summary>
    Task<OtpCodeRow?> GetLatestActiveAsync(string purpose, Guid targetId, CancellationToken cancellationToken);

    Task IncrementAttemptsAsync(Guid id, CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid id, CancellationToken cancellationToken);
}
