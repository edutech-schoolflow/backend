using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;
using EduTech.Shared.Security;
using EduTech.Shared.Storage;
using Microsoft.AspNetCore.Http;

namespace EduTech.School.Kyc;

/// <summary>
/// School-owner KYC: submit (school details + proprietor + bank + 5 documents) and read status.
/// Public because the controller depends on it; the implementation stays internal.
/// </summary>
public interface ISchoolKycService
{
    Task<KycSubmissionResponse> SubmitAsync(SubmitKycRequest request, CancellationToken cancellationToken);
    Task<KycSubmissionResponse> GetStatusAsync(CancellationToken cancellationToken);
}

internal sealed class SchoolKycService : ISchoolKycService
{
    private const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB per document

    private static readonly (string Type, Func<SubmitKycRequest, IFormFile?> Selector)[] DocumentSpecs =
    {
        ("registration_cert", r => r.RegistrationCert),
        ("operating_licence", r => r.OperatingLicence),
        ("proof_of_address", r => r.ProofOfAddress),
        ("proprietor_id_front", r => r.ProprietorIdFront),
        ("proprietor_id_back", r => r.ProprietorIdBack),
    };

    private readonly IEduTechRequestContext _requestContext;
    private readonly ISchoolKycRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly IFieldEncryptor _encryptor;
    private readonly IDbConnectionFactory _connectionFactory;

    public SchoolKycService(
        IEduTechRequestContext requestContext,
        ISchoolKycRepository repository,
        IFileStorage fileStorage,
        IFieldEncryptor encryptor,
        IDbConnectionFactory connectionFactory)
    {
        _requestContext = requestContext;
        _repository = repository;
        _fileStorage = fileStorage;
        _encryptor = encryptor;
        _connectionFactory = connectionFactory;
    }

    public async Task<KycSubmissionResponse> SubmitAsync(SubmitKycRequest request, CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();

        string? status = await _repository.GetKycStatusAsync(schoolId, cancellationToken);
        if (status is "under_review" or "approved")
        {
            throw new AppErrorException(
                status == "approved" ? "Your school is already verified." : "Your KYC is already under review.",
                409, ErrorCodes.Conflict);
        }

        ValidateText(request);

        // Upload files BEFORE the DB transaction (mirrors the OTP-after-commit pattern). If a DB write
        // fails afterwards, the worst case is an orphaned object — never a half-written submission.
        List<(string Type, string Url)> uploaded = new List<(string, string)>(DocumentSpecs.Length);
        foreach ((string type, Func<SubmitKycRequest, IFormFile?> selector) in DocumentSpecs)
        {
            IFormFile? file = selector(request);
            if (file is null || file.Length == 0)
            {
                throw new AppErrorException($"Document '{type}' is required.", 400, ErrorCodes.ValidationError);
            }

            if (file.Length > MaxFileBytes)
            {
                throw new AppErrorException($"Document '{type}' exceeds the 10 MB limit.", 400, ErrorCodes.ValidationError);
            }

            string extension = Path.GetExtension(file.FileName);
            string key = $"kyc/{schoolId}/{type}{extension}";

            await using Stream stream = file.OpenReadStream();
            string url = await _fileStorage.UploadAsync(stream, key, file.ContentType, cancellationToken);
            uploaded.Add((type, url));
        }

        KycSubmissionRow submission = new KycSubmissionRow
        {
            ProprietorName = request.ProprietorName.Trim(),
            ProprietorIdType = request.ProprietorIdType.Trim(),
            ProprietorIdNumber = request.ProprietorIdNumber.Trim(),
            ProprietorPhone = request.ProprietorPhone.Trim(),
            ProprietorEmail = request.ProprietorEmail.Trim(),
            BankName = request.BankName.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountName = request.AccountName.Trim(),
            AccountType = request.AccountType.Trim()
        };

        SchoolDetails details = new SchoolDetails
        {
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim()
        };

        // NIN/BVN are encrypted at rest and never returned to the client.
        string encryptedNin = _encryptor.Encrypt(request.ProprietorNin.Trim());
        string encryptedBvn = _encryptor.Encrypt(request.ProprietorBvn.Trim());

        await using (DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken))
        {
            await _repository.UpdateSchoolDetailsAsync(schoolId, details, transaction.Transaction, cancellationToken);
            await _repository.UpsertSubmissionAsync(schoolId, submission, encryptedNin, encryptedBvn,
                transaction.Transaction, cancellationToken);
            foreach ((string type, string url) in uploaded)
            {
                await _repository.UpsertDocumentAsync(schoolId, type, url, transaction.Transaction, cancellationToken);
            }
            await _repository.SetKycStatusAsync(schoolId, "under_review", transaction.Transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetStatusAsync(cancellationToken);
    }

    public async Task<KycSubmissionResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();

        string status = await _repository.GetKycStatusAsync(schoolId, cancellationToken) ?? "not_submitted";
        KycSubmissionRow? submission = await _repository.GetSubmissionAsync(schoolId, cancellationToken);
        IReadOnlyList<KycDocumentRow> documents = await _repository.GetDocumentsAsync(schoolId, cancellationToken);

        return new KycSubmissionResponse
        {
            SchoolId = schoolId,
            Status = status,
            SubmittedAt = submission?.SubmittedAt,
            ReviewedAt = submission?.ReviewedAt,
            SchoolMessage = submission?.SchoolMessage,
            ProprietorName = submission?.ProprietorName,
            ProprietorIdType = submission?.ProprietorIdType,
            ProprietorIdNumber = submission?.ProprietorIdNumber,
            ProprietorPhone = submission?.ProprietorPhone,
            ProprietorEmail = submission?.ProprietorEmail,
            BankName = submission?.BankName,
            AccountNumber = submission?.AccountNumber,
            AccountName = submission?.AccountName,
            AccountType = submission?.AccountType,
            Documents = documents.Select(d => new KycDocumentResponse
            {
                Type = d.Type,
                Url = d.Url,
                Status = d.Status,
                Notes = d.Notes
            }).ToList()
        };
    }

    private static void ValidateText(SubmitKycRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Type))
        {
            throw new AppErrorException("School name and type are required.", 400, ErrorCodes.ValidationError);
        }

        if (string.IsNullOrWhiteSpace(request.ProprietorName) ||
            string.IsNullOrWhiteSpace(request.ProprietorIdNumber))
        {
            throw new AppErrorException("Proprietor name and ID number are required.", 400, ErrorCodes.ValidationError);
        }

        if (!IsElevenDigits(request.ProprietorNin))
        {
            throw new AppErrorException("Proprietor NIN must be 11 digits.", 400, ErrorCodes.ValidationError);
        }

        if (!IsElevenDigits(request.ProprietorBvn))
        {
            throw new AppErrorException("Proprietor BVN must be 11 digits.", 400, ErrorCodes.ValidationError);
        }

        if (string.IsNullOrWhiteSpace(request.BankName) ||
            string.IsNullOrWhiteSpace(request.AccountNumber) ||
            string.IsNullOrWhiteSpace(request.AccountName))
        {
            throw new AppErrorException("Bank account details are required.", 400, ErrorCodes.ValidationError);
        }
    }

    private static bool IsElevenDigits(string? value)
    {
        return value is { Length: 11 } && value.All(char.IsDigit);
    }

    private Guid CurrentSchoolId()
    {
        if (!Guid.TryParse(_requestContext.SchoolId, out Guid schoolId))
        {
            throw new AppErrorException("No school context on this request.", 403, ErrorCodes.Forbidden);
        }

        return schoolId;
    }
}
