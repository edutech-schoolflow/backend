using EduTech.Admissions.Applications;
using EduTech.Admissions.Domain;
using EduTech.Admissions.Offers;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Enrollments;

/// <summary>
/// Enrollment commands + queries (EDD-014 Slice 8) — the platform transition. Enrolling an accepted
/// application raises StudentEnrolled, the only bridge to the Students module.
/// </summary>
public interface IEnrollmentService
{
    Task<EnrollmentResponse> EnrollAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<EnrollmentResponse> CancelAsync(Guid applicationId, string reason, CancellationToken cancellationToken);
    Task<EnrollmentResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class EnrollmentService : IEnrollmentService
{
    private readonly IEnrollmentRepository _enrollments;
    private readonly IApplicationService _applications;
    private readonly IOfferRepository _offers;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public EnrollmentService(IEnrollmentRepository enrollments, IApplicationService applications,
        IOfferRepository offers, IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _enrollments = enrollments;
        _applications = applications;
        _offers = offers;
        _events = events;
        _context = context;
    }

    public async Task<EnrollmentResponse> EnrollAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationResponse application = await _applications.GetAsync(applicationId, cancellationToken);
        if (application.Status != Domain.ApplicationStatus.Accepted)
        {
            throw new AppErrorException("Only an accepted application can enroll.", 409, ErrorCodes.Conflict);
        }
        if (await _enrollments.GetByApplicationAsync(applicationId, cancellationToken) is not null)
        {
            throw new AppErrorException("This application is already enrolled.", 409, ErrorCodes.Conflict);
        }

        // The accepted offer supplies the class / academic year for the handoff.
        IReadOnlyList<Offer> offers = await _offers.ListForApplicationAsync(applicationId, cancellationToken);
        Offer? accepted = offers.FirstOrDefault(o => o.Status == OfferStatus.Accepted);

        Guid enrollmentId = await _enrollments.EnrollAsync(applicationId, accepted?.Id, application.ChildProfileId, cancellationToken);
        await _applications.MarkEnrolledAsync(applicationId, cancellationToken);

        Guid schoolId = Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;
        await _events.PublishAsync(new StudentEnrolled(enrollmentId, applicationId, schoolId,
            application.ChildProfileId, application.ProspectiveName, application.DateOfBirth, application.Gender,
            application.GuardianPhone, accepted?.ClassId, accepted?.AcademicYear), cancellationToken);

        return Map(await LoadAsync(applicationId, cancellationToken));
    }

    public async Task<EnrollmentResponse> CancelAsync(Guid applicationId, string reason, CancellationToken cancellationToken)
    {
        Enrollment enrollment = await LoadAsync(applicationId, cancellationToken);
        enrollment.Cancel(reason);
        await _enrollments.SaveAsync(enrollment, cancellationToken);
        return Map(enrollment);
    }

    public async Task<EnrollmentResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
        Map(await LoadAsync(applicationId, cancellationToken));

    private async Task<Enrollment> LoadAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await _enrollments.GetByApplicationAsync(applicationId, cancellationToken)
        ?? throw new AppErrorException("Enrollment not found.", 404, ErrorCodes.NotFound);

    private static EnrollmentResponse Map(Enrollment e) => new()
    {
        Id = e.Id,
        ApplicationId = e.ApplicationId,
        OfferId = e.OfferId,
        ChildProfileId = e.ChildProfileId,
        Status = e.Status,
        CancelledReason = e.CancelledReason,
        EnrolledAt = e.EnrolledAt
    };
}
