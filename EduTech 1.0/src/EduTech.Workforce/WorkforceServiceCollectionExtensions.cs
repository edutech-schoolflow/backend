using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Workforce;

/// <summary>Registers the Workforce module (positions today; employments/invites migrate here — EDD-002 V6).</summary>
public static class WorkforceServiceCollectionExtensions
{
    public static IServiceCollection AddWorkforceModule(this IServiceCollection services)
    {
        services.AddScoped<IPositionRepository, PositionRepository>();

        // Staff domain (EDD-002 V6): profiles, employments (affiliations), invites, feature templates.
        services.AddScoped<IStaffUserRepository, StaffUserRepository>();
        services.AddScoped<IStaffAffiliationRepository, StaffAffiliationRepository>();
        services.AddScoped<IStaffInviteTokenRepository, StaffInviteTokenRepository>();
        services.AddScoped<IPermissionTemplateRepository, PermissionTemplateRepository>();
        services.AddScoped<IStaffFeatureOverrideRepository, StaffFeatureOverrideRepository>();
        services.AddScoped<IStaffInviteService, StaffInviteService>();
        services.AddScoped<ISchoolStaffService, SchoolStaffService>();
        return services;
    }
}
