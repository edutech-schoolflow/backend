using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Students.Students.Commands;

// The concrete commands. Each captures its receiver (the repository) + the arguments, does the work with a
// race-safe conditional update, and returns a result the invoker turns into an auditable event.

internal sealed class WithdrawStudentCommand : IStudentCommand
{
    private readonly IStudentRepository _repository;
    private readonly Guid _studentId;

    public WithdrawStudentCommand(IStudentRepository repository, Guid studentId)
    {
        _repository = repository;
        _studentId = studentId;
    }

    public async Task<StudentCommandResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        StudentRow student = await _repository.GetAsync(_studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);

        int changed = await _repository.SetStatusIfAsync(_studentId, StudentStatus.Active, StudentStatus.Withdrawn, cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Only an active student can be withdrawn.", 409, ErrorCodes.Conflict);
        }

        return new StudentCommandResult
        {
            StudentId = _studentId, Action = "student.withdrawn",
            Summary = $"Withdrew {StudentNames.Of(student)}."
        };
    }
}

internal sealed class ReAdmitStudentCommand : IStudentCommand
{
    private readonly IStudentRepository _repository;
    private readonly Guid _studentId;

    public ReAdmitStudentCommand(IStudentRepository repository, Guid studentId)
    {
        _repository = repository;
        _studentId = studentId;
    }

    public async Task<StudentCommandResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        StudentRow student = await _repository.GetAsync(_studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);

        int changed = await _repository.SetStatusIfAsync(_studentId, StudentStatus.Withdrawn, StudentStatus.Active, cancellationToken);
        if (changed == 0)
        {
            throw new AppErrorException("Only a withdrawn student can be re-admitted.", 409, ErrorCodes.Conflict);
        }

        return new StudentCommandResult
        {
            StudentId = _studentId, Action = "student.readmitted",
            Summary = $"Re-admitted {StudentNames.Of(student)}."
        };
    }
}

internal sealed class TransferStudentCommand : IStudentCommand
{
    private readonly IStudentRepository _repository;
    private readonly Guid _studentId;
    private readonly Guid _targetArmId;

    public TransferStudentCommand(IStudentRepository repository, Guid studentId, Guid targetArmId)
    {
        _repository = repository;
        _studentId = studentId;
        _targetArmId = targetArmId;
    }

    public async Task<StudentCommandResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        StudentRow student = await _repository.GetAsync(_studentId, cancellationToken)
            ?? throw new AppErrorException("Student not found.", 404, ErrorCodes.NotFound);

        Guid? beforeArm = student.ClassArmId;   // captured so a revert can move them back
        await _repository.SetClassArmAsync(_studentId, _targetArmId, cancellationToken);

        string beforeArmJson = beforeArm is Guid b ? $"\"{b}\"" : "null";
        return new StudentCommandResult
        {
            StudentId = _studentId, Action = "student.transferred",
            Summary = $"Transferred {StudentNames.Of(student)} to a new arm.",
            Metadata = $"{{\"beforeArmId\":{beforeArmJson}}}"
        };
    }
}

internal static class StudentNames
{
    public static string Of(StudentRow s) => $"{s.FirstName} {s.LastName}".Trim();
}
