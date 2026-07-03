namespace EduTech.Grades.Subjects;

/// <summary>A subject in a class's catalog (mirrors the frontend Subject).</summary>
public sealed class SubjectResponse
{
    public required Guid Id { get; init; }
    public required Guid ClassId { get; init; }
    public required string Name { get; init; }
    public required int MaxCa { get; init; }
    public required int MaxExam { get; init; }
}

public sealed class CreateSubjectRequest
{
    public string Name { get; init; } = string.Empty;
    public int? MaxCa { get; init; }    // per-CA cap, default 30
    public int? MaxExam { get; init; }  // exam cap, default 40
}
