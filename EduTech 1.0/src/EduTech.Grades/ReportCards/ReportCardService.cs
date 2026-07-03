using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;

namespace EduTech.Grades.ReportCards;

public interface IReportCardService
{
    Task<ReportCardResponse> GetReportAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportSummaryResponse>> ListForArmAsync(Guid armId, Guid termId, CancellationToken cancellationToken);
    Task SaveMetaAsync(Guid studentId, Guid termId, SaveReportMetaRequest request, CancellationToken cancellationToken);
    Task PublishStudentAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    Task<int> PublishArmAsync(PublishArmReportsRequest request, CancellationToken cancellationToken);
}

internal sealed class ReportCardService : IReportCardService
{
    private readonly IReportCardRepository _repository;
    private readonly IGradingScaleService _gradingScale;
    private readonly INotificationDispatcher _notifications;

    public ReportCardService(IReportCardRepository repository, IGradingScaleService gradingScale,
        INotificationDispatcher notifications)
    {
        _repository = repository;
        _gradingScale = gradingScale;
        _notifications = notifications;
    }

    public async Task<ReportCardResponse> GetReportAsync(Guid studentId, Guid termId, CancellationToken cancellationToken)
    {
        StudentReportInfoRow student = await _repository.GetStudentAsync(studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);
        TermInfoRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<SubjectScoreRow> scores = student.ClassArmId is Guid armId
            ? await _repository.GetSubjectScoresAsync(studentId, armId, termId, cancellationToken)
            : Array.Empty<SubjectScoreRow>();

        IReadOnlyList<GradeBoundaryDto> bands = await _gradingScale.GetAsync(cancellationToken);

        List<SubjectGradeResponse> grades = scores
            .GroupBy(s => (s.SubjectId, s.SubjectName))
            .Select(g => BuildSubjectGrade(g.Key.SubjectId, g.Key.SubjectName, g, bands))
            .ToList();

        List<decimal> totals = grades.Where(g => g.Total.HasValue).Select(g => g.Total!.Value).ToList();
        decimal? overall = totals.Count > 0 ? Math.Round(totals.Average(), 2) : null;

        AttendanceSummaryRow attendance = await _repository.GetAttendanceSummaryAsync(studentId, termId, cancellationToken);

        ReportMetaRow? meta = await _repository.GetMetaAsync(studentId, termId, cancellationToken);
        IReadOnlyList<BehavioralRatingDto> behavioral = Array.Empty<BehavioralRatingDto>();
        if (meta is not null)
        {
            behavioral = (await _repository.GetBehavioralAsync(meta.Id, cancellationToken))
                .Select(b => new BehavioralRatingDto { Trait = SnakeCaseEnum.Parse<BehavioralTrait>(b.Trait), Score = b.Score })
                .ToList();
        }

        return new ReportCardResponse
        {
            StudentId = student.Id,
            StudentName = student.StudentName,
            AdmissionNumber = student.AdmissionNumber,
            ClassName = student.ClassName,
            ArmName = student.ArmName,
            TermId = termId,
            Term = SnakeCaseEnum.Parse<Term>(term.Name),
            AcademicYear = term.AcademicYear,
            Grades = grades,
            OverallAverage = overall,
            TeacherComment = meta?.TeacherComment,
            PrincipalComment = meta?.PrincipalComment,
            BehavioralRatings = behavioral,
            AttendanceDays = attendance.Total,
            PresentDays = attendance.Present,
            AbsentDays = attendance.Absent,
            LateDays = attendance.Late,
            NextTermResumption = meta?.NextTermResumption,
            Status = meta is null ? GradeStatus.Draft : SnakeCaseEnum.Parse<GradeStatus>(meta.Status),
            PublishedAt = meta?.PublishedAt
        };
    }

    public async Task<IReadOnlyList<ReportSummaryResponse>> ListForArmAsync(Guid armId, Guid termId,
        CancellationToken cancellationToken)
    {
        if (await _repository.GetTermAsync(termId, cancellationToken) is null)
        {
            throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);
        }

