using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Workforce;

/// <summary>Registers the Workforce (business) module — staff profiles, invites, attendance. Position
/// moved to the EduTech.People foundation context (EDD-008).</summary>
public static class WorkforceServiceCollectionExtensions
{
    public static IServiceCollection AddWorkforceModule(this IServiceCollection services)
    {
        // Staff domain (EDD-002 V6): profiles, employments (affiliations), invites, feature templates.
        services.AddScoped<IStaffUserRepository, StaffUserRepository>();
        services.AddScoped<IStaffAffiliationRepository, StaffAffiliationRepository>();
        services.AddScoped<IStaffInviteTokenRepository, StaffInviteTokenRepository>();
        services.AddScoped<IPermissionTemplateRepository, PermissionTemplateRepository>();
        services.AddScoped<IStaffFeatureOverrideRepository, StaffFeatureOverrideRepository>();
        services.AddScoped<IStaffInviteService, StaffInviteService>();
        services.AddScoped<ISchoolStaffService, SchoolStaffService>();
        services.AddScoped<Staffing.IStaffProfileService, Staffing.StaffProfileService>();
        services.AddScoped<StaffAttendance.IStaffAttendanceRepository, StaffAttendance.StaffAttendanceRepository>();
        services.AddScoped<StaffAttendance.IStaffAttendanceService, StaffAttendance.StaffAttendanceService>();

        return services;
    }
}
