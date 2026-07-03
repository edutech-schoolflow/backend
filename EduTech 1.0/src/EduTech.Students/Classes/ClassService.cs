using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Students.Classes;

public interface IClassService
{
    Task<IReadOnlyList<SchoolClassResponse>> ListClassesAsync(CancellationToken cancellationToken);
    Task<SchoolClassResponse> GetClassAsync(Guid classId, CancellationToken cancellationToken);
    Task<SchoolClassResponse> CreateClassAsync(CreateClassRequest request, CancellationToken cancellationToken);
    Task DeleteClassAsync(Guid classId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassArmResponse>> ListArmsAsync(Guid classId, CancellationToken cancellationToken);
    Task<ClassArmResponse> AddArmAsync(Guid classId, AddArmRequest request, CancellationToken cancellationToken);
    Task SetClassLevelTeacherAsync(Guid classId, SetClassTeacherRequest request, CancellationToken cancellationToken);
    Task SetClassTeacherAsync(Guid armId, SetClassTeacherRequest request, CancellationToken cancellationToken);
    Task<SubjectTeacherResponse> AddSubjectTeacherAsync(Guid armId, AddSubjectTeacherRequest request, CancellationToken cancellationToken);
    Task RemoveSubjectTeacherAsync(Guid subjectTeacherId, CancellationToken cancellationToken);
}

internal sealed class ClassService : IClassService
{
    private readonly IClassRepository _repository;

    public ClassService(IClassRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<SchoolClassResponse>> ListClassesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SchoolClassRow> rows = await _repository.ListClassesAsync(cancellationToken);
        return rows.Select(MapClass).ToList();
    }

    public async Task<SchoolClassResponse> GetClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        SchoolClassRow row = await _repository.GetClassAsync(classId, cancellationToken)
            ?? throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        return MapClass(row);
    }

    private static SchoolClassResponse MapClass(SchoolClassRow r) => new SchoolClassResponse
    {
        Id = r.Id,
        Name = r.Name,
        Level = SnakeCaseEnum.Parse<ClassLevel>(r.Level),
        Order = r.Order,
        ArmsCount = r.ArmsCount,
        StudentsCount = r.StudentsCount,
        TeacherNames = SplitNames(r.TeacherNames),
        ClassTeacher = r.ClassTeacherAffiliationId is Guid tid
            ? new ClassTeacherResponse { AffiliationId = tid, Name = r.ClassTeacherName ?? string.Empty }
            : null
    };

    public async Task<SchoolClassResponse> CreateClassAsync(CreateClassRequest request, CancellationToken cancellationToken)
    {
        string name = (request.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            throw new AppErrorException("Class name is required.", 400, ErrorCodes.ValidationError);
        }

        // Class names are unique per school (the DB enforces it) — fail with a clear message
        // rather than letting the unique-constraint violation surface as a 500.
        if (await _repository.ClassNameExistsAsync(name, cancellationToken))
        {
            throw new AppErrorException(
                $"A class named \"{name}\" already exists.",
                409, ErrorCodes.Conflict);
        }

        if (request.Level is not ClassLevel level)
        {
            throw new AppErrorException(
                "Level must be pre_school, nursery, primary, junior_secondary, or senior_secondary.",
                400, ErrorCodes.ValidationError);
        }

        List<string> arms = (request.Arms ?? new List<string>())
            .Select(a => a.Trim().ToUpperInvariant())
            .Where(a => a.Length > 0)
            .Distinct()
            .ToList();

        // Arms are optional — a class can have none. Students enrol into the class directly; add named
        // streams (A/B/C) only when the school splits the class.
        List<(string Arm, Guid? TeacherAffiliationId)> armSpecs = new List<(string, Guid?)>();
        foreach (string arm in arms)
        {
            Guid? teacher = null;
            if (request.TeacherPerArm is not null && request.TeacherPerArm.TryGetValue(arm, out Guid affiliationId))
            {
                if (!await _repository.AffiliationActiveAsync(affiliationId, cancellationToken))
                {
                    throw new AppErrorException($"The teacher assigned to arm {arm} isn't an active staff member.",
                        400, ErrorCodes.ValidationError);
                }
                teacher = affiliationId;
            }
            armSpecs.Add((arm, teacher));
        }

        Guid classId = await _repository.CreateClassWithArmsAsync(name, level, request.Order, armSpecs, cancellationToken);

        return new SchoolClassResponse
        {
            Id = classId, Name = name, Level = level, Order = request.Order,
            ArmsCount = armSpecs.Count, StudentsCount = 0, TeacherNames = Array.Empty<string>()
        };
    }

