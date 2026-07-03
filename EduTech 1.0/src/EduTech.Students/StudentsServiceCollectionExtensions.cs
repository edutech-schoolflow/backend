using EduTech.Students.Academics;
using EduTech.Students.Admissions;
using EduTech.Students.Classes;
using EduTech.Students.ParentFacing;
using EduTech.Students.Students;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Students;

/// <summary>Registers the Students module (academic calendar + classes + student records).</summary>
public static class StudentsServiceCollectionExtensions
{
    public static IServiceCollection AddStudentsModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAcademicCalendarRepository, AcademicCalendarRepository>();
        services.AddScoped<IAcademicCalendarService, AcademicCalendarService>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IParentChildrenRepository, ParentChildrenRepository>();
        services.AddScoped<IParentChildrenService, ParentChildrenService>();
        services.AddScoped<IParentApplicationRepository, ParentApplicationRepository>();
        services.AddScoped<IParentApplicationService, ParentApplicationService>();
        services.AddScoped<ISchoolApplicationRepository, SchoolApplicationRepository>();
        services.AddScoped<ISchoolApplicationService, SchoolApplicationService>();

        services.AddControllers()
            .AddApplicationPart(typeof(StudentsServiceCollectionExtensions).Assembly);

        return services;
    }
}
