using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Grades.Subjects;

internal interface ISubjectRepository
{
    Task<IReadOnlyList<SubjectRow>> ListByClassAsync(Guid classId, CancellationToken cancellationToken);
    Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid classId, string name, CancellationToken cancellationToken);
    Task<string?> GetClassLevelAsync(Guid classId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(Guid classId, string name, int maxCa, int maxExam, int order, CancellationToken cancellationToken);

    /// <summary>Bulk-insert default subjects, skipping any name already in the class. Returns count added.</summary>
    Task<int> SeedAsync(Guid classId, IReadOnlyList<string> names, int maxCa, int maxExam, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid subjectId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid subjectId, CancellationToken cancellationToken);
}

internal sealed class SubjectRow
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MaxCa { get; init; }
    public int MaxExam { get; init; }
}

internal sealed class SubjectRepository : TenantRepository, ISubjectRepository
{
    public SubjectRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<IReadOnlyList<SubjectRow>> ListByClassAsync(Guid classId, CancellationToken cancellationToken)
    {
        return QueryAsync<SubjectRow>(
            """
            SELECT id, class_id AS ClassId, name, max_ca AS MaxCa, max_exam AS MaxExam
            FROM subjects
            WHERE school_id = @SchoolId AND class_id = @ClassId
            ORDER BY display_order, name
            """,
            TenantParameters(new { ClassId = classId }), cancellationToken);
    }

    public async Task<bool> ClassExistsAsync(Guid classId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM classes WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken) > 0;
    }

    public async Task<bool> NameExistsAsync(Guid classId, string name, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM subjects WHERE school_id = @SchoolId AND class_id = @ClassId AND lower(name) = lower(@Name)",
            TenantParameters(new { ClassId = classId, Name = name }), cancellationToken) > 0;
    }

    public Task<string?> GetClassLevelAsync(Guid classId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT level FROM classes WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = classId }), cancellationToken);
    }

    public Task<Guid> CreateAsync(Guid classId, string name, int maxCa, int maxExam, int order,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO subjects (school_id, class_id, name, max_ca, max_exam, display_order)
            VALUES (@SchoolId, @ClassId, @Name, @MaxCa, @MaxExam, @Order)
            RETURNING id
            """,
            TenantParameters(new { ClassId = classId, Name = name, MaxCa = maxCa, MaxExam = maxExam, Order = order }),
            cancellationToken);
    }

    public async Task<int> SeedAsync(Guid classId, IReadOnlyList<string> names, int maxCa, int maxExam,
        CancellationToken cancellationToken)
    {
        int added = 0;
        for (int i = 0; i < names.Count; i++)
        {
            added += await ExecuteAsync(
                """
                INSERT INTO subjects (school_id, class_id, name, max_ca, max_exam, display_order)
                VALUES (@SchoolId, @ClassId, @Name, @MaxCa, @MaxExam, @Order)
                ON CONFLICT (class_id, name) DO NOTHING
                """,
                TenantParameters(new { ClassId = classId, Name = names[i], MaxCa = maxCa, MaxExam = maxExam, Order = i }),
                cancellationToken);
        }

        return added;
    }

    public async Task<bool> ExistsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM subjects WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = subjectId }), cancellationToken) > 0;
    }

    public Task DeleteAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "DELETE FROM subjects WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = subjectId }), cancellationToken);
    }
}
