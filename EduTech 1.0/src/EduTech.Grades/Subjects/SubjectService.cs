using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Grades.Subjects;

public interface ISubjectService
{
    Task<IReadOnlyList<SubjectResponse>> ListAsync(Guid classId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> SuggestionsAsync(Guid classId, CancellationToken cancellationToken);
    Task<SubjectResponse> CreateAsync(Guid classId, CreateSubjectRequest request, CancellationToken cancellationToken);
    Task<int> SeedDefaultsAsync(Guid classId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid subjectId, CancellationToken cancellationToken);
}

internal sealed class SubjectService : ISubjectService
{
    private const int DefaultMaxCa = 30;
    private const int DefaultMaxExam = 40;

    private readonly ISubjectRepository _repository;

    public SubjectService(ISubjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<SubjectResponse>> ListAsync(Guid classId, CancellationToken cancellationToken)
    {
        await EnsureClassAsync(classId, cancellationToken);
        IReadOnlyList<SubjectRow> rows = await _repository.ListByClassAsync(classId, cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<string>> SuggestionsAsync(Guid classId, CancellationToken cancellationToken)
    {
        ClassLevel level = await ResolveLevelAsync(classId, cancellationToken);
        return SubjectDefaults.ForLevel(level);
    }

    public async Task<SubjectResponse> CreateAsync(Guid classId, CreateSubjectRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureClassAsync(classId, cancellationToken);

        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new AppErrorException("Subject name is required.", 400, ErrorCodes.ValidationError);
        }

        (int maxCa, int maxExam) = ValidateMaxes(request.MaxCa, request.MaxExam);

        if (await _repository.NameExistsAsync(classId, name, cancellationToken))
        {
            throw new AppErrorException($"'{name}' is already a subject for this class.", 409, ErrorCodes.Conflict);
        }

        Guid id = await _repository.CreateAsync(classId, name, maxCa, maxExam, order: 0, cancellationToken);
        return new SubjectResponse { Id = id, ClassId = classId, Name = name, MaxCa = maxCa, MaxExam = maxExam };
    }

    public async Task<int> SeedDefaultsAsync(Guid classId, CancellationToken cancellationToken)
    {
        ClassLevel level = await ResolveLevelAsync(classId, cancellationToken);
        IReadOnlyList<string> names = SubjectDefaults.ForLevel(level);
        if (names.Count == 0)
        {
            return 0;
        }

        return await _repository.SeedAsync(classId, names, DefaultMaxCa, DefaultMaxExam, cancellationToken);
    }

    public async Task DeleteAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        if (!await _repository.ExistsAsync(subjectId, cancellationToken))
        {
            throw new AppErrorException("Subject not found.", 404, ErrorCodes.NotFound);
        }

        await _repository.DeleteAsync(subjectId, cancellationToken);
    }

    private async Task EnsureClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        if (!await _repository.ClassExistsAsync(classId, cancellationToken))
        {
            throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        }
    }

    private async Task<ClassLevel> ResolveLevelAsync(Guid classId, CancellationToken cancellationToken)
    {
        string? level = await _repository.GetClassLevelAsync(classId, cancellationToken)
            ?? throw new AppErrorException("Class not found.", 404, ErrorCodes.NotFound);
        return SnakeCaseEnum.Parse<ClassLevel>(level);
    }

    /// <summary>
    /// Report-card totals are CA1 + CA2 + Exam resolved against the 0–100 grading scale, so the maxima
    /// must satisfy 2×MaxCa + MaxExam = 100 — otherwise totals can exceed the scale (or never reach it)
    /// and the grade prints as "-". Omitted values fall back to the 30/40 defaults; invalid values are
    /// rejected, never silently replaced.
    /// </summary>
    private static (int MaxCa, int MaxExam) ValidateMaxes(int? maxCa, int? maxExam)
    {
        int ca = maxCa ?? DefaultMaxCa;
        int exam = maxExam ?? DefaultMaxExam;

        if (ca is < 1 or > 100 || exam is < 1 or > 100)
        {
            throw new AppErrorException("CA and exam maximums must each be between 1 and 100.",
                400, ErrorCodes.ValidationError);
        }

        if (2 * ca + exam != 100)
        {
            throw new AppErrorException(
                $"CA and exam maximums must total 100 (2×CA + Exam); {ca}+{ca}+{exam} = {2 * ca + exam}.",
                400, ErrorCodes.ValidationError);
        }

        return (ca, exam);
    }

    private static SubjectResponse Map(SubjectRow r) => new SubjectResponse
    {
        Id = r.Id, ClassId = r.ClassId, Name = r.Name, MaxCa = r.MaxCa, MaxExam = r.MaxExam
    };
}
