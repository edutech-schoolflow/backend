using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Grades.Scores;

public interface IGradeService
{
    Task<IReadOnlyList<GradeableArmResponse>> ListGradeableArmsAsync(CancellationToken cancellationToken);
    Task<GradeRecordResponse> GetRecordAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType? assessmentType, CancellationToken cancellationToken);
    Task<GradeRecordSummaryResponse> SubmitAsync(SubmitGradesRequest request, CancellationToken cancellationToken);
    Task PublishAsync(Guid recordId, CancellationToken cancellationToken);
    Task<int> PublishAllAsync(PublishAllRequest request, CancellationToken cancellationToken);
    Task<GradesOverviewResponse> GetOverviewAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class GradeService : IGradeService
{
    private readonly IGradeRepository _repository;
    private readonly IEduTechRequestContext _context;

    public GradeService(IGradeRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<IReadOnlyList<GradeableArmResponse>> ListGradeableArmsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GradeableArmRow> rows =
            await _repository.ListGradeableArmsAsync(CurrentAffiliation(), _context.IsOwner, cancellationToken);

        return rows.Select(r => new GradeableArmResponse
        {
            ArmId = r.ArmId, ArmName = r.ArmName, ClassId = r.ClassId, ClassName = r.ClassName,
            Level = SnakeCaseEnum.Parse<ClassLevel>(r.Level)
        }).ToList();
    }

    public async Task<GradeRecordResponse> GetRecordAsync(Guid armId, Guid subjectId, Guid termId,
        AssessmentType? assessmentType, CancellationToken cancellationToken)
    {
        AssessmentType assessment = RequireAssessment(assessmentType);
        (ArmGradingRow arm, SubjectInfoRow subject) = await AuthorizeAsync(armId, subjectId, cancellationToken);
        await EnsureTermAsync(termId, cancellationToken);

        GradeRecordHeaderRow? header =
            await _repository.GetRecordHeaderAsync(armId, subjectId, termId, assessment, cancellationToken);
        IReadOnlyList<GradeRosterRow> roster =
            await _repository.GetRosterAsync(armId, subjectId, termId, assessment, cancellationToken);

        return new GradeRecordResponse
        {
            RecordId = header?.Id,
            ArmId = armId,
            ArmName = arm.ArmName,
            SubjectId = subjectId,
            SubjectName = subject.Name,
            TermId = termId,
            AssessmentType = assessment,
            MaxScore = MaxFor(assessment, subject),
            Status = header is null ? GradeStatus.Draft : SnakeCaseEnum.Parse<GradeStatus>(header.Status),
            Students = roster.Select(r => new GradeEntryStudent
            {
                StudentId = r.StudentId, StudentName = r.StudentName,
                AdmissionNumber = r.AdmissionNumber, Score = r.Score
            }).ToList()
        };
    }

    public async Task<GradeRecordSummaryResponse> SubmitAsync(SubmitGradesRequest request,
        CancellationToken cancellationToken)
    {
        AssessmentType assessment = RequireAssessment(request.AssessmentType);
        (ArmGradingRow arm, SubjectInfoRow subject) = await AuthorizeAsync(request.ArmId, request.SubjectId, cancellationToken);
        await EnsureTermAsync(request.TermId, cancellationToken);

        GradeRecordHeaderRow? existing =
            await _repository.GetRecordHeaderAsync(request.ArmId, request.SubjectId, request.TermId, assessment, cancellationToken);
        if (existing is not null && SnakeCaseEnum.Parse<GradeStatus>(existing.Status) == GradeStatus.Published)
        {
            throw new AppErrorException("These grades are published and can't be edited.", 409, ErrorCodes.Conflict);
        }

        int maxScore = MaxFor(assessment, subject);
        HashSet<Guid> armStudents =
            (await _repository.GetActiveStudentIdsAsync(request.ArmId, cancellationToken)).ToHashSet();

        List<(Guid StudentId, decimal Score)> entries = new List<(Guid, decimal)>(request.Entries.Count);
        HashSet<Guid> seen = new HashSet<Guid>();

        foreach (GradeEntryInput entry in request.Entries)
        {
            if (entry.Score is not decimal score)
            {
                continue;   // null = not yet entered — skip
            }

            if (entry.StudentId == Guid.Empty || !armStudents.Contains(entry.StudentId))
            {
                throw new AppErrorException("A graded student is not an active member of this arm.",
                    400, ErrorCodes.ValidationError);
            }

            if (!seen.Add(entry.StudentId))
            {
                throw new AppErrorException("The same student was graded more than once.",
                    400, ErrorCodes.ValidationError);
            }

            if (score < 0 || score > maxScore)
            {
                throw new AppErrorException($"Score must be between 0 and {maxScore}.",
                    400, ErrorCodes.ValidationError);
            }

            entries.Add((entry.StudentId, score));
        }

        Guid? submittedBy = _context.IsOwner ? null : CurrentAffiliation();
        (Guid id, DateTime submittedAt) = await _repository.UpsertRecordAsync(
            request.ArmId, request.SubjectId, request.TermId, assessment, maxScore, submittedBy, entries, cancellationToken);

        return new GradeRecordSummaryResponse
        {
            Id = id,
            ArmId = request.ArmId,
            SubjectId = request.SubjectId,
            TermId = request.TermId,
            AssessmentType = assessment,
            MaxScore = maxScore,
            Status = GradeStatus.Draft,
            EnteredCount = entries.Count,
            TotalCount = armStudents.Count,
            SubmittedAt = submittedAt
        };
    }

    public async Task PublishAsync(Guid recordId, CancellationToken cancellationToken)
    {
        string? raw = await _repository.GetRecordStatusAsync(recordId, cancellationToken)
            ?? throw new AppErrorException("Grade record not found.", 404, ErrorCodes.NotFound);

        GradeStatus current = SnakeCaseEnum.Parse<GradeStatus>(raw);
        if (current == GradeStatus.Published)
        {
            return;   // idempotent
        }

        GradeLifecycle.Rules.Require(current, GradeStatus.Published);

        int changed = await _repository.PublishRecordIfDraftAsync(recordId, cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("These grades changed, please retry.", 409, ErrorCodes.Conflict);
        }
    }

    public async Task<int> PublishAllAsync(PublishAllRequest request, CancellationToken cancellationToken)
    {
        await EnsureTermAsync(request.TermId, cancellationToken);
        return await _repository.PublishAllDraftAsync(request.TermId, request.ArmId, cancellationToken);
    }

    public async Task<GradesOverviewResponse> GetOverviewAsync(Guid termId, CancellationToken cancellationToken)
    {
        await EnsureTermAsync(termId, cancellationToken);

        IReadOnlyList<GradeOverviewRow> rows = await _repository.GetOverviewAsync(termId, cancellationToken);
        List<GradeSummaryRowResponse> mapped = rows.Select(r => new GradeSummaryRowResponse
        {
            RecordId = r.RecordId,
            ArmId = r.ArmId,
            ArmName = r.ArmName,
            SubjectId = r.SubjectId,
            SubjectName = r.SubjectName,
            AssessmentType = SnakeCaseEnum.Parse<AssessmentType>(r.AssessmentType),
            MaxScore = r.MaxScore,
            Status = SnakeCaseEnum.Parse<GradeStatus>(r.Status),
            AverageScore = r.AverageScore,
            PassCount = r.PassCount,
            FailCount = r.FailCount,
            TotalCount = r.TotalCount,
            SubmittedAt = r.SubmittedAt
        }).ToList();

        return new GradesOverviewResponse
        {
            TermId = termId,
            TotalSubmitted = mapped.Count,
            TotalPublished = mapped.Count(r => r.Status == GradeStatus.Published),
            Rows = mapped
        };
    }

    /// <summary>
    /// Enforces the Nigerian grading-authority rule: primary-tier arms are entered by the arm's class
    /// teacher; secondary arms by the subject teacher assigned to that subject. Owner bypasses.
    /// </summary>
    private async Task<(ArmGradingRow Arm, SubjectInfoRow Subject)> AuthorizeAsync(Guid armId, Guid subjectId,
        CancellationToken cancellationToken)
    {
        ArmGradingRow arm = await _repository.GetArmAsync(armId, cancellationToken)
            ?? throw new AppErrorException("Class arm not found.", 404, ErrorCodes.NotFound);
        SubjectInfoRow subject = await _repository.GetSubjectAsync(subjectId, cancellationToken)
            ?? throw new AppErrorException("Subject not found.", 404, ErrorCodes.NotFound);

        if (subject.ClassId != arm.ClassId)
        {
            throw new AppErrorException("That subject doesn't belong to this class.", 400, ErrorCodes.ValidationError);
        }

        if (_context.IsOwner)
        {
            return (arm, subject);
        }

        Guid? affiliation = CurrentAffiliation();
        ClassLevel level = SnakeCaseEnum.Parse<ClassLevel>(arm.Level);
        bool primaryTier = IsPrimaryTier(level);

        bool allowed = affiliation is Guid aff && (primaryTier
            ? arm.ClassTeacherAffiliationId == aff
            : await _repository.IsSubjectTeacherAsync(armId, subject.Name, aff, cancellationToken));

        if (!allowed)
        {
            throw new AppErrorException(
                primaryTier
                    ? "Only the class teacher of this arm can enter its grades."
                    : "You can only enter grades for a subject you teach in this arm.",
                403, ErrorCodes.AccessDenied,
                logReason: $"Affiliation {affiliation?.ToString() ?? "(none)"} attempted to grade arm {armId} " +
                           $"subject '{subject.Name}' (level {level}).");
        }

        return (arm, subject);
    }

    private async Task EnsureTermAsync(Guid termId, CancellationToken cancellationToken)
    {
        if (!await _repository.TermExistsAsync(termId, cancellationToken))
        {
            throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);
        }
    }

    private static AssessmentType RequireAssessment(AssessmentType? assessment) =>
        assessment ?? throw new AppErrorException(
            "Assessment type must be 'first_ca', 'second_ca', or 'exam'.", 400, ErrorCodes.ValidationError);

    private static bool IsPrimaryTier(ClassLevel level) =>
        level is ClassLevel.PreSchool or ClassLevel.Nursery or ClassLevel.Primary;

    private static int MaxFor(AssessmentType assessment, SubjectInfoRow subject) =>
        assessment == AssessmentType.Exam ? subject.MaxExam : subject.MaxCa;

    private Guid? CurrentAffiliation() =>
        Guid.TryParse(_context.AffiliationId, out Guid id) ? id : (Guid?)null;
}
