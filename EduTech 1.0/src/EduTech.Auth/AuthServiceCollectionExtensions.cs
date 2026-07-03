using EduTech.Auth.Otp;
using EduTech.Auth.Parent;
using EduTech.Auth.PlatformAdmin;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.SchoolOwner;
using EduTech.Auth.Security;
using EduTech.Auth.Staff;
using EduTech.Auth.Tokens;
using EduTech.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduTech.Auth;


public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        
        services.AddScoped<IOtpRepository, OtpRepository>();
        services.AddScoped<IOtpService, OtpService>();

        
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        
        services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();

        
        services.AddScoped<ISchoolRepository, SchoolRepository>();
        services.AddScoped<ISchoolOwnerRepository, SchoolOwnerRepository>();
        services.AddScoped<ISchoolOwnerAuthService, SchoolOwnerAuthService>();

        
        services.AddScoped<IStaffUserRepository, StaffUserRepository>();
        services.AddScoped<IStaffAuthService, StaffAuthService>();

        
        services.AddScoped<IStaffAffiliationRepository, StaffAffiliationRepository>();
        services.AddScoped<IStaffInviteTokenRepository, StaffInviteTokenRepository>();
        services.AddScoped<IPermissionTemplateRepository, PermissionTemplateRepository>();
        services.AddScoped<IStaffFeatureOverrideRepository, StaffFeatureOverrideRepository>();
        services.AddScoped<IStaffInviteService, StaffInviteService>();
        services.AddScoped<ISchoolStaffService, SchoolStaffService>();
        services.AddScoped<IStaffInviteAcceptService, StaffInviteAcceptService>();
        services.AddScoped<IStaffSchoolService, StaffSchoolService>();

        // Parent (Actor 3)
        services.AddScoped<IParentRepository, ParentRepository>();
        services.AddScoped<IParentAuthService, ParentAuthService>();

        // Platform Admin (Actor 4)
        services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();
        services.AddScoped<IAdminSchoolRepository, AdminSchoolRepository>();
        services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
        services.AddScoped<IPlatformAdminAuthService, PlatformAdminAuthService>();
        services.AddScoped<ISchoolKycAdminService, SchoolKycAdminService>();
        services.AddScoped<IFeatureFlagAdminService, FeatureFlagAdminService>();
        services.AddScoped<IPlatformSettingsAdminService, PlatformSettingsAdminService>();

       
        services.TryAddSingleton<INotificationDispatcher, LoggingNotificationDispatcher>();

        
        services.AddControllers()
            .AddApplicationPart(typeof(AuthServiceCollectionExtensions).Assembly);

        return services;
    }
}
