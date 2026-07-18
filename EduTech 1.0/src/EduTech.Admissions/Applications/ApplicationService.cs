using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Applications;

/// <summary>Application commands + queries (EDD-014 Part 9). Slice 3: draft → submit → withdraw.</summary>
public interface IApplicationService
{
    Task<ApplicationResponse> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse> SubmitAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<ApplicationResponse> WithdrawAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationResponse>> ListAsync(Guid? cycleId, string? status, CancellationToken cancellationToken);

    /// <summary>Creates a draft application from a converted inquiry (used within the module).</summary>
    Task<ApplicationResponse> CreateFromInquiryAsync(Guid cycleId, Guid inquiryId, string prospectiveName,
        string? guardianName, string guardianPhone, CancellationToken cancellationToken);
}

internal sealed class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _applications;
    private readonly IDomainEventPublisher _events;

    public ApplicationService(IApplicationRepository applications, IDomainEventPublisher events)
    {
        _applications = applications;
        _events = events;
    }

    public async Task<ApplicationResponse> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        Validate(request.CycleId, request.ProspectiveName, request.GuardianPhone);
        Guid id = await _applications.CreateDraftAsync(new NewApplication
        {
            CycleId = request.CycleId,
            ProspectiveName = request.ProspectiveName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            GuardianName = request.GuardianName,
            GuardianPhone = request.GuardianPhone.Trim(),
            PreferredClass = request.PreferredClass
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ApplicationResponse> CreateFromInquiryAsync(Guid cycleId, Guid inquiryId,
        string prospectiveName, string? guardianName, string guardianPhone, CancellationToken cancellationToken)
    {
        Validate(cycleId, prospectiveName, guardianPhone);
        Guid id = await _applications.CreateDraftAsync(new NewApplication
        {
            CycleId = cycleId,
            SourceInquiryId = inquiryId,
            ProspectiveName = prospectiveName.Trim(),
            GuardianName = guardianName,
            GuardianPhone = guardianPhone.Trim()
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ApplicationResponse> SubmitAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        Application application = await LoadAsync(applicationId, cancellationToken);
        application.Submit(DateTime.UtcNow);
        await _applications.SaveAsync(application, cancellationToken);
        await _events.PublishAsync(new ApplicationSubmitted(application.Id, application.OrganizationId,
            application.CycleId, application.ProspectiveName), cancellationToken);
        return Map(application);
    }

    public async Task<ApplicationResponse> WithdrawAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        Application application = await LoadAsync(applicationId, cancellationToken);
        application.Withdraw();
        await _applications.SaveAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
        Map(await LoadAsync(applicationId, cancellationToken));

    public async Task<IReadOnlyList<ApplicationResponse>> ListAsync(Guid? cycleId, string? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Application> apps = await _applications.ListAsync(cycleId, status, cancellationToken);
        return apps.Select(Map).ToList();
    }

    private async Task<Application> LoadAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await _applications.GetByIdAsync(applicationId, cancellationToken)
        ?? throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);

    private static void Validate(Guid cycleId, string? name, string? phone)
    {
        if (cycleId == Guid.Empty)
        {
            throw new AppErrorException("An application must belong to an admission cycle.", 400, ErrorCodes.ValidationError);
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppErrorException("Enter the applicant's name.", 400, ErrorCodes.ValidationError);
        }
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new AppErrorException("Enter a guardian contact phone.", 400, ErrorCodes.ValidationError);
        }
    }

    private static ApplicationResponse Map(Application a) => new()
    {
        Id = a.Id,
        CycleId = a.CycleId,
        ChildProfileId = a.ChildProfileId,
        SourceInquiryId = a.SourceInquiryId,
        ProspectiveName = a.ProspectiveName,
        DateOfBirth = a.DateOfBirth,
        Gender = a.Gender,
        GuardianName = a.GuardianName,
        GuardianPhone = a.GuardianPhone,
        PreferredClass = a.PreferredClass,
        Status = a.Status,
        SubmittedAt = a.SubmittedAt,
        CreatedAt = a.CreatedAt
    };
}
