using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Ports;

namespace EduTech.Students.ParentFacing;

public interface IParentChildrenService
{
    Task<IReadOnlyList<ParentChildResponse>> GetChildrenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The signed-in IDENTITY's children across every school (EDD-002: an identity-level "my data" view,
    /// authorized by the identity session, not a parent token). Empty when there's no parent profile yet.
    /// </summary>
    Task<IReadOnlyList<ParentChildResponse>> GetMyChildrenAsync(CancellationToken cancellationToken);
    Task<ChildProfileResponse> GetChildAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<Guid> UpsertChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Identity space (EDD-002): save a child to the signed-in identity's account, provisioning the
    /// parent profile if this is their first child — the Stage-1 completion point. Authorized by the
    /// identity session; a school membership is not required to keep children on your account.
    /// </summary>
    Task<Guid> UpsertMyChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildReportCardSummary>> GetReportCardsAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildCaScoreResponse>> GetCaScoresAsync(Guid childProfileId, Guid? termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildAttendanceSummary>> GetAttendanceAsync(Guid childProfileId, CancellationToken cancellationToken);
}

internal sealed class ParentChildrenService : IParentChildrenService
{
    private readonly IParentChildrenRepository _repository;
    private readonly IEduTechRequestContext _context;
    private readonly IStudentFeeBalanceProvider _feeBalances;
    private readonly EduTech.Shared.Storage.IFileStorage _fileStorage;

    public ParentChildrenService(IParentChildrenRepository repository, IEduTechRequestContext context,
        IStudentFeeBalanceProvider feeBalances, EduTech.Shared.Storage.IFileStorage fileStorage)
    {
        _repository = repository;
        _context = context;
        _feeBalances = feeBalances;
        _fileStorage = fileStorage;
    }

    public Task<IReadOnlyList<ParentChildResponse>> GetChildrenAsync(CancellationToken cancellationToken)
        => GetMyChildrenAsync(cancellationToken);

    public async Task<IReadOnlyList<ParentChildResponse>> GetMyChildrenAsync(CancellationToken cancellationToken)
    {
        // Resolve the parent profile from the identity; a brand-new user has none yet → no children.
        Guid? parentId = await _repository.GetParentIdByIdentityAsync(CurrentIdentityId, cancellationToken);
        return parentId is Guid pid
            ? await FetchChildrenAsync(pid, cancellationToken)
            : Array.Empty<ParentChildResponse>();
    }

    private async Task<IReadOnlyList<ParentChildResponse>> FetchChildrenAsync(Guid parentId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentChildRow> rows = await _repository.GetChildrenAsync(parentId, cancellationToken);

        // Outstanding balances are Finance's number — fetched through the SharedKernel port (EDD-002 V1).
        IReadOnlyList<Guid> studentIds = rows.Where(r => r.StudentId is not null)
            .Select(r => r.StudentId!.Value).ToList();
        IReadOnlyDictionary<Guid, decimal> balances = studentIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _feeBalances.GetOutstandingAsync(studentIds.ToList(), cancellationToken);

        return rows.Select(r => new ParentChildResponse
        {
            ChildProfileId = r.ChildProfileId, StudentName = r.StudentName, StudentId = r.StudentId,
            SchoolId = r.SchoolId, SchoolName = r.SchoolName, SchoolLogoUrl = r.SchoolLogoUrl,
            ClassName = r.ClassName, AdmissionNumber = r.AdmissionNumber, EnrollmentStatus = r.EnrollmentStatus,
            OutstandingFees = r.StudentId is Guid sid && balances.TryGetValue(sid, out decimal owed) ? owed : 0m,
            HasNewResult = r.HasNewResult
        }).ToList();
    }

    public async Task<ChildProfileResponse> GetChildAsync(Guid childProfileId, CancellationToken cancellationToken)
    {
        await EnsureOwnsAsync(childProfileId, cancellationToken);
        ChildProfileDetailRow row = await _repository.GetChildProfileAsync(childProfileId, cancellationToken)
            ?? throw new AppErrorException("Child not found.", 404, ErrorCodes.NotFound);

        return new ChildProfileResponse
        {
            Id = row.Id,
            FirstName = row.FirstName,
            MiddleName = row.MiddleName,
            LastName = row.LastName,
            DateOfBirth = row.DateOfBirth,
            Gender = SnakeCaseEnum.TryParse<Gender>(row.Gender, out Gender g) ? g : null,
            PhotoUrl = row.PhotoUrl,
            PreviousSchool = row.PreviousSchool,
            MedicalInfo = row.MedicalInfo,
            BirthCertUrl = row.BirthCertUrl,
            MedicalDocUrl = row.MedicalDocUrl
        };
    }

    public Task<Guid> UpsertChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken)
        => UpsertMyChildAsync(request, cancellationToken);

