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

        services.AddControllers()
            .AddApplicationPart(typeof(SchoolServiceCollectionExtensions).Assembly);

        return services;
    }
}
