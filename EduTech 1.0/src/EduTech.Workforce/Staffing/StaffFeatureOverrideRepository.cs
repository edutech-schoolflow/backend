using EduTech.Shared.Persistence;

namespace EduTech.Workforce;

/// <summary>Reads per-affiliation feature overrides (the top layer of resolution).</summary>
internal interface IStaffFeatureOverrideRepository
{
    Task<IReadOnlyDictionary<string, bool>> GetForAffiliationAsync(Guid affiliationId,
        CancellationToken cancellationToken);

    /// <summary>Overrides for many affiliations in ONE query (avoids the per-staff N+1 in the directory view).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>>> GetForAffiliationsAsync(
        IReadOnlyCollection<Guid> affiliationIds, CancellationToken cancellationToken);
}

internal sealed class StaffFeatureOverrideRow
{
    public string FeatureKey { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

internal sealed class StaffFeatureOverrideBatchRow
{
    public Guid AffiliationId { get; init; }
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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>>> GetForAffiliationsAsync(
        IReadOnlyCollection<Guid> affiliationIds, CancellationToken cancellationToken)
    {
        if (affiliationIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyDictionary<string, bool>>();
        }

        IReadOnlyList<StaffFeatureOverrideBatchRow> rows = await QueryAsync<StaffFeatureOverrideBatchRow>(
            "SELECT affiliation_id, feature_key, enabled FROM staff_feature_overrides WHERE affiliation_id = ANY(@Ids)",
            new { Ids = affiliationIds.ToArray() }, cancellationToken);

        Dictionary<Guid, Dictionary<string, bool>> byAffiliation = new Dictionary<Guid, Dictionary<string, bool>>();
        foreach (StaffFeatureOverrideBatchRow row in rows)
        {
            if (!byAffiliation.TryGetValue(row.AffiliationId, out Dictionary<string, bool>? map))
            {
                map = new Dictionary<string, bool>();
                byAffiliation[row.AffiliationId] = map;
            }
            map[row.FeatureKey] = row.Enabled;
        }

        return byAffiliation.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyDictionary<string, bool>)kv.Value);
    }
}
