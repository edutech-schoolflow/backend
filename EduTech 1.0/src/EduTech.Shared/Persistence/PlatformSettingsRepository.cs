namespace EduTech.Shared.Persistence;

/// <summary>Well-known keys in <c>platform_settings</c>.</summary>
public static class PlatformSettingKeys
{
    /// <summary>Flat fee (naira) added on top of every payment — the platform's transfer fee.</summary>
    public const string PaymentPlatformFee = "payment.platform_fee";
}

/// <summary>
/// Reads/writes platform-wide settings (a generic key/value store, editable by Platform Admin).
/// Global (not tenant-scoped), so it uses <see cref="BaseRepository"/>.
/// </summary>
public interface IPlatformSettingsRepository
{
    Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken);
    Task SetDecimalAsync(string key, decimal value, CancellationToken cancellationToken);
}

public sealed class PlatformSettingsRepository : BaseRepository, IPlatformSettingsRepository
{
    public PlatformSettingsRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken)
    {
        string? raw = await QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM platform_settings WHERE key = @Key", new { Key = key }, cancellationToken);

        return decimal.TryParse(raw, out decimal value) ? value : fallback;
    }

    public Task SetDecimalAsync(string key, decimal value, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO platform_settings (key, value, updated_at) VALUES (@Key, @Value, NOW())
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = NOW()
            """,
            new { Key = key, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
    }
}
