using EduTech.Admissions.Applications;
using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Decisions;

/// <summary>Decision commands + queries (EDD-014 Slice 6).</summary>
public interface IDecisionService
{
    Task<DecisionResponse> RecordAsync(Guid applicationId, RecordDecisionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DecisionResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class DecisionService : IDecisionService
{
    private readonly IDecisionRepository _decisions;
    private readonly IApplicationService _applications;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public DecisionService(IDecisionRepository decisions, IApplicationService applications,
        IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _decisions = decisions;
        _applications = applications;
        _events = events;
        _context = context;
    }

    public async Task<DecisionResponse> RecordAsync(Guid applicationId, RecordDecisionRequest request,
        CancellationToken cancellationToken)
    {
        // Validate the outcome/conditions rules in the aggregate before persisting.
        Decision decision = new(Guid.NewGuid(), applicationId, request.Outcome, request.Conditions,
            request.Notes, decidedBy: null, DateTime.UtcNow);

        string outcome = DecisionRepository.OutcomeToDb(decision.Outcome);
        Guid id = await _decisions.RecordAsync(applicationId, outcome, decision.Conditions, decision.Notes, cancellationToken);
        if (id == Guid.Empty)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        // The decision moves the application to 'decided' and announces the review outcome.
        await _applications.MarkDecidedAsync(applicationId, cancellationToken);
        Guid schoolId = Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;
        await _events.PublishAsync(new ApplicationReviewed(applicationId, schoolId, outcome), cancellationToken);

        return new DecisionResponse
        {
            Id = id,
            ApplicationId = applicationId,
            Outcome = decision.Outcome,
            Conditions = decision.Conditions,
            Notes = decision.Notes,
            DecidedAt = decision.DecidedAt
        };
    }

    public async Task<IReadOnlyList<DecisionResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Decision> list = await _decisions.ListForApplicationAsync(applicationId, cancellationToken);
        return list.Select(d => new DecisionResponse
        {
            Id = d.Id,
            ApplicationId = d.ApplicationId,
            Outcome = d.Outcome,
            Conditions = d.Conditions,
            Notes = d.Notes,
            DecidedAt = d.DecidedAt
        }).ToList();
    }
}
