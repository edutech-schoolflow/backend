using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Organization;

/// <summary>
/// Registers the Organization context (EDD-010): the platform root. A shadow root during Sprint D —
/// the repository is wired but not yet consumed by production readers.
/// </summary>
public static class OrganizationServiceCollectionExtensions
{
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        return services;
    }
}
