using EduTech.Shared.Persistence;

namespace EduTech.Workforce;

/// <summary>Reads per-affiliation feature overrides (the top layer of resolution).</summary>
internal interface IStaffFeatureOverrideRepository
{
    Task<IReadOnlyDictionary<string, bool>> GetForAffiliationAsync(Guid affiliationId,
        CancellationToken cancellationToken);
}

internal sealed class StaffFeatureOverrideRow
{
    public string FeatureKey { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

internal sealed class StaffFeatureOverrideRepository : BaseRepository, IStaffFeatureOverrideRepository
{
    public StaffFeatureOverrideRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetForAffiliationAsync(Guid affiliationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffFeatureOverrideRow> rows = await QueryAsync<StaffFeatureOverrideRow>(
            "SELECT feature_key, enabled FROM staff_feature_overrides WHERE affiliation_id = @Id",
            new { Id = affiliationId }, cancellationToken);

        Dictionary<string, bool> overrides = new Dictionary<string, bool>(rows.Count);
        foreach (StaffFeatureOverrideRow row in rows)
        {
            overrides[row.FeatureKey] = row.Enabled;
        }

        return overrides;
    }
}
