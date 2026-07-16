using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Membership;

/// <summary>
/// Registers the Membership bounded context (EDD-007): the canonical belonging edge and the source
/// of truth for adult membership lifecycle. Adult lifecycle flows in Auth/Workforce drive it via
/// <see cref="IMembershipRepository"/>.
/// </summary>
public static class MembershipServiceCollectionExtensions
{
    public static IServiceCollection AddMembershipModule(this IServiceCollection services)
    {
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        return services;
    }
}
