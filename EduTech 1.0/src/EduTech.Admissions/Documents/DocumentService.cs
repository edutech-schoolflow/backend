using EduTech.Admissions.Domain;
using EduTech.Admissions.Events;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Storage;

namespace EduTech.Admissions.Documents;

/// <summary>Document-checklist commands + queries (EDD-014 Slice 4). Consumes platform Storage.</summary>
public interface IDocumentService
{
    Task<DocumentResponse> RequestAsync(Guid applicationId, RequestDocumentRequest request, CancellationToken cancellationToken);
    Task<DocumentResponse> UploadAsync(Guid applicationId, Guid documentId, Stream content, string contentType,
        string fileName, CancellationToken cancellationToken);
    Task<DocumentResponse> VerifyAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken);
    Task<DocumentResponse> RejectAsync(Guid applicationId, Guid documentId, string reason, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken);
}

internal sealed class DocumentService : IDocumentService
{
    private readonly IApplicationDocumentRepository _documents;
    private readonly IFileStorage _storage;
    private readonly IDomainEventPublisher _events;
    private readonly IEduTechRequestContext _context;

    public DocumentService(IApplicationDocumentRepository documents, IFileStorage storage,
        IDomainEventPublisher events, IEduTechRequestContext context)
    {
        _documents = documents;
        _storage = storage;
        _events = events;
        _context = context;
    }

    public async Task<DocumentResponse> RequestAsync(Guid applicationId, RequestDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocType))
        {
            throw new AppErrorException("Choose a document type.", 400, ErrorCodes.ValidationError);
        }

        Guid id = await _documents.RequestAsync(applicationId, request.DocType.Trim(), request.Required, cancellationToken);
        if (id == Guid.Empty)
        {
            throw new AppErrorException("Application not found.", 404, ErrorCodes.NotFound);
        }

        return Map(await LoadAsync(applicationId, id, cancellationToken));
    }

    public async Task<DocumentResponse> UploadAsync(Guid applicationId, Guid documentId, Stream content,
        string contentType, string fileName, CancellationToken cancellationToken)
    {
        ApplicationDocument document = await LoadAsync(applicationId, documentId, cancellationToken);

        string schoolId = _context.SchoolId ?? "unknown";
        string ext = Path.GetExtension(fileName);
        string key = $"admissions/{schoolId}/applications/{applicationId}/documents/{documentId:N}{ext}";
        string url = await _storage.UploadAsync(content, key, contentType, cancellationToken);

        document.Upload(url);
        await _documents.SaveAsync(document, cancellationToken);
        return Map(document);
    }

    public async Task<DocumentResponse> VerifyAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken)
    {
        ApplicationDocument document = await LoadAsync(applicationId, documentId, cancellationToken);
        document.Verify(verifiedBy: null);   // the audit trail records the actor from context
        await _documents.SaveAsync(document, cancellationToken);

        Guid schoolId = Guid.TryParse(_context.SchoolId, out Guid sid) ? sid : Guid.Empty;
        await _events.PublishAsync(new DocumentVerified(document.Id, applicationId, schoolId, document.DocType), cancellationToken);
        return Map(document);
    }

    public async Task<DocumentResponse> RejectAsync(Guid applicationId, Guid documentId, string reason,
        CancellationToken cancellationToken)
    {
        ApplicationDocument document = await LoadAsync(applicationId, documentId, cancellationToken);
        document.Reject(reason);
        await _documents.SaveAsync(document, cancellationToken);
        return Map(document);
    }

    public async Task<IReadOnlyList<DocumentResponse>> ListAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationDocument> docs = await _documents.ListForApplicationAsync(applicationId, cancellationToken);
        return docs.Select(Map).ToList();
    }

    private async Task<ApplicationDocument> LoadAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken) =>
        await _documents.GetAsync(applicationId, documentId, cancellationToken)
        ?? throw new AppErrorException("Document not found.", 404, ErrorCodes.NotFound);

    private static DocumentResponse Map(ApplicationDocument d) => new()
    {
        Id = d.Id,
        ApplicationId = d.ApplicationId,
        DocType = d.DocType,
        Required = d.Required,
        Status = d.Status,
        FileUrl = d.FileUrl,
        Notes = d.Notes,
        CreatedAt = d.CreatedAt
    };
}
