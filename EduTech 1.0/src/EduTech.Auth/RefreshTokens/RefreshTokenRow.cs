namespace EduTech.Auth.RefreshTokens;

/// <summary>Row shape read back from <c>refresh_tokens</c> during rotation.</summary>
internal sealed class RefreshTokenRow
{
    public Guid Id { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public Guid ActorId { get; init; }
    // Canonical key (EDD-012 B2c.3c) — the actor columns above retire in B2d.
    public Guid? IdentityId { get; init; }
    public Guid? ContextId { get; init; }
    public Guid FamilyId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RotatedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}
