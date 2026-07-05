using EduTech.Shared.Audit;
using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Phone;
using EduTech.Students.Students.Commands;

namespace EduTech.Students.Students;

public interface IStudentService
{
    Task<StudentListResponse> ListAsync(Guid? classId, string? status, int page, int limit, CancellationToken cancellationToken);
    Task<StudentResponse> GetAsync(Guid studentId, CancellationToken cancellationToken);
    Task<StudentResponse> CreateAsync(CreateStudentRequest request, CancellationToken cancellationToken);
    Task<ParentLookupResponse> LookupParentAsync(string? phone, CancellationToken cancellationToken);
    Task UpdateGuardiansAsync(Guid studentId, UpdateGuardiansRequest request, CancellationToken cancellationToken);
    Task WithdrawAsync(Guid studentId, CancellationToken cancellationToken);
    Task ReAdmitAsync(Guid studentId, CancellationToken cancellationToken);
    Task TransferAsync(Guid studentId, TransferStudentRequest request, CancellationToken cancellationToken);
    Task<string> UndoLastAsync(Guid studentId, CancellationToken cancellationToken);
    Task<PromotionResultResponse> PromoteAsync(PromoteStudentsRequest request, CancellationToken cancellationToken);
}

internal sealed class StudentService : IStudentService
{
    private readonly IStudentRepository _repository;
    private readonly StudentCommandInvoker _invoker;
    private readonly IDomainEventPublisher _events;
    private readonly IAuditLogRepository _audit;

    public StudentService(IStudentRepository repository, StudentCommandInvoker invoker,
        IDomainEventPublisher events, IAuditLogRepository audit)
    {
        _repository = repository;
        _invoker = invoker;
        _events = events;
        _audit = audit;
    }

    public async Task<StudentListResponse> ListAsync(Guid? classId, string? status, int page, int limit,
        CancellationToken cancellationToken)
    {
        int safePage = page < 1 ? 1 : page;
        int safeLimit = limit is < 1 or > 100 ? 20 : limit;

        StudentStatus? statusFilter = NormalizeStatusFilter(status);

        (IReadOnlyList<StudentRow> rows, int total) =
            await _repository.ListAsync(classId, statusFilter, (safePage - 1) * safeLimit, safeLimit, cancellationToken);

        return new StudentListResponse
        {
            Data = rows.Select(r => Map(r, Array.Empty<GuardianDto>())).ToList(),
            Total = total
        };
    }

