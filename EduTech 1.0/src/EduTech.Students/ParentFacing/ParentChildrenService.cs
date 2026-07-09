using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Ports;

namespace EduTech.Students.ParentFacing;

public interface IParentChildrenService
{
    Task<IReadOnlyList<ParentChildResponse>> GetChildrenAsync(CancellationToken cancellationToken);
    Task<ChildProfileResponse> GetChildAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<Guid> UpsertChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildReportCardSummary>> GetReportCardsAsync(Guid childProfileId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildCaScoreResponse>> GetCaScoresAsync(Guid childProfileId, Guid? termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildAttendanceSummary>> GetAttendanceAsync(Guid childProfileId, CancellationToken cancellationToken);
}

internal sealed class ParentChildrenService : IParentChildrenService
{
    private readonly IParentChildrenRepository _repository;
    private readonly IEduTechRequestContext _context;
    private readonly IStudentFeeBalanceProvider _feeBalances;

    public ParentChildrenService(IParentChildrenRepository repository, IEduTechRequestContext context,
        IStudentFeeBalanceProvider feeBalances)
    {
        _repository = repository;
        _context = context;
        _feeBalances = feeBalances;
    }

    public async Task<IReadOnlyList<ParentChildResponse>> GetChildrenAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ParentChildRow> rows = await _repository.GetChildrenAsync(ParentId, cancellationToken);

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
            MedicalInfo = row.MedicalInfo
        };
    }

    public async Task<Guid> UpsertChildAsync(UpsertChildProfileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        if (request.DateOfBirth == default || request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new AppErrorException("A valid date of birth is required.", 400, ErrorCodes.ValidationError);
        }

        ChildProfileInsert insert = new ChildProfileInsert
        {
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender is Gender g ? SnakeCaseEnum.ToWire(g) : null,
            PhotoUrl = request.PhotoUrl,
            PreviousSchool = request.PreviousSchool,
            MedicalInfo = request.MedicalInfo
        };

        if (request.Id is Guid id)
        {
            await EnsureOwnsAsync(id, cancellationToken);
            await _repository.UpdateChildProfileAsync(id, insert, cancellationToken);
            return id;
        }

        return await _repository.InsertChildProfileAsync(ParentId, insert,
            string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim(), cancellationToken);
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
        if (!await _repository.OwnsChildAsync(ParentId, childProfileId, cancellationToken))
        {
            throw new AppErrorException("Child not found.", 404, ErrorCodes.NotFound);
        }
    }

    private Guid ParentId =>
        Guid.TryParse(_context.UserId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
}
