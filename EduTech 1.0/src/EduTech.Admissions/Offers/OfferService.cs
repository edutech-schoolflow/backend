using EduTech.Admissions.Applications;
using EduTech.Admissions.Decisions;
using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Offers;

/// <summary>Offer commands + queries (EDD-014 Slice 7). Issued from an approved/conditional decision.</summary>
public interface IOfferService
{
    Task<OfferResponse> IssueAsync(Guid applicationId, IssueOfferRequest request, CancellationToken cancellationToken);
    Task<OfferResponse> AcceptAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken);
    Task<OfferResponse> DeclineAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken);
    Task<OfferResponse> WithdrawAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OfferResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class OfferService : IOfferService
{
    private readonly IOfferRepository _offers;
    private readonly IDecisionRepository _decisions;
    private readonly IApplicationService _applications;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public OfferService(IOfferRepository offers, IDecisionRepository decisions, IApplicationService applications,
        IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _offers = offers;
        _decisions = decisions;
        _applications = applications;
        _events = events;
        _context = context;
    }

    public async Task<OfferResponse> IssueAsync(Guid applicationId, IssueOfferRequest request, CancellationToken cancellationToken)
    {
        // An offer may only follow an approved/conditional decision.
        Decision? latest = await _decisions.GetLatestAsync(applicationId, cancellationToken);
        if (latest is null || !latest.CanProduceOffer)
        {
            throw new AppErrorException("Approve the application before issuing an offer.", 409, ErrorCodes.Conflict);
        }

        // At most one outstanding offer per application.
        if (await _offers.GetActiveAsync(applicationId, cancellationToken) is not null)
        {
            throw new AppErrorException("An offer is already outstanding for this application.", 409, ErrorCodes.Conflict);
        }

        Guid id = await _offers.IssueAsync(applicationId, new NewOffer
        {
            DecisionId = latest.Id,
            Campus = request.Campus,
            ClassId = request.ClassId,
            AcademicYear = request.AcademicYear,
            FeePlan = request.FeePlan,
            Scholarship = request.Scholarship,
            Conditions = request.Conditions,
            AcceptanceDeadline = request.AcceptanceDeadline
        }, cancellationToken);
        if (id == Guid.Empty)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        await _applications.MarkOfferedAsync(applicationId, cancellationToken);
        await _events.PublishAsync(new OfferIssued(id, applicationId, SchoolId()), cancellationToken);
        return Map(await LoadAsync(applicationId, id, cancellationToken));
    }

    public async Task<OfferResponse> AcceptAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        Offer offer = await LoadAsync(applicationId, offerId, cancellationToken);
        offer.Accept(DateTime.UtcNow);
        await _offers.SaveAsync(offer, cancellationToken);

        // Accepting moves the application to 'accepted'; enrollment (Slice 8) finalizes the place.
        await _applications.MarkAcceptedAsync(applicationId, cancellationToken);
        await _events.PublishAsync(new OfferAccepted(offer.Id, applicationId, SchoolId()), cancellationToken);
        return Map(offer);
    }

    public async Task<OfferResponse> DeclineAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        Offer offer = await LoadAsync(applicationId, offerId, cancellationToken);
        offer.Decline(DateTime.UtcNow);
        await _offers.SaveAsync(offer, cancellationToken);
        await _events.PublishAsync(new OfferDeclined(offer.Id, applicationId, SchoolId()), cancellationToken);
        return Map(offer);
    }

    public async Task<OfferResponse> WithdrawAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken)
    {
        Offer offer = await LoadAsync(applicationId, offerId, cancellationToken);
        offer.Withdraw();
        await _offers.SaveAsync(offer, cancellationToken);
        return Map(offer);
    }

    public async Task<IReadOnlyList<OfferResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Offer> list = await _offers.ListForApplicationAsync(applicationId, cancellationToken);
        return list.Select(Map).ToList();
    }

    private async Task<Offer> LoadAsync(Guid applicationId, Guid offerId, CancellationToken cancellationToken) =>
        await _offers.GetAsync(applicationId, offerId, cancellationToken)
        ?? throw new AppErrorException("Offer not found.", 404, ErrorCodes.NotFound);

    private Guid SchoolId() => Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;

    private static OfferResponse Map(Offer o) => new()
    {
        Id = o.Id,
        ApplicationId = o.ApplicationId,
        DecisionId = o.DecisionId,
        Campus = o.Campus,
        ClassId = o.ClassId,
        AcademicYear = o.AcademicYear,
        FeePlan = o.FeePlan,
        Scholarship = o.Scholarship,
        Conditions = o.Conditions,
        AcceptanceDeadline = o.AcceptanceDeadline,
        Status = o.Status,
        RespondedAt = o.RespondedAt,
        CreatedAt = o.CreatedAt
    };
}