        IReadOnlyList<ReportListRow> rows = await _repository.ListForArmAsync(armId, termId, cancellationToken);
        return rows.Select(r => new ReportSummaryResponse
        {
            StudentId = r.StudentId,
            StudentName = r.StudentName,
            AdmissionNumber = r.AdmissionNumber,
            OverallAverage = r.OverallAverage,
            Status = SnakeCaseEnum.Parse<GradeStatus>(r.Status)
        }).ToList();
    }

    public async Task SaveMetaAsync(Guid studentId, Guid termId, SaveReportMetaRequest request,
        CancellationToken cancellationToken)
    {
        StudentReportInfoRow student = await _repository.GetStudentAsync(studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);
        if (await _repository.GetTermAsync(termId, cancellationToken) is null)
        {
            throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);
        }

        string? status = await _repository.GetStatusAsync(studentId, termId, cancellationToken);
        if (status == "published")
        {
            throw new AppErrorException("This report card is published and can't be edited.", 409, ErrorCodes.Conflict);
        }

        List<(BehavioralTrait Trait, int Score)> behavioral = new List<(BehavioralTrait, int)>();
        HashSet<BehavioralTrait> seen = new HashSet<BehavioralTrait>();
        foreach (BehavioralRatingDto rating in request.BehavioralRatings ?? new List<BehavioralRatingDto>())
        {
            if (rating.Score is < 1 or > 5)
            {
                throw new AppErrorException("Behavioral scores must be between 1 and 5.", 400, ErrorCodes.ValidationError);
            }

            if (!seen.Add(rating.Trait))
            {
                throw new AppErrorException("The same behavioral trait was rated more than once.", 400, ErrorCodes.ValidationError);
            }

            behavioral.Add((rating.Trait, rating.Score));
        }

        await _repository.UpsertMetaAsync(studentId, termId, student.ClassArmId,
            Trim(request.TeacherComment), Trim(request.PrincipalComment), request.NextTermResumption,
            behavioral, cancellationToken);
    }

    public async Task PublishStudentAsync(Guid studentId, Guid termId, CancellationToken cancellationToken)
    {
        StudentReportInfoRow student = await _repository.GetStudentAsync(studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);
        TermInfoRow term = await _repository.GetTermAsync(termId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        string? status = await _repository.GetStatusAsync(studentId, termId, cancellationToken);
        if (status == "published")
        {
            return;   // idempotent — already released
        }

        if (status is not null)
        {
            ReportLifecycle.Rules.Require(SnakeCaseEnum.Parse<GradeStatus>(status), GradeStatus.Published);
        }

        Guid? published = await _repository.PublishStudentAsync(studentId, termId, student.ClassArmId, cancellationToken);
        if (published is not null)
        {
            await NotifyGuardiansAsync(new[] { studentId }, term, cancellationToken);
        }
    }

    public async Task<int> PublishArmAsync(PublishArmReportsRequest request, CancellationToken cancellationToken)
    {
        TermInfoRow term = await _repository.GetTermAsync(request.TermId, cancellationToken)
            ?? throw new AppErrorException("Term not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<Guid> published = await _repository.PublishArmAsync(request.ArmId, request.TermId, cancellationToken);
        if (published.Count > 0)
        {
            await NotifyGuardiansAsync(published, term, cancellationToken);
        }

        return published.Count;
    }

    private async Task NotifyGuardiansAsync(IReadOnlyList<Guid> studentIds, TermInfoRow term, CancellationToken cancellationToken)
    {
        string termLabel = SnakeCaseEnum.ToWire(SnakeCaseEnum.Parse<Term>(term.Name)).Replace('_', ' ');
        IReadOnlyList<NotifyTargetRow> targets = await _repository.GetNotifyTargetsAsync(studentIds, cancellationToken);

        foreach (NotifyTargetRow target in targets)
        {
            await _notifications.SendSmsAsync(target.Phone,
                $"{target.StudentName}'s {termLabel} report card ({term.AcademicYear}) has been published.",
                cancellationToken);
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SubjectGradeResponse BuildSubjectGrade(Guid subjectId, string subjectName,
        IEnumerable<SubjectScoreRow> rows, IReadOnlyList<GradeBoundaryDto> bands)
    {
        decimal? ca1 = ScoreOf(rows, "first_ca");
        decimal? ca2 = ScoreOf(rows, "second_ca");
        decimal? exam = ScoreOf(rows, "exam");

        decimal? total = (ca1.HasValue || ca2.HasValue || exam.HasValue)
            ? (ca1 ?? 0) + (ca2 ?? 0) + (exam ?? 0)
            : null;

        (string grade, string remark) = total.HasValue ? GradingScale.Resolve(total.Value, bands) : (null!, null!);

        return new SubjectGradeResponse
        {
            SubjectId = subjectId,
            SubjectName = subjectName,
            Ca1 = ca1,
            Ca2 = ca2,
            Exam = exam,
            Total = total,
            Grade = total.HasValue ? grade : null,
            Remark = total.HasValue ? remark : null
        };
    }

    private static decimal? ScoreOf(IEnumerable<SubjectScoreRow> rows, string assessment) =>
        rows.FirstOrDefault(r => r.AssessmentType == assessment)?.Score;
}
