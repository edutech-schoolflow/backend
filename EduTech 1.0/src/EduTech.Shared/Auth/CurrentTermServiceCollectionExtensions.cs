using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Auth;

public static class CurrentTermServiceCollectionExtensions
{
    /// <summary>Registers the provider behind the [RequiresCurrentTerm] filter.</summary>
    public static IServiceCollection AddCurrentTermGuard(this IServiceCollection services)
    {
        services.AddScoped<ICurrentTermProvider, CurrentTermProvider>();
        return services;
    }
}