    public async Task DeleteClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        if (!await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        }

        // A class with history must not be silently destroyed — deleting it would orphan its students and
        // wipe their grades, attendance and fee links. Block it and tell the owner what to clear first.
        ClassDependentsRow dep = await _repository.GetDependentsAsync(classId, cancellationToken);
        if (dep.HasAny)
        {
            List<string> parts = new List<string>();
            if (dep.Students > 0) parts.Add($"{dep.Students} student{(dep.Students == 1 ? "" : "s")}");
            // Historical enrolments (alumni / past sessions) that aren't current students still block it.
            if (dep.Students == 0 && dep.Enrollments > 0) parts.Add("past enrolment history (alumni)");
            if (dep.FeeTypes > 0) parts.Add($"{dep.FeeTypes} fee type{(dep.FeeTypes == 1 ? "" : "s")}");
            if (dep.Subjects > 0) parts.Add($"{dep.Subjects} subject{(dep.Subjects == 1 ? "" : "s")}");
            if (dep.Attendance > 0) parts.Add("attendance records");
            if (dep.Grades > 0) parts.Add("grade records");

            throw new AppErrorException(
                $"This class still has {string.Join(", ", parts)} and can't be deleted — its history must be " +
                "kept. Move current students to another class (and detach its fees) instead.",
                409, ErrorCodes.Conflict);
        }

