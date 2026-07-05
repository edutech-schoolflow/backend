using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Attendance;

public interface IAttendanceService
{
    Task<IReadOnlyList<MarkableArmResponse>> ListMarkableArmsAsync(CancellationToken cancellationToken);
    Task<AttendanceRosterResponse> GetRosterAsync(Guid classId, Guid? armId, DateOnly? date, CancellationToken cancellationToken);
    Task<AttendanceRecordResponse> SubmitAsync(SubmitAttendanceRequest request, CancellationToken cancellationToken);
    Task<AttendanceOverviewResponse> GetOverviewAsync(DateOnly? date, CancellationToken cancellationToken);
}

internal sealed class AttendanceService : IAttendanceService
{
    private readonly IAttendanceRepository _repository;
    private readonly IEduTechRequestContext _context;

    public AttendanceService(IAttendanceRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<IReadOnlyList<MarkableArmResponse>> ListMarkableArmsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MarkableArmRow> rows =
            await _repository.ListMarkableArmsAsync(CurrentAffiliation(), _context.IsOwner, cancellationToken);

        return rows.Select(r => new MarkableArmResponse
        {
            ArmId = r.ArmId, ArmName = r.ArmName, ClassId = r.ClassId, ClassName = r.ClassName,
            Level = SnakeCaseEnum.Parse<ClassLevel>(r.Level)
        }).ToList();
    }

    public async Task<AttendanceRosterResponse> GetRosterAsync(Guid classId, Guid? armId, DateOnly? date,
        CancellationToken cancellationToken)
    {
        UnitInfoRow unit = await AuthorizeUnitAsync(classId, armId, cancellationToken);
        DateOnly day = date ?? Today;

        IReadOnlyList<RosterStudentRow> rows = await _repository.GetRosterAsync(classId, armId, day, cancellationToken);
        bool submitted = await _repository.RecordExistsAsync(classId, armId, day, cancellationToken);

        return new AttendanceRosterResponse
        {
            ClassId = classId,
            ArmId = armId,
            ArmName = unit.UnitName,
            Date = day,
            Submitted = submitted,
            Students = rows.Select(r => new AttendanceRosterStudent
            {
                StudentId = r.StudentId, StudentName = r.StudentName, AdmissionNumber = r.AdmissionNumber,
                Status = r.Status is null ? null : SnakeCaseEnum.Parse<AttendanceStatus>(r.Status)
            }).ToList()
        };
    }

    public async Task<AttendanceRecordResponse> SubmitAsync(SubmitAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        UnitInfoRow unit = await AuthorizeUnitAsync(request.ClassId, request.ArmId, cancellationToken);

        if (request.Date == default || request.Date > Today)
        {
            throw new AppErrorException("A valid attendance date (not in the future) is required.",
                400, ErrorCodes.ValidationError);
        }

        if (request.Marks.Count == 0)
        {
            throw new AppErrorException("At least one student mark is required.", 400, ErrorCodes.ValidationError);
        }

        HashSet<Guid> unitStudents =
            (await _repository.GetActiveStudentIdsAsync(request.ClassId, request.ArmId, cancellationToken)).ToHashSet();

        List<(Guid StudentId, AttendanceStatus Status)> marks =
            new List<(Guid, AttendanceStatus)>(request.Marks.Count);
        HashSet<Guid> seen = new HashSet<Guid>();

        foreach (AttendanceMarkInput mark in request.Marks)
        {
            if (mark.Status is not AttendanceStatus status)
            {
                throw new AppErrorException("Status must be 'present', 'absent', or 'late'.",
                    400, ErrorCodes.ValidationError);
            }

            if (mark.StudentId == Guid.Empty || !unitStudents.Contains(mark.StudentId))
            {
                throw new AppErrorException("A marked student is not an active member of this class.",
                    400, ErrorCodes.ValidationError);
            }

            if (!seen.Add(mark.StudentId))
            {
                throw new AppErrorException("The same student was marked more than once.",
                    400, ErrorCodes.ValidationError);
            }

            marks.Add((mark.StudentId, status));
        }

        // A register belongs to the term CONTAINING its date — a backdated entry for last term must
        // never be booked to the current one (it feeds report-card attendance totals via term_id).
        // Schools that haven't set term dates yet fall back to the current term ([RequiresCurrentTerm]
        // guarantees one exists).
        Guid? termId = await _repository.GetTermIdForDateAsync(request.Date, cancellationToken);
        if (termId is null)
        {
            if (await _repository.HasDatedTermsAsync(cancellationToken))
            {
                throw new AppErrorException(
                    "That date falls outside every term. Attendance can only be marked for school days within a term.",
                    400, ErrorCodes.ValidationError);
            }

            termId = await _repository.GetCurrentTermIdAsync(cancellationToken);
        }

        Guid? submittedBy = _context.IsOwner ? null : CurrentAffiliation();

        (Guid id, DateTime submittedAt) = await _repository.UpsertRecordAsync(
            request.ClassId, request.ArmId, request.Date, termId, submittedBy, marks, cancellationToken);

        return new AttendanceRecordResponse
        {
            Id = id,
            ClassId = request.ClassId,
            ArmId = request.ArmId,
            ArmName = unit.UnitName,
            Date = request.Date,
            PresentCount = marks.Count(m => m.Status == AttendanceStatus.Present),
            AbsentCount = marks.Count(m => m.Status == AttendanceStatus.Absent),
            LateCount = marks.Count(m => m.Status == AttendanceStatus.Late),
            TotalCount = marks.Count,
            SubmittedAt = submittedAt
        };
    }

