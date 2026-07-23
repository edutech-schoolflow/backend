using EduTech.Admissions.Domain;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Cycles;

/// <summary>Admission-cycle commands + queries (EDD-014 Part 9). School is resolved from the token.</summary>
public interface IAdmissionCycleService
{
    Task<AdmissionCycleResponse> CreateAsync(CreateAdmissionCycleRequest request, CancellationToken cancellationToken);
    Task<AdmissionCycleResponse> OpenAsync(Guid cycleId, CancellationToken cancellationToken);
    Task<AdmissionCycleResponse> CloseAsync(Guid cycleId, CancellationToken cancellationToken);
    Task<AdmissionCycleResponse> ArchiveAsync(Guid cycleId, CancellationToken cancellationToken);
    Task<AdmissionCycleResponse> SetQuotaAsync(Guid cycleId, int? quota, CancellationToken cancellationToken);
    Task<AdmissionCycleResponse> GetAsync(Guid cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionCycleResponse>> ListAsync(string? status, CancellationToken cancellationToken);
}

internal sealed class AdmissionCycleService : IAdmissionCycleService
{
    private readonly IAdmissionCycleRepository _cycles;

    public AdmissionCycleService(IAdmissionCycleRepository cycles)
    {
        _cycles = cycles;
    }

    public async Task<AdmissionCycleResponse> CreateAsync(CreateAdmissionCycleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppErrorException("An admission cycle needs a name.", 400, ErrorCodes.ValidationError);
        }
        if (request.Quota is < 0)
        {
            throw new AppErrorException("Quota cannot be negative.", 400, ErrorCodes.ValidationError);
        }

        Guid id = await _cycles.CreateAsync(request.Name.Trim(), request.IntakeType, request.OpensAt,
            request.ClosesAt, request.Quota, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<AdmissionCycleResponse> OpenAsync(Guid cycleId, CancellationToken cancellationToken) =>
        MutateAsync(cycleId, c => c.Open(), cancellationToken);

    public Task<AdmissionCycleResponse> CloseAsync(Guid cycleId, CancellationToken cancellationToken) =>
        MutateAsync(cycleId, c => c.Close(), cancellationToken);

    public Task<AdmissionCycleResponse> ArchiveAsync(Guid cycleId, CancellationToken cancellationToken) =>
        MutateAsync(cycleId, c => c.Archive(), cancellationToken);

    public Task<AdmissionCycleResponse> SetQuotaAsync(Guid cycleId, int? quota, CancellationToken cancellationToken) =>
        MutateAsync(cycleId, c => c.SetQuota(quota), cancellationToken);

    public async Task<AdmissionCycleResponse> GetAsync(Guid cycleId, CancellationToken cancellationToken)
    {
        AdmissionCycle cycle = await _cycles.GetByIdAsync(cycleId, cancellationToken)
            ?? throw new AppErrorException("Admission cycle not found.", 404, ErrorCodes.NotFound);
        return Map(cycle);
    }

    public async Task<IReadOnlyList<AdmissionCycleResponse>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdmissionCycle> cycles = await _cycles.ListAsync(status, cancellationToken);
        return cycles.Select(Map).ToList();
    }

    private async Task<AdmissionCycleResponse> MutateAsync(Guid cycleId, Action<AdmissionCycle> mutate,
        CancellationToken cancellationToken)
    {
        AdmissionCycle cycle = await _cycles.GetByIdAsync(cycleId, cancellationToken)
            ?? throw new AppErrorException("Admission cycle not found.", 404, ErrorCodes.NotFound);
        mutate(cycle);
        await _cycles.SaveAsync(cycle, cancellationToken);
        return Map(cycle);
    }

    private static AdmissionCycleResponse Map(AdmissionCycle c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        IntakeType = c.IntakeType,
        OpensAt = c.OpensAt,
        ClosesAt = c.ClosesAt,
        Quota = c.Quota,
        Status = c.Status,
        CreatedAt = c.CreatedAt
    };
}
