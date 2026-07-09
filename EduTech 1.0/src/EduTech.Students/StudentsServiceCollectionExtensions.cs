using EduTech.Shared.Events;
using EduTech.Students.Academics;
using EduTech.Students.Academics.Transition;
using EduTech.Students.Admissions;
using EduTech.Students.Admissions.Events;
using EduTech.Students.Classes;
using EduTech.Students.ParentFacing;
using EduTech.Students.Students;
using EduTech.Students.Students.Commands;
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
        // The port behind [RequiresCurrentTerm] — an Academics question, answered by Academics (EDD-002 V5).
        services.AddScoped<EduTech.Shared.Auth.ICurrentTermProvider, CurrentTermProvider>();
        services.AddScoped<IAcademicTransitionService, AcademicTransitionService>();
        services.AddScoped<ICalendarRollForwardRepository, CalendarRollForwardRepository>();
        services.AddScoped<ISchoolCalendarProvisioner, SchoolCalendarProvisioner>();
        services.AddScoped<CalendarRollForwardJob>();   // daily sweep; scheduled in Program.cs
        // Observer: provision a calendar the moment a school is activated (approved + made visible).
        services.AddScoped<IDomainEventHandler<SchoolActivatedEvent>, ProvisionCalendarOnSchoolActivated>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<ISchoolClassProvisionRepository, SchoolClassProvisionRepository>();
        services.AddScoped<ISchoolClassProvisioner, SchoolClassProvisioner>();
        // Observer: provision the standard 6-3-3 classes when a school is activated (alongside the calendar).
        services.AddScoped<IDomainEventHandler<SchoolActivatedEvent>, ProvisionClassesOnSchoolActivated>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<StudentCommandInvoker>();   // Command: runs lifecycle actions + audits them
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IParentChildrenRepository, ParentChildrenRepository>();
        services.AddScoped<IParentChildrenService, ParentChildrenService>();
        services.AddScoped<IParentApplicationRepository, ParentApplicationRepository>();
        services.AddScoped<IParentApplicationService, ParentApplicationService>();
        services.AddScoped<IParentSchoolDirectoryRepository, ParentSchoolDirectoryRepository>();
        services.AddScoped<IParentSchoolDirectoryService, ParentSchoolDirectoryService>();
        services.AddScoped<ISchoolApplicationRepository, SchoolApplicationRepository>();
        services.AddScoped<ISchoolApplicationService, SchoolApplicationService>();

        // Observers for admission decisions (the audit observer is registered globally via AddAuditLog).
        services.AddScoped<IDomainEventHandler<ExamScheduledEvent>, AdmissionNotificationHandler>();
        services.AddScoped<IDomainEventHandler<ApplicationAdmittedEvent>, AdmissionNotificationHandler>();
        services.AddScoped<IDomainEventHandler<ApplicationRejectedEvent>, AdmissionNotificationHandler>();

        services.AddControllers()
            .AddApplicationPart(typeof(StudentsServiceCollectionExtensions).Assembly);

        return services;
    }
}
