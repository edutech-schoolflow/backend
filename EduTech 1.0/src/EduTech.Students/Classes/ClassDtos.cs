using EduTech.Shared.Constants;

namespace EduTech.Students.Classes;

/// <summary>A class with its aggregates (mirrors the frontend SchoolClass).</summary>
public sealed class SchoolClassResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ClassLevel Level { get; init; }
    public required int Order { get; init; }
    public required int ArmsCount { get; init; }
    public required int StudentsCount { get; init; }
    public required IReadOnlyList<string> TeacherNames { get; init; }
    public ClassTeacherResponse? ClassTeacher { get; init; }   // the class's own teacher (arm-less classes)
}

public sealed class CreateClassRequest
{
    public string Name { get; init; } = string.Empty;
    public ClassLevel? Level { get; init; }                     // null => missing/invalid
    public int Order { get; init; }
    public List<string> Arms { get; init; } = new List<string>(); // ["A","B","C"]
    public Dictionary<string, Guid>? TeacherPerArm { get; init; } // arm -> staff_affiliations.id (class teacher)
}

/// <summary>An arm with its class teacher + subject teachers (mirrors the frontend ClassArm).</summary>
public sealed class ClassArmResponse
{
    public required Guid Id { get; init; }
    public required Guid ClassId { get; init; }
    public required string ClassName { get; init; }
    public required string Arm { get; init; }
    public required string FullName { get; init; }   // "JSS 1A"
    public ClassTeacherResponse? ClassTeacher { get; init; }
    public required int StudentsCount { get; init; }
    public required IReadOnlyList<SubjectTeacherResponse> SubjectTeachers { get; init; }
}

public sealed class ClassTeacherResponse
{
    public required Guid AffiliationId { get; init; }
    public required string Name { get; init; }
}

public sealed class SubjectTeacherResponse
{
    public required Guid Id { get; init; }
    public required string Subject { get; init; }
    public required Guid TeacherAffiliationId { get; init; }
    public required string TeacherName { get; init; }
}

/// <summary>Add a single arm to an existing class, optionally with a class teacher.</summary>
public sealed class AddArmRequest
{
    public string Arm { get; init; } = string.Empty;     // "A", "B", "C"
    public Guid? TeacherAffiliationId { get; init; }     // optional class teacher
}

public sealed class SetClassTeacherRequest
{
    public Guid? TeacherAffiliationId { get; init; }   // null clears the class teacher
}

public sealed class AddSubjectTeacherRequest
{
    public Guid TeacherAffiliationId { get; init; }
    public string Subject { get; init; } = string.Empty;
}
