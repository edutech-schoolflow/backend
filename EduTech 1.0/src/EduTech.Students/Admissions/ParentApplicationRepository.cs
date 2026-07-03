using EduTech.Shared.Persistence;

namespace EduTech.Students.Admissions;

/// <summary>Parent-facing applications — ownership-scoped (a parent only touches their own).</summary>
internal interface IParentApplicationRepository
{
    Task<bool> ParentOwnsChildAsync(Guid parentId, Guid childProfileId, CancellationToken cancellationToken);
    Task<bool> SchoolExistsAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<bool> ChildActiveAtSchoolAsync(Guid childProfileId, Guid schoolId, CancellationToken cancellationToken);
    Task<bool> HasOpenApplicationAsync(Guid childProfileId, Guid schoolId, CancellationToken cancellationToken);

    Task<ApplicationRow> SubmitAsync(Guid parentId, Guid childProfileId, Guid schoolId, string? desiredClass,
        Guid? termId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApplicationRow>> ListByParentAsync(Guid parentId, CancellationToken cancellationToken);
    Task<ApplicationRow?> GetForParentAsync(Guid parentId, Guid applicationId, CancellationToken cancellationToken);
    Task<int> MarkPaidAsync(Guid parentId, Guid applicationId, string reference, CancellationToken cancellationToken);
}

internal sealed class ParentApplicationRepository : BaseRepository, IParentApplicationRepository
{
    public ParentApplicationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<bool> ParentOwnsChildAsync(Guid parentId, Guid childProfileId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM parent_children WHERE parent_id = @ParentId AND child_profile_id = @ChildProfileId",
            new { ParentId = parentId, ChildProfileId = childProfileId }, cancellationToken) > 0;
    }

    public async Task<bool> SchoolExistsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM schools WHERE id = @Id", new { Id = schoolId }, cancellationToken) > 0;
    }

    public async Task<bool> ChildActiveAtSchoolAsync(Guid childProfileId, Guid schoolId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM students WHERE child_profile_id = @C AND school_id = @S AND status = 'active'",
            new { C = childProfileId, S = schoolId }, cancellationToken) > 0;
    }

    public async Task<bool> HasOpenApplicationAsync(Guid childProfileId, Guid schoolId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM applications WHERE child_profile_id = @C AND school_id = @S " +
            "AND status IN ('under_review', 'exam_scheduled')",
            new { C = childProfileId, S = schoolId }, cancellationToken) > 0;
    }

    public async Task<ApplicationRow> SubmitAsync(Guid parentId, Guid childProfileId, Guid schoolId,
        string? desiredClass, Guid? termId, CancellationToken cancellationToken)
    {
        string reference = $"APP/{DateTime.UtcNow.Year}/{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        Guid id = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO applications (reference_number, child_profile_id, parent_id, school_id, desired_class, term_id)
            VALUES (@Reference, @ChildProfileId, @ParentId, @SchoolId, @DesiredClass, @TermId)
            RETURNING id
            """,
            new { Reference = reference, ChildProfileId = childProfileId, ParentId = parentId, SchoolId = schoolId,
                  DesiredClass = desiredClass, TermId = termId },
            cancellationToken);

        return (await GetForParentAsync(parentId, id, cancellationToken))!;
    }

    public Task<IReadOnlyList<ApplicationRow>> ListByParentAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QueryAsync<ApplicationRow>(
            $"SELECT {ApplicationSql.Columns} {ApplicationSql.From} WHERE a.parent_id = @ParentId ORDER BY a.created_at DESC",
            new { ParentId = parentId }, cancellationToken);
    }

    public Task<ApplicationRow?> GetForParentAsync(Guid parentId, Guid applicationId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<ApplicationRow>(
            $"SELECT {ApplicationSql.Columns} {ApplicationSql.From} WHERE a.id = @Id AND a.parent_id = @ParentId",
            new { Id = applicationId, ParentId = parentId }, cancellationToken);
    }

    public Task<int> MarkPaidAsync(Guid parentId, Guid applicationId, string reference, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE applications SET application_fee_paid = TRUE, payment_reference = @Reference, updated_at = NOW()
            WHERE id = @Id AND parent_id = @ParentId
            """,
            new { Id = applicationId, ParentId = parentId, Reference = reference }, cancellationToken);
    }
}
