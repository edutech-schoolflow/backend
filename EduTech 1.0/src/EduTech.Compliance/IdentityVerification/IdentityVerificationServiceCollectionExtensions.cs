using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Compliance.IdentityVerification;

/// <summary>Registers <see cref="IIdentityVerifier"/>: real Dojah when <c>Dojah:Enabled</c> is true
/// (with an API key), otherwise the dev stub that auto-verifies.</summary>
public static class IdentityVerificationServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityVerification(this IServiceCollection services,
        IConfiguration configuration)
    {
        bool dojahEnabled = configuration.GetValue("Dojah:Enabled", false)
            && !string.IsNullOrWhiteSpace(configuration["Dojah:ApiKey"]);

        if (dojahEnabled)
        {
            services.AddHttpClient<IIdentityVerifier, DojahNinVerifier>();
        }
        else
        {
            services.AddSingleton<IIdentityVerifier, StubNinVerifier>();
        }

        return services;
    }
}
