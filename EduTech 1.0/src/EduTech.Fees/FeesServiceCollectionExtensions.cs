using EduTech.Fees.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Fees;

/// <summary>Registers the Fees module (fee types, invoicing, payments).</summary>
public static class FeesServiceCollectionExtensions
{
    public static IServiceCollection AddFeesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISchoolFeeRepository, SchoolFeeRepository>();
        // Finance's answer to the SharedKernel balance port (EDD-002 V1) — consumed by parent-facing reads.
        services.AddScoped<EduTech.Shared.Ports.IStudentFeeBalanceProvider, StudentFeeBalanceProvider>();
        services.AddScoped<ISchoolFeeService, SchoolFeeService>();
        services.AddScoped<IParentFeeRepository, ParentFeeRepository>();
        services.AddScoped<IParentFeeService, ParentFeeService>();

        // Payment rail (Strategy seam). Dev stub auto-confirms; swap for a real Monnify provider by
        // config when credentials exist — the service flow is identical.
        services.AddScoped<IPaymentProvider, StubPaymentProvider>();

        services.AddControllers()
            .AddApplicationPart(typeof(FeesServiceCollectionExtensions).Assembly);

        return services;
    }
}
