using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Features;

/// <summary>Registers the release feature-flag service + repository (reads/cache). Depends on the
/// cache (<c>AddEduTechCaching</c>) and persistence being registered.</summary>
public static class FeatureFlagsServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services)
    {
        services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();
        services.AddScoped<IFeatureFlagService, FeatureFlagService>();
        return services;
    }
}
