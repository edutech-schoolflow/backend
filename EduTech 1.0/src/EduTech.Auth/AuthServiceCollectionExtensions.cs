using EduTech.Shared.Audit;
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
using EduTech.Workforce;

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

        
        services.AddScoped<IStaffAuthService, StaffAuthService>();

        
        services.AddScoped<IStaffInviteAcceptService, StaffInviteAcceptService>();
        services.AddScoped<IStaffSchoolService, StaffSchoolService>();

        // Parent (Actor 3)
        services.AddScoped<IParentRepository, ParentRepository>();
        services.AddScoped<IParentAuthService, ParentAuthService>();

        // EDD-001 Sprint 2 — unified identity auth (one register/login; contexts pick the portal).
        services.AddScoped<Unified.IAuthContextRepository, Unified.AuthContextRepository>();
        services.AddScoped<Unified.IAccessContextProjector, Unified.AccessContextProjector>();
        // EDD-012 B2d — the ONE context-token minter. LegacyContextMinter reads legacy actor tables; a
        // CanonicalContextMinter will be swapped in after a claim-equivalence proof (mint re-source).
        services.AddScoped<Unified.IContextMinter, Unified.LegacyContextMinter>();
        services.AddScoped<Unified.IUnifiedAuthService, Unified.UnifiedAuthService>();
        services.AddScoped<Parent.IParentProfileService, Parent.ParentProfileService>();
        services.AddScoped<EduTech.Shared.Events.IDomainEventHandler<EduTech.Shared.Events.GuardianLinkedEvent>,
            Unified.EnsureIdentityOnGuardianLinked>();
        services.AddScoped<Unified.IIdentityReconciliationRepository, Unified.IdentityReconciliationRepository>();
        services.AddScoped<Unified.IdentityReconciliationJob>();   // daily sweep; scheduled in Program.cs

        // Platform Admin (Actor 4)
        services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();
        services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
        services.AddScoped<IPlatformAdminAuthService, PlatformAdminAuthService>();
        services.AddScoped<IFeatureFlagAdminService, FeatureFlagAdminService>();
        services.AddScoped<IPlatformSettingsAdminService, PlatformSettingsAdminService>();

       
        services.TryAddSingleton<INotificationDispatcher, LoggingNotificationDispatcher>();

        
        services.AddControllers()
            .AddApplicationPart(typeof(AuthServiceCollectionExtensions).Assembly);

        return services;
    }
}
