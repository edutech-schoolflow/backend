using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Documents;

public sealed class RequestDocumentRequest
{
    public string DocType { get; init; } = string.Empty;
    public bool Required { get; init; } = true;
}

public sealed class RejectDocumentRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class DocumentResponse
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string DocType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public DocumentStatus Status { get; init; }
    public string? FileUrl { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
}