    public async Task<Guid> UpsertMyChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken)
    {
        // Saving a child is a Stage-1 act: provision the parent profile from the identity if it's their
        // first child, so a person can keep children on their account before joining any school.
        Guid parentId = await _repository.GetOrProvisionParentIdAsync(CurrentIdentityId, cancellationToken);
        return await UpsertForParentAsync(parentId, request, cancellationToken);
    }

    private async Task<Guid> UpsertForParentAsync(Guid parentId, UpsertChildProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        if (request.DateOfBirth == default || request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new AppErrorException("A valid date of birth is required.", 400, ErrorCodes.ValidationError);
        }

        bool creating = request.Id is null;
        if (creating && (request.Photo is null || request.Photo.Length == 0))
        {
            throw new AppErrorException("The child's photo is required.", 400, ErrorCodes.ValidationError);
        }
        if (creating && (request.BirthCert is null || request.BirthCert.Length == 0))
        {
            throw new AppErrorException("The birth certificate is required.", 400, ErrorCodes.ValidationError);
        }

        // Upload before the DB write (KYC pattern): a later failure orphans an object at worst,
        // never a half-written profile. The medical document stays optional.
        string? photoUrl = await UploadDocumentAsync(parentId, "photo", request.Photo, cancellationToken);
        string? birthCertUrl = await UploadDocumentAsync(parentId, "birth_cert", request.BirthCert, cancellationToken);
        string? medicalDocUrl = await UploadDocumentAsync(parentId, "medical_doc", request.MedicalDoc, cancellationToken);

        ChildProfileInsert insert = new ChildProfileInsert
        {
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender is Gender g ? SnakeCaseEnum.ToWire(g) : null,
            PhotoUrl = photoUrl ?? request.PhotoUrl,
            PreviousSchool = request.PreviousSchool,
            MedicalInfo = request.MedicalInfo,
            BirthCertUrl = birthCertUrl,
            MedicalDocUrl = medicalDocUrl
        };

        if (request.Id is Guid id)
        {
            await EnsureOwnsAsync(id, cancellationToken);
            await _repository.UpdateChildProfileAsync(id, insert, cancellationToken);
            return id;
        }

        return await _repository.InsertChildProfileAsync(parentId, insert,
            string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim(), cancellationToken);
    }

    private const long MaxDocumentBytes = 10 * 1024 * 1024; // 10 MB, same ceiling as KYC documents

    private async Task<string?> UploadDocumentAsync(Guid parentId, string kind,
        Microsoft.AspNetCore.Http.IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        if (file.Length > MaxDocumentBytes)
        {
            throw new AppErrorException("Each document must be 10 MB or smaller.", 400, ErrorCodes.ValidationError);
        }

        string key = $"children/{parentId}/{Guid.NewGuid():N}-{kind}{Path.GetExtension(file.FileName)}";
        await using Stream stream = file.OpenReadStream();
        return await _fileStorage.UploadAsync(stream, key, file.ContentType, cancellationToken);
    }

    public async Task<IReadOnlyList<ChildReportCardSummary>> GetReportCardsAsync(Guid childProfileId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnsAsync(childProfileId, cancellationToken);
        IReadOnlyList<ChildReportCardRow> rows = await _repository.GetReportCardsAsync(childProfileId, cancellationToken);
        return rows.Select(r => new ChildReportCardSummary
        {
            Id = r.Id, Term = r.Term, AcademicYear = r.AcademicYear, SchoolName = r.SchoolName,
            Status = r.Status, PublishedAt = r.PublishedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<ChildCaScoreResponse>> GetCaScoresAsync(Guid childProfileId, Guid? termId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnsAsync(childProfileId, cancellationToken);
        IReadOnlyList<ChildCaScoreRow> rows = await _repository.GetCaScoresAsync(childProfileId, termId, cancellationToken);
        return rows.Select(r => new ChildCaScoreResponse
        {
            SubjectName = r.SubjectName, AssessmentType = r.AssessmentType, Score = r.Score,
            MaxScore = r.MaxScore, TermId = r.TermId
        }).ToList();
    }

    public async Task<IReadOnlyList<ChildAttendanceSummary>> GetAttendanceAsync(Guid childProfileId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnsAsync(childProfileId, cancellationToken);
        IReadOnlyList<ChildAttendanceRow> rows = await _repository.GetAttendanceAsync(childProfileId, cancellationToken);
        return rows.Select(r => new ChildAttendanceSummary
        {
            Term = r.Term, PresentDays = r.PresentDays, AbsentDays = r.AbsentDays,
            LateDays = r.LateDays, TotalDays = r.TotalDays
        }).ToList();
    }

    /// <summary>404 (not 403) if the child isn't the caller's — don't reveal that it exists.</summary>
    private async Task EnsureOwnsAsync(Guid childProfileId, CancellationToken cancellationToken)
    {
        Guid? parentId = await _repository.GetParentIdByIdentityAsync(CurrentIdentityId, cancellationToken);
        if (parentId is null || !await _repository.OwnsChildAsync(parentId.Value, childProfileId, cancellationToken))
        {
            throw new AppErrorException("Child not found.", 404, ErrorCodes.NotFound);
        }
    }

    // The identity behind the session: the identity_id claim (org tokens) or the user_id itself (an
    // identity-scope session, where user_id IS the identity).
    private Guid CurrentIdentityId =>
        Guid.TryParse(_context.IdentityId ?? _context.UserId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
}
