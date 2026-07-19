using EduTech.Admissions.Applications;
using EduTech.Admissions.Assessments;
using EduTech.Admissions.Cycles;
using EduTech.Admissions.Decisions;
using EduTech.Admissions.Documents;
using EduTech.Admissions.Inquiries;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Admissions;

/// <summary>
/// Registers the Admissions module (EDD-014) — the reference Layer-3 module. It depends only on the
/// platform (contracts + services) and grows one vertical slice at a time. Slice 1: Admission Cycles.
/// </summary>
public static class AdmissionsServiceCollectionExtensions
{
    public static IServiceCollection AddAdmissionsModule(this IServiceCollection services)
    {
        services.AddScoped<IAdmissionCycleRepository, AdmissionCycleRepository>();
        services.AddScoped<IAdmissionCycleService, AdmissionCycleService>();
        services.AddScoped<IInquiryRepository, InquiryRepository>();
        services.AddScoped<IInquiryService, InquiryService>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationDocumentRepository, ApplicationDocumentRepository>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IAssessmentRepository, AssessmentRepository>();
        services.AddScoped<IAssessmentService, AssessmentService>();
        services.AddScoped<IDecisionRepository, DecisionRepository>();
        services.AddScoped<IDecisionService, DecisionService>();
        return services;
    }
}
