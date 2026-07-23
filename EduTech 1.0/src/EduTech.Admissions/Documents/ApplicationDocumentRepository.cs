using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Documents;

internal interface IApplicationDocumentRepository
{
    Task<Guid> RequestAsync(Guid applicationId, string docType, bool required, CancellationToken cancellationToken);
    Task<ApplicationDocument?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationDocument>> ListForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task SaveAsync(ApplicationDocument document, CancellationToken cancellationToken);
}

internal sealed class ApplicationDocumentRow
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string DocType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string Status { get; init; } = "pending";
    public string? FileUrl { get; init; }
    public string? Notes { get; init; }
    public Guid? VerifiedBy { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class ApplicationDocumentRepository : TenantRepository, IApplicationDocumentRepository
{
    private const string Columns =
        "id AS Id, application_id AS ApplicationId, doc_type AS DocType, required AS Required, status, " +
        "file_url AS FileUrl, notes AS Notes, verified_by AS VerifiedBy, created_at AS CreatedAt";

    public ApplicationDocumentRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> RequestAsync(Guid applicationId, string docType, bool required, CancellationToken cancellationToken)
    {
        // The application must belong to this tenant; the INSERT's school_id is bound and the FK enforces it.
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO application_documents (application_id, school_id, doc_type, required, status)
            SELECT a.id, @SchoolId, @DocType, @Required, 'pending'
            FROM admission_applications a
            WHERE a.id = @ApplicationId AND a.school_id = @SchoolId
            RETURNING id
            """,
            TenantParameters(new { ApplicationId = applicationId, DocType = docType, Required = required }),
            cancellationToken);
    }

    public async Task<ApplicationDocument?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken)
    {
        ApplicationDocumentRow? row = await QuerySingleOrDefaultAsync<ApplicationDocumentRow>(
            $"SELECT {Columns} FROM application_documents WHERE id = @Id AND application_id = @ApplicationId AND school_id = @SchoolId",
            TenantParameters(new { Id = documentId, ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public async Task<IReadOnlyList<ApplicationDocument>> ListForApplicationAsync(Guid applicationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationDocumentRow> rows = await QueryAsync<ApplicationDocumentRow>(
            $"SELECT {Columns} FROM application_documents WHERE application_id = @ApplicationId AND school_id = @SchoolId ORDER BY created_at",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return rows.Select(Rehydrate).ToList();
    }

    public Task SaveAsync(ApplicationDocument document, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE application_documents
               SET status = @Status, file_url = @FileUrl, notes = @Notes, verified_by = @VerifiedBy, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new
            {
                Id = document.Id, Status = ToDb(document.Status), FileUrl = document.FileUrl,
                Notes = document.Notes, VerifiedBy = document.VerifiedBy
            }),
            cancellationToken);
    }

    private static string ToDb(DocumentStatus status) => status switch
    {
        DocumentStatus.Uploaded => "uploaded",
        DocumentStatus.Verified => "verified",
        DocumentStatus.Rejected => "rejected",
        _ => "pending"
    };

    private static ApplicationDocument Rehydrate(ApplicationDocumentRow r) => new(
        r.Id, r.ApplicationId, r.DocType, r.Required,
        r.Status switch
        {
            "uploaded" => DocumentStatus.Uploaded,
            "verified" => DocumentStatus.Verified,
            "rejected" => DocumentStatus.Rejected,
            _ => DocumentStatus.Pending
        },
        r.FileUrl, r.Notes, r.VerifiedBy, r.CreatedAt);
}
