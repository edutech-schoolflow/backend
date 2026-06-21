using EduTech.Compliance.IdentityVerification;
using EduTech.Compliance.Nin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Compliance;

/// <summary>Registers the Compliance module (staff/parent NIN verification + Dojah seam).</summary>
public static class ComplianceServiceCollectionExtensions
{
    public static IServiceCollection AddComplianceModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityVerification(configuration);
        services.AddScoped<IComplianceRepository, ComplianceRepository>();
        services.AddScoped<IComplianceService, ComplianceService>();

        services.AddControllers()
            .AddApplicationPart(typeof(ComplianceServiceCollectionExtensions).Assembly);

        return services;
    }
}
