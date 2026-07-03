using EduTech.Shared.Constants;
using EduTech.Shared.Identity;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Security;

namespace EduTech.Compliance.Nin;

/// <summary>
/// Staff/parent NIN compliance: submit a NIN → verify (Dojah/stub) → store ENCRYPTED → set
/// kyc_status; plus the derived compliance record. Liveness is gated client-side, so only the NIN
/// reaches the backend.
/// </summary>
public interface IComplianceService
{
    Task<ComplianceRecordResponse> SubmitNinAsync(SubmitNinRequest request, CancellationToken cancellationToken);
    Task<ComplianceRecordResponse> GetRecordAsync(CancellationToken cancellationToken);
}

internal sealed class ComplianceService : IComplianceService
{
    private readonly IEduTechRequestContext _requestContext;
    private readonly IComplianceRepository _repository;
    private readonly IIdentityVerifier _verifier;
    private readonly IFieldEncryptor _encryptor;

    public ComplianceService(
        IEduTechRequestContext requestContext,
        IComplianceRepository repository,
        IIdentityVerifier verifier,
        IFieldEncryptor encryptor)
    {
        _requestContext = requestContext;
        _repository = repository;
        _verifier = verifier;
        _encryptor = encryptor;
    }

    public async Task<ComplianceRecordResponse> SubmitNinAsync(SubmitNinRequest request,
        CancellationToken cancellationToken)
    {
        (string actorType, Guid actorId) = ResolveActor();

        string nin = (request.Nin ?? string.Empty).Trim();
        if (nin.Length != 11 || !nin.All(char.IsDigit))
        {
            throw new AppErrorException("NIN must be exactly 11 digits.", 400, ErrorCodes.ValidationError);
        }

        string? fullName = await _repository.GetFullNameAsync(actorType, actorId, cancellationToken);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);
        }

        IdentityVerificationResult result = await _verifier.VerifyNinAsync(nin, fullName, cancellationToken);
        if (!result.Verified)
        {
            throw new AppErrorException(result.Reason ?? "We couldn't verify your NIN.",
                422, ErrorCodes.ValidationError);
        }

        string encrypted = _encryptor.Encrypt(nin);
        await _repository.SetNinAsync(actorType, actorId, encrypted, "verified", cancellationToken);

        return await GetRecordAsync(cancellationToken);
    }

    public async Task<ComplianceRecordResponse> GetRecordAsync(CancellationToken cancellationToken)
    {
        (string actorType, Guid actorId) = ResolveActor();

        ComplianceStateRow? state = await _repository.GetAsync(actorType, actorId, cancellationToken);
        return BuildRecord(actorType, state?.KycStatus ?? "not_submitted");
    }

    private (string ActorType, Guid ActorId) ResolveActor()
    {
        string? actorType = _requestContext.UserType;
        if (actorType != UserTypes.Staff && actorType != UserTypes.Parent)
        {
            throw new AppErrorException("Compliance isn't available for this account.", 403, ErrorCodes.Forbidden);
        }

        if (!Guid.TryParse(_requestContext.UserId, out Guid actorId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        return (actorType, actorId);
    }

    private static ComplianceRecordResponse BuildRecord(string actorType, string kycStatus)
    {
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
            ActorType = actorType == UserTypes.Staff ? "teacher" : "parent",
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
