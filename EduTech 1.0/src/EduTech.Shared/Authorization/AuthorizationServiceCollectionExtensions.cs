using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Authorization;

/// <summary>
/// Registers the platform authorization service (EDD-013): the single, actor-neutral
/// <see cref="ICapabilityResolver"/> that <c>[RequireCapability]</c> and every module consult.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityResolution(this IServiceCollection services)
    {
        services.AddMemoryCache();   // idempotent (TryAdd); the resolver's per-context cache
        // EDD-015 / B2d.1 Stage 3 (Commit A): authorization now resolves from the CANONICAL model
        // (AccessContext → Membership → Employment → Position → template/overrides) — no staff_affiliations.
        // Proven byte-identical to the legacy resolver by the Validation Gate before this swap. The legacy
        // CapabilityResolver stays present (unregistered) as an instant `git revert` target until Commit B
        // deletes it, once production has proven the canonical path.
        services.AddScoped<ICapabilityResolver, CanonicalCapabilityResolver>();
        return services;
    }
}
