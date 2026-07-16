using Microsoft.Extensions.DependencyInjection;

namespace EduTech.People;

/// <summary>
/// Registers the People bounded context (EDD-008) — the foundation for Position and (C2) Employment.
/// Position is the canonical job catalog an organization employs people into.
/// </summary>
public static class PeopleServiceCollectionExtensions
{
    public static IServiceCollection AddPeopleModule(this IServiceCollection services)
    {
        services.AddScoped<IPositionRepository, PositionRepository>();
        return services;
    }
}
