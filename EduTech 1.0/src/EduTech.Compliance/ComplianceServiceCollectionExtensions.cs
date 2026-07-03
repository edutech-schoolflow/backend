using EduTech.Compliance.Nin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Compliance;

/// <summary>Registers the Compliance module (staff/parent NIN verification). The shared
/// IIdentityVerifier is registered once in Program.cs (AddIdentityVerification).</summary>
public static class ComplianceServiceCollectionExtensions
{
    public static IServiceCollection AddComplianceModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IComplianceRepository, ComplianceRepository>();
        services.AddScoped<IComplianceService, ComplianceService>();

        services.AddControllers()
            .AddApplicationPart(typeof(ComplianceServiceCollectionExtensions).Assembly);

        return services;
    }
}