    public async Task<ParentLookupResponse> LookupParentAsync(string? phone, CancellationToken cancellationToken)
    {
        string? normalized = PhoneNumber.Normalize(phone);
        if (normalized is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        ParentLookupRow? row = await _repository.LookupParentByPhoneAsync(normalized, cancellationToken);
        if (row is null)
        {
            return new ParentLookupResponse { Found = false };
        }

        string name = string.Join(' ',
            new[] { row.FirstName, row.MiddleName, row.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new ParentLookupResponse
        {
            Found = true,
            Name = name,
            Status = row.HasPassword ? "registered" : "pending",
        };
    }

    public async Task<StudentResponse> GetAsync(Guid studentId, CancellationToken cancellationToken)
    {
        StudentRow row = await _repository.GetAsync(studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);

        IReadOnlyList<GuardianRow> guardians = await _repository.GetGuardiansAsync(studentId, cancellationToken);
        IReadOnlyList<GuardianDto> mapped = guardians
            .Select(g => new GuardianDto { Name = g.Name, Phone = g.Phone, Relationship = g.Relationship, Email = g.Email })
            .ToList();

        return Map(row, mapped);
    }

    public async Task<StudentResponse> CreateAsync(CreateStudentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        if (request.Gender is not Gender gender)
        {
            throw new AppErrorException("Gender must be 'male' or 'female'.", 400, ErrorCodes.ValidationError);
        }

        if (request.DateOfBirth == default || request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new AppErrorException("A valid date of birth is required.", 400, ErrorCodes.ValidationError);
        }

        // Enrol into a CLASS (required). An arm (stream) is optional — only when the class is streamed.
        if (request.ClassId is not Guid classId || !await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Select a valid class.", 400, ErrorCodes.ValidationError);
        }

        if (request.ClassArmId is Guid armId
            && !await _repository.ArmInClassAsync(armId, classId, cancellationToken))
        {
            throw new AppErrorException("The selected arm doesn't belong to that class.", 400, ErrorCodes.ValidationError);
        }

        // The new student must link to a parent (primary guardian) by phone — existing account is
        // reused, otherwise a pending one is created and claimed later via OTP login.
        string? parentPhone = PhoneNumber.Normalize(request.Parent?.Phone);
        if (parentPhone is null)
        {
            throw new AppErrorException("A valid parent/guardian phone is required.", 400, ErrorCodes.ValidationError);
        }

        List<GuardianDto> extraGuardians = ValidateGuardians(request.Guardians);

        StudentInsert insert = new StudentInsert
        {
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = gender,
            PhotoUrl = request.PhotoUrl,
            PreviousSchool = request.PreviousSchool,
            MedicalNotes = request.MedicalNotes,
            ClassId = classId,
            ClassArmId = request.ClassArmId,
            Parent = new ParentLink
            {
                Phone = parentPhone,
                FirstName = request.Parent?.FirstName?.Trim(),
                LastName = request.Parent?.LastName?.Trim(),
                Relationship = string.IsNullOrWhiteSpace(request.Parent?.Relationship) ? null : request.Parent!.Relationship.Trim()
            }
        };

        (Guid id, _) = await _repository.CreateAsync(insert, extraGuardians, cancellationToken);

        // Re-read so the response reflects the resolved class/arm names + parent (and any extra contacts).
        return await GetAsync(id, cancellationToken);
    }

    public async Task UpdateGuardiansAsync(Guid studentId, UpdateGuardiansRequest request, CancellationToken cancellationToken)
    {
        if (!await _repository.ExistsAsync(studentId, cancellationToken))
        {
            throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);
        }

        List<GuardianDto> guardians = ValidateGuardians(request.Guardians);
        await _repository.ReplaceGuardiansAsync(studentId, guardians, cancellationToken);
    }

    // Lifecycle actions are Command objects run through the invoker, so each is applied uniformly and
    // recorded in the audit trail (and can be reversed by UndoLastAsync).

    public Task WithdrawAsync(Guid studentId, CancellationToken cancellationToken)
        => _invoker.RunAsync(new WithdrawStudentCommand(_repository, studentId), cancellationToken);

    public Task ReAdmitAsync(Guid studentId, CancellationToken cancellationToken)
        => _invoker.RunAsync(new ReAdmitStudentCommand(_repository, studentId), cancellationToken);

    public async Task TransferAsync(Guid studentId, TransferStudentRequest request, CancellationToken cancellationToken)
    {
        if (!await _repository.ClassArmExistsAsync(request.ClassArmId, cancellationToken))
        {
            throw new AppErrorException("The selected class/arm doesn't exist.", 400, ErrorCodes.ValidationError);
        }

        await _invoker.RunAsync(new TransferStudentCommand(_repository, studentId, request.ClassArmId), cancellationToken);
    }

    /// <summary>Reverses the student's most recent lifecycle action (single-level undo) from the audit trail.</summary>
    public async Task<string> UndoLastAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if (!await _repository.ExistsAsync(studentId, cancellationToken))
        {
            throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);
        }

        IReadOnlyList<AuditLogEntry> recent = await _audit.ListAsync("student", studentId, 0, 1, cancellationToken);
        AuditLogEntry? last = recent.Count > 0 ? recent[0] : null;

        // Nothing to undo if there's no history, or the most recent action was itself a revert.
        if (last is null || last.Action == "student.reverted")
        {
            throw new AppErrorException("There's no recent action to undo for this student.", 409, ErrorCodes.Conflict);
        }

        string summary;
        switch (last.Action)
        {
            case "student.withdrawn":
                await _repository.SetStatusIfAsync(studentId, StudentStatus.Withdrawn, StudentStatus.Active, cancellationToken);
                summary = "Reverted a withdrawal — the student is active again.";
                break;

            case "student.readmitted":
                await _repository.SetStatusIfAsync(studentId, StudentStatus.Active, StudentStatus.Withdrawn, cancellationToken);
                summary = "Reverted a re-admission — the student is withdrawn again.";
                break;

            case "student.transferred":
                await _repository.SetClassArmAsync(studentId, ParseBeforeArm(last.Metadata), cancellationToken);
                summary = "Reverted a transfer — the student is back in their previous arm.";
                break;

            default:
                throw new AppErrorException("That action can't be undone.", 409, ErrorCodes.Conflict);
        }

        await _events.PublishAsync(new StudentLifecycleEvent
        {
            StudentId = studentId, Action = "student.reverted", Summary = summary
        }, cancellationToken);

        return summary;
    }

    // Reads {"beforeArmId":"<guid>"|null} from a transfer's audit metadata.
    private static Guid? ParseBeforeArm(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(metadata);
            if (doc.RootElement.TryGetProperty("beforeArmId", out System.Text.Json.JsonElement el)
                && el.ValueKind == System.Text.Json.JsonValueKind.String
                && Guid.TryParse(el.GetString(), out Guid armId))
            {
                return armId;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed metadata → treat as "no arm".
        }

        return null;
    }

