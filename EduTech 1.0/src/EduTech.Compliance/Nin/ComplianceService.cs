using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Identity;
using EduTech.Shared.Security;

namespace EduTech.Compliance.Nin;

/// <summary>
/// EDD-017 Slice 2 — Identity NIN verification. Compliance owns the WORKFLOW (submit → provider → encrypt →
/// record); Identity owns the STATE. Re-keyed off <c>_context.IdentityId</c> — one path for every actor
/// type (a person verifies once, not per portal). Auth is untouched (Slice 3 flips it to
/// <c>identities.kyc_status</c>). Liveness is gated client-side, so only the NIN reaches the backend.
/// </summary>
public interface IComplianceService
{
    Task<ComplianceRecordResponse> SubmitNinAsync(SubmitNinRequest request, CancellationToken cancellationToken);
    Task<ComplianceRecordResponse> GetRecordAsync(CancellationToken cancellationToken);
}

internal sealed class ComplianceService : IComplianceService
{
    private readonly IEduTechRequestContext _context;
    private readonly IComplianceRepository _repository;
    private readonly IIdentityVerifier _verifier;
    private readonly IFieldEncryptor _encryptor;
    private readonly IDomainEventPublisher _events;

    public ComplianceService(
        IEduTechRequestContext context,
        IComplianceRepository repository,
        IIdentityVerifier verifier,
        IFieldEncryptor encryptor,
        IDomainEventPublisher events)
    {
        _context = context;
        _repository = repository;
        _verifier = verifier;
        _encryptor = encryptor;
        _events = events;
    }

    public async Task<ComplianceRecordResponse> SubmitNinAsync(SubmitNinRequest request,
        CancellationToken cancellationToken)
    {
        (string userType, Guid identityId) = CurrentActor();

        string nin = (request.Nin ?? string.Empty).Trim();
        if (nin.Length != 11 || !nin.All(char.IsDigit))
        {
            throw new AppErrorException("NIN must be exactly 11 digits.", 400, ErrorCodes.ValidationError);
        }

        string? fullName = await _repository.GetIdentityNameAsync(identityId, cancellationToken);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
        }

        // The attempt begins: unverified/rejected → pending.
        await _events.PublishAsync(new IdentityVerificationSubmittedEvent(identityId), cancellationToken);

        IdentityVerificationResult result = await _verifier.VerifyNinAsync(nin, fullName, cancellationToken);

        if (!result.Verified)
        {
            await _repository.RecordFailureAsync(identityId, _verifier.ProviderName, result.Reason, cancellationToken);
            await _events.PublishAsync(new IdentityVerificationFailedEvent(identityId, result.Reason), cancellationToken);
            await _events.PublishAsync(new IdentityVerificationRejectedEvent(identityId), cancellationToken);
            throw new AppErrorException(result.Reason ?? "We couldn't verify your NIN.", 422, ErrorCodes.ValidationError);
        }

        string encrypted = _encryptor.Encrypt(nin);
        await _repository.RecordSuccessAsync(identityId, _verifier.ProviderName, encrypted,
            syncStaff: userType == UserTypes.Staff, syncParent: userType == UserTypes.Parent, cancellationToken);
        await _events.PublishAsync(new IdentityVerificationSucceededEvent(identityId), cancellationToken);

        return await GetRecordAsync(cancellationToken);
    }

    public async Task<ComplianceRecordResponse> GetRecordAsync(CancellationToken cancellationToken)
    {
        (string userType, Guid identityId) = CurrentActor();

        string status = await _repository.GetKycStatusAsync(identityId, cancellationToken) ?? "unverified";
        return BuildRecord(userType, status);
    }

    // Portal gate (product rule: NIN compliance is for staff/parent, not owners) + the canonical identity the
    // workflow keys on. UserType is kept (portal/UX semantic + response label); the legacy actor id is gone.
    private (string UserType, Guid IdentityId) CurrentActor()
    {
        string? userType = _context.UserType;
        if (userType != UserTypes.Staff && userType != UserTypes.Parent)
        {
            throw new AppErrorException("Compliance isn't available for this account.", 403, ErrorCodes.Forbidden);
        }

        if (!Guid.TryParse(_context.IdentityId, out Guid identityId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        return (userType, identityId);
    }

    private static ComplianceRecordResponse BuildRecord(string userType, string kycStatus)
    {
        // The FE ComplianceStep vocabulary is not_started | pending | verified; canonical 'unverified' and
        // 'rejected' both surface as not_started so the user can (re)submit (unchanged FE contract).
        string stepStatus = kycStatus switch
        {
            "verified" => "verified",
            "pending" => "pending",
            _ => "not_started"
        };

        string overall = stepStatus switch
        {
            "verified" => "verified",
            "pending" => "pending",
            _ => "incomplete"
        };

        return new ComplianceRecordResponse
        {
            ActorType = userType == UserTypes.Staff ? "teacher" : "parent",
            OverallStatus = overall,
            UpdatedAt = DateTime.UtcNow,
            Steps = new[]
            {
                new ComplianceStepResponse
                {
                    Id = "nin", Label = "National ID (NIN)", Status = stepStatus, Required = true
                }
            }
        };
    }
}
