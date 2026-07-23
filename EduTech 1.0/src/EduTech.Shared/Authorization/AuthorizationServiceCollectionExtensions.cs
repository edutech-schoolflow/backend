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
        services.AddScoped<ICapabilityResolver, CapabilityResolver>();
        return services;
    }
}
