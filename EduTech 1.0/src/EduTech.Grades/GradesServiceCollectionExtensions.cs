using EduTech.Grades.ReportCards;
using EduTech.Grades.Scores;
using EduTech.Grades.Subjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Grades;

/// <summary>Registers the Grades module (subject catalog + term score entry).</summary>
public static class GradesServiceCollectionExtensions
{
    public static IServiceCollection AddGradesModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IGradeRepository, GradeRepository>();
        services.AddScoped<IGradeService, GradeService>();
        services.AddScoped<IGradingScaleRepository, GradingScaleRepository>();
        services.AddScoped<IGradingScaleService, GradingScaleService>();
        services.AddScoped<IReportCardRepository, ReportCardRepository>();
        services.AddScoped<IReportCardService, ReportCardService>();

        services.AddControllers()
            .AddApplicationPart(typeof(GradesServiceCollectionExtensions).Assembly);

        return services;
    }
}
