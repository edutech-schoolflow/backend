using EduTech.School.Kyc;
using EduTech.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.School;

/// <summary>Registers the School module (KYC submission + file storage seam).</summary>
public static class SchoolServiceCollectionExtensions
{
    public static IServiceCollection AddSchoolModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFileStorage(configuration);
        services.AddScoped<ISchoolKycRepository, SchoolKycRepository>();
        services.AddScoped<ISchoolKycService, SchoolKycService>();
        // Organization lifecycle management by the platform (EDD-002 V4 — moved out of Auth).
        services.AddScoped<Dashboard.ISchoolDashboardRepository, Dashboard.SchoolDashboardRepository>();
        services.AddScoped<Dashboard.ISchoolDashboardService, Dashboard.SchoolDashboardService>();

        services.AddScoped<PlatformAdmin.IAdminSchoolRepository, PlatformAdmin.AdminSchoolRepository>();
        services.AddScoped<PlatformAdmin.ISchoolKycAdminService, PlatformAdmin.SchoolKycAdminService>();

        services.AddControllers()
            .AddApplicationPart(typeof(SchoolServiceCollectionExtensions).Assembly);

        return services;
    }
}
