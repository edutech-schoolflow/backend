using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Assessments;

/// <summary>Assessment commands + queries (EDD-014 Slice 5).</summary>
public interface IAssessmentService
{
    Task<AssessmentResponse> ScheduleAsync(Guid applicationId, ScheduleAssessmentRequest request, CancellationToken cancellationToken);
    Task<AssessmentResponse> RescheduleAsync(Guid applicationId, Guid assessmentId, DateTime? scheduledAt, CancellationToken cancellationToken);
    Task<AssessmentResponse> RecordResultAsync(Guid applicationId, Guid assessmentId, RecordResultRequest request, CancellationToken cancellationToken);
    Task<AssessmentResponse> CancelAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssessmentResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class AssessmentService : IAssessmentService
{
    private readonly IAssessmentRepository _assessments;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public AssessmentService(IAssessmentRepository assessments, IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _assessments = assessments;
        _events = events;
        _context = context;
    }

    public async Task<AssessmentResponse> ScheduleAsync(Guid applicationId, ScheduleAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        Guid id = await _assessments.ScheduleAsync(applicationId, AssessmentRepository.TypeToDb(request.Type),
            request.ScheduledAt, cancellationToken);
        if (id == Guid.Empty)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        Assessment assessment = await LoadAsync(applicationId, id, cancellationToken);
        await _events.PublishAsync(new AssessmentScheduled(assessment.Id, applicationId, SchoolId(),
            AssessmentRepository.TypeToDb(assessment.Type)), cancellationToken);
        return Map(assessment);
    }

    public async Task<AssessmentResponse> RescheduleAsync(Guid applicationId, Guid assessmentId, DateTime? scheduledAt,
        CancellationToken cancellationToken)
    {
        Assessment assessment = await LoadAsync(applicationId, assessmentId, cancellationToken);
        assessment.Reschedule(scheduledAt);
        await _assessments.SaveAsync(assessment, cancellationToken);
        return Map(assessment);
    }

    public async Task<AssessmentResponse> RecordResultAsync(Guid applicationId, Guid assessmentId,
        RecordResultRequest request, CancellationToken cancellationToken)
    {
        Assessment assessment = await LoadAsync(applicationId, assessmentId, cancellationToken);
        assessment.RecordResult(request.Outcome, request.Score, request.Notes, DateTime.UtcNow);
        await _assessments.SaveAsync(assessment, cancellationToken);
        await _events.PublishAsync(new AssessmentCompleted(assessment.Id, applicationId, SchoolId(),
            AssessmentRepository.TypeToDb(assessment.Type), assessment.Outcome ?? string.Empty), cancellationToken);
        return Map(assessment);
    }

    public async Task<AssessmentResponse> CancelAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken)
    {
        Assessment assessment = await LoadAsync(applicationId, assessmentId, cancellationToken);
        assessment.Cancel();
        await _assessments.SaveAsync(assessment, cancellationToken);
        return Map(assessment);
    }

    public async Task<IReadOnlyList<AssessmentResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Assessment> list = await _assessments.ListForApplicationAsync(applicationId, cancellationToken);
        return list.Select(Map).ToList();
    }

    private async Task<Assessment> LoadAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken) =>
        await _assessments.GetAsync(applicationId, assessmentId, cancellationToken)
        ?? throw new AppErrorException("Assessment not found.", 404, ErrorCodes.NotFound);

    private Guid SchoolId() => Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;

    private static AssessmentResponse Map(Assessment a) => new()
    {
        Id = a.Id,
        ApplicationId = a.ApplicationId,
        Type = a.Type,
        ScheduledAt = a.ScheduledAt,
        Status = a.Status,
        Outcome = a.Outcome,
        Score = a.Score,
        ResultNotes = a.ResultNotes,
        RecordedAt = a.RecordedAt,
        CreatedAt = a.CreatedAt
    };
}