    public async Task<AttendanceOverviewResponse> GetOverviewAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        DateOnly day = date ?? Today;

        IReadOnlyList<ArmStatRow> stats = await _repository.GetOverviewArmStatsAsync(day, cancellationToken);
        IReadOnlyList<AbsentStudentRow> absents = await _repository.GetAbsentStudentsAsync(day, cancellationToken);

        List<ArmAttendanceStatResponse> arms = stats.Select(s => new ArmAttendanceStatResponse
        {
            ClassId = s.ClassId, ArmId = s.ArmId, ArmName = s.ArmName, Submitted = s.Submitted,
            PresentCount = s.PresentCount, AbsentCount = s.AbsentCount, LateCount = s.LateCount,
            TotalCount = s.TotalCount, PresentPct = Percent(s.PresentCount, s.TotalCount)
        }).ToList();

        // Totals reflect submitted units only (an unsubmitted unit contributes nothing).
        List<ArmStatRow> submitted = stats.Where(s => s.Submitted).ToList();
        int totalPresent = submitted.Sum(s => s.PresentCount);
        int totalAbsent = submitted.Sum(s => s.AbsentCount);
        int totalLate = submitted.Sum(s => s.LateCount);
        int totalStudents = submitted.Sum(s => s.TotalCount);

        return new AttendanceOverviewResponse
        {
            Date = day,
            TotalPresent = totalPresent,
            TotalAbsent = totalAbsent,
            TotalLate = totalLate,
            TotalStudents = totalStudents,
            OverallPresentPct = Percent(totalPresent, totalStudents),
            Arms = arms,
            AbsentStudents = absents.Select(a => new AbsentStudentResponse
            {
                StudentName = a.StudentName, ArmName = a.ArmName
            }).ToList()
        };
    }

    /// <summary>
    /// Resolves the register unit (arm or arm-less class) and enforces the marking rule: owners may mark
    /// any unit; everyone else may mark only a unit they are the class teacher of. Throws 404 if the unit
    /// isn't at this school, 403 otherwise.
    /// </summary>
    private async Task<UnitInfoRow> AuthorizeUnitAsync(Guid classId, Guid? armId, CancellationToken cancellationToken)
    {
        UnitInfoRow unit = await _repository.GetUnitAsync(classId, armId, cancellationToken)
            ?? throw new AppErrorException("Class or arm not found.", 404, ErrorCodes.NotFound);

        if (_context.IsOwner)
        {
            return unit;
        }

        Guid? affiliation = CurrentAffiliation();
        if (affiliation is null || unit.ClassTeacherAffiliationId != affiliation)
        {
            throw new AppErrorException(
                "You can only mark attendance for classes where you are the class teacher.",
                403, ErrorCodes.AccessDenied, logReason:
                $"Affiliation {affiliation?.ToString() ?? "(none)"} attempted to mark unit class={classId} arm={armId?.ToString() ?? "(none)"} " +
                $"whose class teacher is {unit.ClassTeacherAffiliationId?.ToString() ?? "(unassigned)"}.");
        }

        return unit;
    }

    private Guid? CurrentAffiliation() =>
        Guid.TryParse(_context.AffiliationId, out Guid id) ? id : (Guid?)null;

    // School days are West Africa Time (UTC+1, no DST) — plain UTC would make "today" roll over at
    // 1 a.m. WAT, defaulting rosters to yesterday and rejecting just-after-midnight marks as future.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    private static int Percent(int part, int whole) =>
        whole > 0 ? (int)Math.Round(100.0 * part / whole) : 0;
}
