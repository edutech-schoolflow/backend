using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Students.Admissions.Events;

namespace EduTech.Students.Admissions;

public interface ISchoolApplicationService
{
    Task<IReadOnlyList<ApplicationResponse>> ListAsync(string? status, CancellationToken cancellationToken);
    Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<ApplicationResponse> ScheduleExamAsync(Guid applicationId, ScheduleExamRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse> RecordAssessmentAsync(Guid applicationId, RecordAssessmentRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse> AdmitAsync(Guid applicationId, AdmitApplicationRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse> RejectAsync(Guid applicationId, RejectApplicationRequest request, CancellationToken cancellationToken);
}

internal sealed class SchoolApplicationService : ISchoolApplicationService
{
    private readonly ISchoolApplicationRepository _repository;
    private readonly IDomainEventPublisher _events;

    public SchoolApplicationService(ISchoolApplicationRepository repository, IDomainEventPublisher events)
    {
        _repository = repository;
        _events = events;
    }

    public async Task<IReadOnlyList<ApplicationResponse>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        string? filter = SnakeCaseEnum.TryParse(status, out ApplicationStatus parsed) ? SnakeCaseEnum.ToWire(parsed) : null;
        IReadOnlyList<ApplicationRow> rows = await _repository.ListAsync(filter, cancellationToken);
        return rows.Select(ApplicationMapper.Map).ToList();
    }

    public async Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        ApplicationRow row = await _repository.GetAsync(applicationId, cancellationToken)
            ?? throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        return ApplicationMapper.Map(row);
    }

    public async Task<ApplicationResponse> ScheduleExamAsync(Guid applicationId, ScheduleExamRequest request,
        CancellationToken cancellationToken)
    {
        ApplicationStatus from = await RequireTransitionAsync(applicationId, ApplicationStatus.ExamScheduled, cancellationToken);

        int changed = await _repository.ScheduleExamAsync(applicationId, from, request.ExamDate,
            request.ExamTime?.Trim(), request.ExamVenue?.Trim(), request.ExamInstructions?.Trim(), cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Application status changed, please retry.", 409, ErrorCodes.Conflict);
        }

        ApplicationNotifyRow? target = await _repository.GetNotifyTargetAsync(applicationId, cancellationToken);
        if (target is not null)
        {
            await _events.PublishAsync(new ExamScheduledEvent
            {
                ApplicationId = applicationId, ChildName = target.ChildName, Phone = target.Phone,
                ExamDate = request.ExamDate
            }, cancellationToken);
        }
        return await GetAsync(applicationId, cancellationToken);
    }

    public async Task<ApplicationResponse> RecordAssessmentAsync(Guid applicationId, RecordAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        if (await _repository.GetStatusAsync(applicationId, cancellationToken) is null)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        string? rating = request.Rating is AssessmentRating r ? SnakeCaseEnum.ToWire(r) : null;
        int changed = await _repository.RecordAssessmentAsync(applicationId, rating, request.Notes?.Trim(), cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Assessment can only be recorded while the application is under review.",
                409, ErrorCodes.Conflict);
        }

        return await GetAsync(applicationId, cancellationToken);
    }

    public async Task<ApplicationResponse> AdmitAsync(Guid applicationId, AdmitApplicationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClassId == Guid.Empty || !await _repository.ClassExistsAsync(request.ClassId, cancellationToken))
        {
            throw new AppErrorException("A valid class is required.", 400, ErrorCodes.ValidationError);
        }

        if (request.ClassArmId is Guid armId
            && !await _repository.ArmInClassAsync(armId, request.ClassId, cancellationToken))
        {
            throw new AppErrorException("The selected arm doesn't belong to that class.", 400, ErrorCodes.ValidationError);
        }

        ApplicationStatus from = await RequireTransitionAsync(applicationId, ApplicationStatus.Admitted, cancellationToken);
        // Admit has a side effect (creates a student), so re-admitting an already-admitted app must fail
        // rather than be a silent no-op (the generic guard treats from==to as idempotent).
        if (from == ApplicationStatus.Admitted)
        {
            throw new AppErrorException("This application has already been admitted.", 409, ErrorCodes.Conflict);
        }

        string admissionNumber = await _repository.AdmitAsync(applicationId, from, request.ClassId, request.ClassArmId, cancellationToken);

        ApplicationNotifyRow? target = await _repository.GetNotifyTargetAsync(applicationId, cancellationToken);
        if (target is not null)
        {
            await _events.PublishAsync(new ApplicationAdmittedEvent
            {
                ApplicationId = applicationId, ChildName = target.ChildName, Phone = target.Phone,
                AdmissionNumber = admissionNumber
            }, cancellationToken);
        }
        return await GetAsync(applicationId, cancellationToken);
    }

    public async Task<ApplicationResponse> RejectAsync(Guid applicationId, RejectApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ApplicationStatus from = await RequireTransitionAsync(applicationId, ApplicationStatus.Rejected, cancellationToken);

        int changed = await _repository.RejectAsync(applicationId, from, request.Reason?.Trim(), cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Application status changed, please retry.", 409, ErrorCodes.Conflict);
        }

        ApplicationNotifyRow? target = await _repository.GetNotifyTargetAsync(applicationId, cancellationToken);
        if (target is not null)
        {
            await _events.PublishAsync(new ApplicationRejectedEvent
            {
                ApplicationId = applicationId, ChildName = target.ChildName, Phone = target.Phone,
                Reason = request.Reason?.Trim()
            }, cancellationToken);
        }
        return await GetAsync(applicationId, cancellationToken);
    }

    private async Task<ApplicationStatus> RequireTransitionAsync(Guid applicationId, ApplicationStatus target,
        CancellationToken cancellationToken)
    {
        string? raw = await _repository.GetStatusAsync(applicationId, cancellationToken)
            ?? throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);

        ApplicationStatus current = SnakeCaseEnum.Parse<ApplicationStatus>(raw);
        ApplicationLifecycle.Rules.Require(current, target);   // 409 on an illegal move
        return current;
    }
}
