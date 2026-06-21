namespace EduTech.Auth.RefreshTokens;

/// <summary>Row shape read back from <c>refresh_tokens</c> during rotation.</summary>
internal sealed class RefreshTokenRow
{
    public Guid Id { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public Guid ActorId { get; init; }
    public Guid FamilyId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RotatedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}
