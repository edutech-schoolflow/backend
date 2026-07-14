using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Constants;

namespace EduTech.Students.Admissions;

public interface IParentApplicationService
{
    Task<ApplicationResponse> SubmitAsync(SubmitApplicationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The signed-in IDENTITY's applications across schools (EDD-002 identity-space view).
    /// Empty until they've created a parent profile / applied. Authorized by the identity session.</summary>
    Task<IReadOnlyList<ApplicationResponse>> ListMineAsync(CancellationToken cancellationToken);
    Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<ApplicationResponse> PayAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class ParentApplicationService : IParentApplicationService
{
    private readonly IParentApplicationRepository _repository;
    private readonly IEduTechRequestContext _context;

    public ParentApplicationService(IParentApplicationRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<ApplicationResponse> SubmitAsync(SubmitApplicationRequest request, CancellationToken cancellationToken)
    {
        if (request.ChildProfileId == Guid.Empty || request.SchoolId == Guid.Empty)
        {
            throw new AppErrorException("Child and school are required.", 400, ErrorCodes.ValidationError);
        }

        Guid parentId = await RequireParentIdAsync(cancellationToken);
        if (!await _repository.ParentOwnsChildAsync(parentId, request.ChildProfileId, cancellationToken))
        {
            throw new AppErrorException("Child not found.", 404, ErrorCodes.NotFound);
        }

        if (!await _repository.SchoolExistsAsync(request.SchoolId, cancellationToken))
        {
            throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);
        }

        if (await _repository.ChildActiveAtSchoolAsync(request.ChildProfileId, request.SchoolId, cancellationToken))
        {
            throw new AppErrorException("This child is already enrolled at that school.", 409, ErrorCodes.Conflict);
        }

        if (await _repository.HasOpenApplicationAsync(request.ChildProfileId, request.SchoolId, cancellationToken))
        {
            throw new AppErrorException("There's already an open application for this child at that school.",
                409, ErrorCodes.Conflict);
        }

        ApplicationRow row = await _repository.SubmitAsync(parentId, request.ChildProfileId, request.SchoolId,
            request.DesiredClass?.Trim(), request.TermId, cancellationToken);
        return ApplicationMapper.Map(row);
    }

    public Task<IReadOnlyList<ApplicationResponse>> ListAsync(CancellationToken cancellationToken)
        => ListMineAsync(cancellationToken);

    public async Task<IReadOnlyList<ApplicationResponse>> ListMineAsync(CancellationToken cancellationToken)
    {
        Guid? parentId = await _repository.GetParentIdByIdentityAsync(CurrentIdentityId, cancellationToken);
        if (parentId is not Guid pid)
        {
            return Array.Empty<ApplicationResponse>();
        }

        IReadOnlyList<ApplicationRow> rows = await _repository.ListByParentAsync(pid, cancellationToken);
        return rows.Select(ApplicationMapper.Map).ToList();
    }

    public async Task<ApplicationResponse> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        Guid parentId = await RequireParentIdAsync(cancellationToken);
        ApplicationRow row = await _repository.GetForParentAsync(parentId, applicationId, cancellationToken)
            ?? throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        return ApplicationMapper.Map(row);
    }

    public async Task<ApplicationResponse> PayAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        // Stub: no real money yet (Monnify lands with the Fees module). Mark paid + a placeholder ref.
        Guid parentId = await RequireParentIdAsync(cancellationToken);
        int changed = await _repository.MarkPaidAsync(parentId, applicationId, $"STUB-{Guid.NewGuid():N}", cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        return await GetAsync(applicationId, cancellationToken);
    }

    /// <summary>The identity's parent profile — 404 (don't reveal) when they have none yet.</summary>
    private async Task<Guid> RequireParentIdAsync(CancellationToken cancellationToken)
    {
        return await _repository.GetParentIdByIdentityAsync(CurrentIdentityId, cancellationToken)
            ?? throw new AppErrorException("Child not found.", 404, ErrorCodes.NotFound,
                logReason: "Family applications: identity has no parent profile.");
    }

    // The identity behind the session: identity_id claim (org tokens) or user_id (identity session).
    private Guid CurrentIdentityId =>
        Guid.TryParse(_context.IdentityId ?? _context.UserId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
}
