using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers Dapper + the PostgreSQL connection factory and applies global Dapper config.
    ///
    /// Tenant isolation: repositories over per-school tables derive from <see cref="TenantRepository"/>,
    /// which binds @SchoolId from <see cref="EduTech.Shared.Context.IEduTechRequestContext"/> so every
    /// tenant query is scoped to the current school. (The request context is registered in Program.cs.)
    /// </summary>
    public static IServiceCollection AddEduTechPersistence(this IServiceCollection services,
        IConfiguration configuration)
    {
        DapperConfiguration.Configure();

        string connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is missing");

        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
        services.AddScoped<IPlatformSettingsRepository, PlatformSettingsRepository>();

        return services;
    }
}
