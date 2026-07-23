using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum DocumentStatus
{
    Pending,
    Uploaded,
    Verified,
    Rejected
}

/// <summary>
/// ApplicationDocument (EDD-014) — one item on an application's document checklist, with its own
/// lifecycle: pending → uploaded → (verified | rejected); a rejected document can be re-uploaded.
/// The file lives in platform Storage (<see cref="FileUrl"/>).
/// </summary>
internal sealed class ApplicationDocument
{
    public ApplicationDocument(Guid id, Guid applicationId, string docType, bool required,
        DocumentStatus status, string? fileUrl, string? notes, Guid? verifiedBy, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(docType))
        {
            throw new AppErrorException("A document needs a type.", 400, ErrorCodes.ValidationError);
        }

        Id = id;
        ApplicationId = applicationId;
        DocType = docType.Trim();
        Required = required;
        Status = status;
        FileUrl = fileUrl;
        Notes = notes;
        VerifiedBy = verifiedBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public string DocType { get; }
    public bool Required { get; }
    public DocumentStatus Status { get; private set; }
    public string? FileUrl { get; private set; }
    public string? Notes { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public DateTime CreatedAt { get; }

    /// <summary>Records an uploaded file (from pending or after a rejection). Clears any prior note.</summary>
    public void Upload(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            throw new AppErrorException("Upload produced no file reference.", 400, ErrorCodes.ValidationError);
        }
        if (Status == DocumentStatus.Verified)
        {
            throw new AppErrorException("This document is already verified.", 409, ErrorCodes.Conflict);
        }

        FileUrl = fileUrl;
        Notes = null;
        Status = DocumentStatus.Uploaded;
    }

    public void Verify(Guid? verifiedBy)
    {
        RequireUploaded();
        VerifiedBy = verifiedBy;
        Status = DocumentStatus.Verified;
    }

    public void Reject(string reason)
    {
        RequireUploaded();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AppErrorException("Give a reason for rejecting the document.", 400, ErrorCodes.ValidationError);
        }

        Notes = reason.Trim();
        Status = DocumentStatus.Rejected;
    }

    private void RequireUploaded()
    {
        if (Status != DocumentStatus.Uploaded)
        {
            throw new AppErrorException("Only an uploaded document can be verified or rejected.", 409,
                ErrorCodes.Conflict, logReason: $"Verify/Reject attempted on document in status {Status}.");
        }
    }
}
