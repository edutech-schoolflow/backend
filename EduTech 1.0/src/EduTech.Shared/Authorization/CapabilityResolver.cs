using System.Text.Json;
using EduTech.Shared.Constants;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace EduTech.Shared.Authorization;

/// <summary>
/// Resolves a workspace's capabilities from the canonical model, keyed on <c>context_id</c> (EDD-013).
///
/// <para>Resolution, byte-identical to the decisions the 13 JWT flags encoded today:
/// <list type="bullet">
///   <item><c>access_contexts.type = owner</c> → every capability (replaces the is_owner bypass).</item>
///   <item><c>staff</c> → role default → permission template → per-staff overrides ⇒ effective flags,
///     mapped to capabilities via <see cref="CapabilityRegistry"/>.</item>
///   <item>parent / unknown → empty.</item>
/// </list>
/// Consumers are fully actor-neutral (they only call this); the type branch here is an internal detail
/// that collapses once template/overrides migrate onto Employment/Position. Reading the legacy
/// <c>staff_affiliations</c> for staff resolution is a strangler detail, retired in B2d.</para>
///
/// Cached per context (short TTL) — permission checks are constant, changes are rare.
/// </summary>
internal sealed class CapabilityResolver : BaseRepository, ICapabilityResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    public CapabilityResolver(IDbConnectionFactory connectionFactory, IMemoryCache cache)
        : base(connectionFactory)
    {
        _cache = cache;
    }

    public async Task<bool> HasCapabilityAsync(Guid contextId, string capability, CancellationToken cancellationToken)
    {
        CapabilitySet set = await GetCapabilitiesAsync(contextId, cancellationToken);
        return set.Has(capability);
    }

    public async Task<CapabilitySet> GetCapabilitiesAsync(Guid contextId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(contextId), out CapabilitySet? cached) && cached is not null)
        {
            return cached;
        }

        CapabilitySet set = await ResolveAsync(contextId, cancellationToken);
        _cache.Set(CacheKey(contextId), set, CacheTtl);
        return set;
    }

    public void Invalidate(Guid contextId) => _cache.Remove(CacheKey(contextId));

    private async Task<CapabilitySet> ResolveAsync(Guid contextId, CancellationToken cancellationToken)
    {
        // The workspace's type is the canonical discriminator (owner / staff / parent).
        string? type = await QuerySingleOrDefaultAsync<string>(
            "SELECT type FROM access_contexts WHERE reference_id = @ContextId AND status = 'active' LIMIT 1",
            new { ContextId = contextId }, cancellationToken);

        // Only staff needs role/template/overrides; owner=all, parent/unknown=∅ need no reads.
        string? role = null;
        IReadOnlyDictionary<string, bool>? templateFeatures = null;
        Dictionary<string, bool> overrides = new(StringComparer.Ordinal);

        if (type == "staff")
        {
            StaffAuthzRow? affiliation = await QuerySingleOrDefaultAsync<StaffAuthzRow>(
                "SELECT role AS Role, permission_template_id AS PermissionTemplateId FROM staff_affiliations WHERE id = @Id",
                new { Id = contextId }, cancellationToken);
            if (affiliation is null)
            {
                return CapabilitySet.Empty;
            }

            role = affiliation.Role;
            if (affiliation.PermissionTemplateId is Guid templateId)
            {
                string? json = await QuerySingleOrDefaultAsync<string>(
                    "SELECT features FROM permission_templates WHERE id = @Id", new { Id = templateId }, cancellationToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    templateFeatures = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                }
            }

            IReadOnlyList<OverrideRow> overrideRows = await QueryAsync<OverrideRow>(
                "SELECT feature_key AS FeatureKey, enabled AS Enabled FROM staff_feature_overrides WHERE affiliation_id = @Id",
                new { Id = contextId }, cancellationToken);
            overrides = overrideRows.ToDictionary(o => o.FeatureKey, o => o.Enabled, StringComparer.Ordinal);
        }

        return CapabilityResolution.Resolve(type, role, templateFeatures, overrides);
    }

    private static string CacheKey(Guid contextId) => $"cap:{contextId}";

    private sealed class StaffAuthzRow
    {
        public string Role { get; init; } = string.Empty;
        public Guid? PermissionTemplateId { get; init; }
    }

    private sealed class OverrideRow
    {
        public string FeatureKey { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }
}
