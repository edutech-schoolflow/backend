using EduTech.Shared.Constants;
using EduTech.Shared.Persistence;
using EduTech.Students.Classes.Domain;

namespace EduTech.Students.Classes;

/// <summary>
/// Creates a school's standard 6-3-3 classes from <see cref="NigerianEducationLadder"/>, matched to the
/// school's type. Idempotent: a school that already has any class is left untouched. Not tenant-scoped —
/// it runs from the activation event (platform-admin context), so every query takes the school id.
/// </summary>
public interface ISchoolClassProvisioner
{
    /// <summary>Provisions the standard classes if the school has none. Returns true if it created any.</summary>
    Task<bool> ProvisionIfMissingAsync(Guid schoolId, CancellationToken cancellationToken);
}

internal sealed class SchoolClassProvisioner : ISchoolClassProvisioner
{
    private readonly ISchoolClassProvisionRepository _repository;

    public SchoolClassProvisioner(ISchoolClassProvisionRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> ProvisionIfMissingAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        if (await _repository.HasClassesAsync(schoolId, cancellationToken))
        {
            return false;
        }

        string? type = await _repository.GetSchoolTypeAsync(schoolId, cancellationToken);
        IReadOnlyList<StandardGrade> grades = NigerianEducationLadder.GradesForType(type);
        if (grades.Count == 0)
        {
            return false;
        }

        await _repository.CreateClassesAsync(schoolId,
            grades.Select(g => (g.Name, g.Stage, g.Order)).ToList(), cancellationToken);
        return true;
    }
}

internal interface ISchoolClassProvisionRepository
{
    Task<bool> HasClassesAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<string?> GetSchoolTypeAsync(Guid schoolId, CancellationToken cancellationToken);
    Task CreateClassesAsync(Guid schoolId,
        IReadOnlyList<(string Name, ClassLevel Level, int Order)> classes, CancellationToken cancellationToken);
}

internal sealed class SchoolClassProvisionRepository : BaseRepository, ISchoolClassProvisionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SchoolClassProvisionRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> HasClassesAsync(Guid schoolId, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM classes WHERE school_id = @Id",
            new { Id = schoolId }, cancellationToken) > 0;

    public Task<string?> GetSchoolTypeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<string?>("SELECT type FROM schools WHERE id = @Id",
            new { Id = schoolId }, cancellationToken);

    public async Task CreateClassesAsync(Guid schoolId,
        IReadOnlyList<(string Name, ClassLevel Level, int Order)> classes, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        foreach ((string name, ClassLevel level, int order) in classes)
        {
            await ExecuteAsync(
                """
                INSERT INTO classes (school_id, name, level, display_order)
                VALUES (@SchoolId, @Name, @Level, @Order)
                ON CONFLICT (school_id, name) DO NOTHING
                """,
                new { SchoolId = schoolId, Name = name, Level = SnakeCaseEnum.ToWire(level), Order = order },
                cancellationToken, transaction.Transaction);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
