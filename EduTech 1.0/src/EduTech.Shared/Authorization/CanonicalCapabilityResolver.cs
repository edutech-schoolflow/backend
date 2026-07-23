using System.Text.Json;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace EduTech.Shared.Authorization;

/// <summary>
/// The CANONICAL capability resolver (EDD-015 / B2d.1 Stage 3) — resolves a staff workspace entirely from
/// the canonical model, with NO <c>staff_affiliations</c> read:
/// <code>
/// context_id → access_contexts (type, membership_id)
///            → active Employment (by membership)
///            → Position (role = slug; default template)
///            → effective template = Employment.template ?? Position.template
///            → employment_feature_overrides
///            → CapabilityResolution.Resolve(...)
/// </code>
/// Owner → all; parent / none / no-active-employment → ∅. Byte-identical to the legacy
/// <see cref="CapabilityResolver"/> by construction: it feeds the SAME <see cref="CapabilityResolution"/>
/// core, differing only in where role/template/overrides are read from.
///
/// <para>Built PARALLEL to the live resolver and NOT registered in DI. The Validation Gate proves it
/// equivalent for every access context before Stage 3 swaps DI onto it. It still keys on the
/// <c>context_id</c> claim (= <c>reference_id</c> today) — the claim flip is a separate, later step.</para>
/// </summary>
internal sealed class CanonicalCapabilityResolver : BaseRepository, ICapabilityResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    public CanonicalCapabilityResolver(IDbConnectionFactory connectionFactory, IMemoryCache cache)
        : base(connectionFactory)
    {
        _cache = cache;
    }

    public async Task<bool> HasCapabilityAsync(Guid contextId, string capability, CancellationToken cancellationToken)
        => (await GetCapabilitiesAsync(contextId, cancellationToken)).Has(capability);

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
        ContextRow? ctx = await QuerySingleOrDefaultAsync<ContextRow>(
            "SELECT type AS Type, membership_id AS MembershipId FROM access_contexts " +
            "WHERE reference_id = @ContextId AND status = 'active' LIMIT 1",
            new { ContextId = contextId }, cancellationToken);

        // owner → all; parent / unknown → ∅ (no reads needed) — same as legacy.
        if (ctx is null || ctx.Type != "staff")
        {
            return CapabilityResolution.Resolve(ctx?.Type, null, null,
                new Dictionary<string, bool>(StringComparer.Ordinal));
        }

        // The active Employment for this membership → role + effective template (Employment ?? Position).
        EmploymentRow? job = ctx.MembershipId is Guid membershipId
            ? await QuerySingleOrDefaultAsync<EmploymentRow>(
                """
                SELECT p.slug AS Role,
                       COALESCE(e.permission_template_id, p.permission_template_id) AS TemplateId,
                       e.id AS EmploymentId
                FROM employments e
                JOIN positions p ON p.id = e.position_id
                WHERE e.membership_id = @MembershipId AND e.status = 'active'
                ORDER BY e.started_at NULLS LAST, e.created_at
                LIMIT 1
                """,
                new { MembershipId = membershipId }, cancellationToken)
            : null;

        if (job is null)
        {
            return CapabilitySet.Empty;   // staff context with no active employment
        }

        IReadOnlyDictionary<string, bool>? templateFeatures = null;
        if (job.TemplateId is Guid templateId)
        {
            string? json = await QuerySingleOrDefaultAsync<string>(
                "SELECT features FROM permission_templates WHERE id = @Id", new { Id = templateId }, cancellationToken);
            if (!string.IsNullOrWhiteSpace(json))
            {
                templateFeatures = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            }
        }

        IReadOnlyList<OverrideRow> overrideRows = await QueryAsync<OverrideRow>(
            "SELECT feature_key AS FeatureKey, enabled AS Enabled FROM employment_feature_overrides " +
            "WHERE employment_id = @Id",
            new { Id = job.EmploymentId }, cancellationToken);
        Dictionary<string, bool> overrides = overrideRows.ToDictionary(o => o.FeatureKey, o => o.Enabled, StringComparer.Ordinal);

        return CapabilityResolution.Resolve("staff", job.Role, templateFeatures, overrides);
    }

    private static string CacheKey(Guid contextId) => $"cancap:{contextId}";

    private sealed class ContextRow
    {
        public string Type { get; init; } = string.Empty;
        public Guid? MembershipId { get; init; }
    }

    private sealed class EmploymentRow
    {
        public string Role { get; init; } = string.Empty;
        public Guid? TemplateId { get; init; }
        public Guid EmploymentId { get; init; }
    }

    private sealed class OverrideRow
    {
        public string FeatureKey { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }
}
