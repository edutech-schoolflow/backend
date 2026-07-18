using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Inquiries;

/// <summary>Inquiry commands + queries (EDD-014 Part 9). School is resolved from the token.</summary>
public interface IInquiryService
{
    Task<InquiryResponse> CreateAsync(CreateInquiryRequest request, CancellationToken cancellationToken);
    Task<InquiryResponse> MarkContactedAsync(Guid inquiryId, CancellationToken cancellationToken);
    Task<InquiryResponse> BookVisitAsync(Guid inquiryId, DateTime? visitAt, CancellationToken cancellationToken);
    Task<InquiryResponse> CloseAsync(Guid inquiryId, CancellationToken cancellationToken);
    Task<InquiryResponse> GetAsync(Guid inquiryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InquiryResponse>> ListAsync(string? status, CancellationToken cancellationToken);
}

internal sealed class InquiryService : IInquiryService
{
    private readonly IInquiryRepository _inquiries;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public InquiryService(IInquiryRepository inquiries, IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _inquiries = inquiries;
        _events = events;
        _context = context;
    }

    public async Task<InquiryResponse> CreateAsync(CreateInquiryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProspectiveName))
        {
            throw new AppErrorException("Enter the prospective learner's name.", 400, ErrorCodes.ValidationError);
        }
        if (string.IsNullOrWhiteSpace(request.GuardianPhone))
        {
            throw new AppErrorException("Enter a contact phone.", 400, ErrorCodes.ValidationError);
        }

        Guid id = await _inquiries.CreateAsync(request.CycleId, request.ProspectiveName.Trim(),
            request.GuardianName, request.GuardianPhone.Trim(), request.Notes, cancellationToken);

        Guid schoolId = Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;
        await _events.PublishAsync(new InquiryCreated(id, schoolId, request.ProspectiveName.Trim()), cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public Task<InquiryResponse> MarkContactedAsync(Guid inquiryId, CancellationToken cancellationToken) =>
        MutateAsync(inquiryId, i => i.MarkContacted(), cancellationToken);

    public Task<InquiryResponse> BookVisitAsync(Guid inquiryId, DateTime? visitAt, CancellationToken cancellationToken) =>
        MutateAsync(inquiryId, i => i.BookVisit(visitAt), cancellationToken);

    public Task<InquiryResponse> CloseAsync(Guid inquiryId, CancellationToken cancellationToken) =>
        MutateAsync(inquiryId, i => i.Close(), cancellationToken);

    public async Task<InquiryResponse> GetAsync(Guid inquiryId, CancellationToken cancellationToken)
    {
        Inquiry inquiry = await _inquiries.GetByIdAsync(inquiryId, cancellationToken)
            ?? throw new AppErrorException("Inquiry not found.", 404, ErrorCodes.NotFound);
        return Map(inquiry);
    }

    public async Task<IReadOnlyList<InquiryResponse>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<Inquiry> inquiries = await _inquiries.ListAsync(status, cancellationToken);
        return inquiries.Select(Map).ToList();
    }

    private async Task<InquiryResponse> MutateAsync(Guid inquiryId, Action<Inquiry> mutate,
        CancellationToken cancellationToken)
    {
        Inquiry inquiry = await _inquiries.GetByIdAsync(inquiryId, cancellationToken)
            ?? throw new AppErrorException("Inquiry not found.", 404, ErrorCodes.NotFound);
        mutate(inquiry);
        await _inquiries.SaveAsync(inquiry, cancellationToken);
        return Map(inquiry);
    }

    private static InquiryResponse Map(Inquiry i) => new()
    {
        Id = i.Id,
        CycleId = i.CycleId,
        ProspectiveName = i.ProspectiveName,
        GuardianName = i.GuardianName,
        GuardianPhone = i.GuardianPhone,
        Notes = i.Notes,
        VisitAt = i.VisitAt,
        Status = i.Status,
        ConvertedApplicationId = i.ConvertedApplicationId,
        CreatedAt = i.CreatedAt
    };
}
