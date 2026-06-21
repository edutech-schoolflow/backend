namespace EduTech.Auth.PlatformAdmin;

public sealed class CreateFeatureFlagRequest
{
    public string Key { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Enabled { get; init; }
}

/// <summary>Toggle a flag (global, or per-school override).</summary>
public sealed class SetFeatureFlagRequest
{
    public bool Enabled { get; init; }
}
