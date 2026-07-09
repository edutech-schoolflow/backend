using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Identity;

/// <summary>
/// Registers the Identity module (EDD-001 Sprint 1): the global identities store. Sprint 2 adds the
/// unified register/login endpoints here; the legacy per-actor auth in EduTech.Auth keeps working
/// unchanged until then.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IIdentityDirectory, IdentityDirectory>();
        return services;
    }
}
