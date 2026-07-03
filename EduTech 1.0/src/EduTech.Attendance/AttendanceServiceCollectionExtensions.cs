using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Attendance;

/// <summary>Registers the Attendance module (daily register marking + the staff attendance board).</summary>
public static class AttendanceServiceCollectionExtensions
{
    public static IServiceCollection AddAttendanceModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IAttendanceService, AttendanceService>();

        services.AddControllers()
            .AddApplicationPart(typeof(AttendanceServiceCollectionExtensions).Assembly);

        return services;
    }
}