        await _repository.DeleteClassAsync(classId, cancellationToken);
    }

    public async Task<IReadOnlyList<ClassArmResponse>> ListArmsAsync(Guid classId, CancellationToken cancellationToken)
    {
        if (!await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        }

        IReadOnlyList<ClassArmRow> arms = await _repository.ListArmsAsync(classId, cancellationToken);
        IReadOnlyList<SubjectTeacherRow> subjects = await _repository.ListSubjectTeachersForClassAsync(classId, cancellationToken);

        return arms.Select(a => new ClassArmResponse
        {
            Id = a.Id,
            ClassId = a.ClassId,
            ClassName = a.ClassName,
            Arm = a.Arm,
            FullName = a.ClassName + a.Arm,
            ClassTeacher = a.ClassTeacherAffiliationId is Guid tid
                ? new ClassTeacherResponse { AffiliationId = tid, Name = a.ClassTeacherName ?? string.Empty }
                : null,
            StudentsCount = a.StudentsCount,
            SubjectTeachers = subjects
                .Where(s => s.ClassArmId == a.Id)
                .Select(s => new SubjectTeacherResponse
                {
                    Id = s.Id, Subject = s.Subject,
                    TeacherAffiliationId = s.TeacherAffiliationId, TeacherName = s.TeacherName
                })
                .ToList()
        }).ToList();
    }

    public async Task<ClassArmResponse> AddArmAsync(Guid classId, AddArmRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        }

        string arm = (request.Arm ?? string.Empty).Trim();
        if (arm.Length == 0)
        {
            throw new AppErrorException("Arm name is required.", 400, ErrorCodes.ValidationError);
        }

        if (await _repository.ArmNameExistsAsync(classId, arm, cancellationToken))
        {
            throw new AppErrorException($"Arm {arm} already exists in this class.", 409, ErrorCodes.Conflict);
        }

        if (request.TeacherAffiliationId is Guid affiliationId
            && !await _repository.AffiliationActiveAsync(affiliationId, cancellationToken))
        {
            throw new AppErrorException("That teacher isn't an active staff member at this school.",
                400, ErrorCodes.ValidationError);
        }

        Guid armId = await _repository.AddArmAsync(classId, arm, request.TeacherAffiliationId, cancellationToken);

        ClassArmRow row = (await _repository.ListArmsAsync(classId, cancellationToken)).First(a => a.Id == armId);
        return new ClassArmResponse
        {
            Id = row.Id,
            ClassId = row.ClassId,
            ClassName = row.ClassName,
            Arm = row.Arm,
            FullName = row.ClassName + row.Arm,
            ClassTeacher = row.ClassTeacherAffiliationId is Guid tid
                ? new ClassTeacherResponse { AffiliationId = tid, Name = row.ClassTeacherName ?? string.Empty }
                : null,
            StudentsCount = row.StudentsCount,
            SubjectTeachers = Array.Empty<SubjectTeacherResponse>()
        };
    }

    public async Task SetClassLevelTeacherAsync(Guid classId, SetClassTeacherRequest request, CancellationToken cancellationToken)
    {
        if (!await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        }

        if (request.TeacherAffiliationId is Guid affiliationId
            && !await _repository.AffiliationActiveAsync(affiliationId, cancellationToken))
        {
            throw new AppErrorException("That teacher isn't an active staff member at this school.",
                400, ErrorCodes.ValidationError);
        }

        await _repository.SetClassLevelTeacherAsync(classId, request.TeacherAffiliationId, cancellationToken);
    }

    public async Task SetClassTeacherAsync(Guid armId, SetClassTeacherRequest request, CancellationToken cancellationToken)
    {
        if (!await _repository.ArmExistsAsync(armId, cancellationToken))
        {
            throw new AppErrorException("Class arm not found.", 404, ErrorCodes.NotFound);
        }

        if (request.TeacherAffiliationId is Guid affiliationId
            && !await _repository.AffiliationActiveAsync(affiliationId, cancellationToken))
        {
            throw new AppErrorException("That teacher isn't an active staff member at this school.",
                400, ErrorCodes.ValidationError);
        }

        await _repository.SetClassTeacherAsync(armId, request.TeacherAffiliationId, cancellationToken);
    }

    public async Task<SubjectTeacherResponse> AddSubjectTeacherAsync(Guid armId, AddSubjectTeacherRequest request,
        CancellationToken cancellationToken)
    {
        string subject = (request.Subject ?? string.Empty).Trim();
        if (subject.Length == 0)
        {
            throw new AppErrorException("Subject is required.", 400, ErrorCodes.ValidationError);
        }

        if (!await _repository.ArmExistsAsync(armId, cancellationToken))
        {
            throw new AppErrorException("Class arm not found.", 404, ErrorCodes.NotFound);
        }

        if (!await _repository.AffiliationActiveAsync(request.TeacherAffiliationId, cancellationToken))
        {
            throw new AppErrorException("That teacher isn't an active staff member at this school.",
                400, ErrorCodes.ValidationError);
        }

        Guid id = await _repository.AddSubjectTeacherAsync(armId, request.TeacherAffiliationId, subject, cancellationToken);

        return new SubjectTeacherResponse
        {
            Id = id, Subject = subject, TeacherAffiliationId = request.TeacherAffiliationId, TeacherName = string.Empty
        };
    }

    public Task RemoveSubjectTeacherAsync(Guid subjectTeacherId, CancellationToken cancellationToken)
    {
        return _repository.RemoveSubjectTeacherAsync(subjectTeacherId, cancellationToken);
    }

    private static IReadOnlyList<string> SplitNames(string? joined)
    {
        if (string.IsNullOrWhiteSpace(joined))
        {
            return Array.Empty<string>();
        }

        return joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }
}