    public async Task<PromotionResultResponse> PromoteAsync(PromoteStudentsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TargetAcademicYearId == Guid.Empty
            || !await _repository.YearExistsAsync(request.TargetAcademicYearId, cancellationToken))
        {
            throw new AppErrorException("Select a valid target session.", 400, ErrorCodes.ValidationError);
        }

        // Promotion only ever moves forward — never into the current or a past session.
        if (!await _repository.IsSessionForwardAsync(request.TargetAcademicYearId, cancellationToken))
        {
            throw new AppErrorException(
                "You can only promote students into a session later than the current one.", 409, ErrorCodes.Conflict);
        }

        if (request.Promotions is null || request.Promotions.Count == 0)
        {
            throw new AppErrorException("Select at least one student to promote.", 400, ErrorCodes.ValidationError);
        }

        if (request.Promotions.Count > 500)
        {
            throw new AppErrorException("Promote at most 500 students at a time.", 400, ErrorCodes.ValidationError);
        }

        List<PromotionCommand> commands = new List<PromotionCommand>();
        int promoted = 0, repeated = 0, graduated = 0;

        foreach (PromotionItem item in request.Promotions)
        {
            if (item.Action is not PromotionAction action)
            {
                throw new AppErrorException("Each student needs an action: promote, repeat, or graduate.",
                    400, ErrorCodes.ValidationError);
            }

            if (!await _repository.ExistsAsync(item.StudentId, cancellationToken))
            {
                throw new AppErrorException("One of the selected students no longer exists.", 404, ErrorCodes.NotFound);
            }

            if (action == PromotionAction.Graduate)
            {
                graduated++;
                commands.Add(new PromotionCommand
                {
                    StudentId = item.StudentId, Outcome = "graduated", Graduate = true
                });
                continue;
            }

            // Promote / repeat both land the student in a class in the target session.
            if (item.TargetClassId is not Guid targetClassId
                || !await _repository.ClassExistsAsync(targetClassId, cancellationToken))
            {
                throw new AppErrorException("Select a valid class to move each promoted/repeating student into.",
                    400, ErrorCodes.ValidationError);
            }

            if (item.TargetClassArmId is Guid armId
                && !await _repository.ArmInClassAsync(armId, targetClassId, cancellationToken))
            {
                throw new AppErrorException("A selected arm doesn't belong to its target class.",
                    400, ErrorCodes.ValidationError);
            }

            if (action == PromotionAction.Promote) promoted++; else repeated++;

            commands.Add(new PromotionCommand
            {
                StudentId = item.StudentId,
                Outcome = action == PromotionAction.Promote ? "promoted" : "repeated",
                Graduate = false,
                TargetClassId = targetClassId,
                TargetClassArmId = item.TargetClassArmId
            });
        }

        await _repository.PromoteStudentsAsync(request.TargetAcademicYearId, commands, cancellationToken);

        return new PromotionResultResponse { Promoted = promoted, Repeated = repeated, Graduated = graduated };
    }


    private static StudentStatus? NormalizeStatusFilter(string? status)
        => SnakeCaseEnum.TryParse(status, out StudentStatus parsed) ? parsed : null;   // "all"/empty/unknown → no filter

    private static List<GuardianDto> ValidateGuardians(IEnumerable<GuardianDto>? guardians)
    {
        List<GuardianDto> result = new List<GuardianDto>();
        foreach (GuardianDto g in guardians ?? Enumerable.Empty<GuardianDto>())
        {
            if (string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.Phone) ||
                string.IsNullOrWhiteSpace(g.Relationship))
            {
                throw new AppErrorException("Each guardian needs a name, phone, and relationship.",
                    400, ErrorCodes.ValidationError);
            }

            result.Add(new GuardianDto
            {
                Name = g.Name.Trim(), Phone = g.Phone.Trim(), Relationship = g.Relationship.Trim(),
                Email = string.IsNullOrWhiteSpace(g.Email) ? null : g.Email.Trim()
            });
        }

        return result;
    }

    private static StudentResponse Map(StudentRow r, IReadOnlyList<GuardianDto> guardians) => new StudentResponse
    {
        Id = r.Id,
        FirstName = r.FirstName,
        MiddleName = r.MiddleName,
        LastName = r.LastName,
        DateOfBirth = r.DateOfBirth,
        Gender = SnakeCaseEnum.Parse<Gender>(r.Gender),
        PhotoUrl = r.PhotoUrl,
        PreviousSchool = r.PreviousSchool,
        MedicalNotes = r.MedicalNotes,
        AdmissionNumber = r.AdmissionNumber,
        ClassArmId = r.ClassArmId,
        ClassId = r.ClassId,
        ClassName = r.ClassName,
        Arm = r.Arm,
        Status = SnakeCaseEnum.Parse<StudentStatus>(r.Status),
        Guardians = guardians,
        CreatedAt = r.CreatedAt
    };
}
