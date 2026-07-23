using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Admissions.Documents;

/// <summary>
/// An application's document checklist (EDD-014 Slice 4): request → upload → verify/reject. Reads gate
/// on Student.Read, writes on Admissions.Manage. Uploads go to platform Storage.
/// </summary>
[ApiController]
[Route("api/v1/admissions/applications/{applicationId:guid}/documents")]
[Authorize(Policy = "SchoolPortal")]
public sealed class DocumentController : ControllerBase
{
    private readonly IDocumentService _service;

    public DocumentController(IDocumentService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<DocumentResponse>>> RequestDocument(
        Guid applicationId, [FromBody] RequestDocumentRequest request, CancellationToken cancellationToken)
    {
        DocumentResponse doc = await _service.RequestAsync(applicationId, request, cancellationToken);
        return Ok(ServiceResponses<DocumentResponse>.Ok(doc, "Document requested."));
    }

    [HttpGet]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<DocumentResponse>>>> List(
        Guid applicationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentResponse> docs = await _service.ListAsync(applicationId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<DocumentResponse>>.Ok(docs, "Documents."));
    }

    [HttpPost("{documentId:guid}/upload")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<DocumentResponse>>> Upload(
        Guid applicationId, Guid documentId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new AppErrorException("Attach a file to upload.", 400, ErrorCodes.ValidationError);
        }

        await using Stream stream = file.OpenReadStream();
        DocumentResponse doc = await _service.UploadAsync(applicationId, documentId, stream, file.ContentType,
            file.FileName, cancellationToken);
        return Ok(ServiceResponses<DocumentResponse>.Ok(doc, "Document uploaded."));
    }

    [HttpPost("{documentId:guid}/verify")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<DocumentResponse>>> Verify(
        Guid applicationId, Guid documentId, CancellationToken cancellationToken)
    {
        DocumentResponse doc = await _service.VerifyAsync(applicationId, documentId, cancellationToken);
        return Ok(ServiceResponses<DocumentResponse>.Ok(doc, "Document verified."));
    }

    [HttpPost("{documentId:guid}/reject")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<DocumentResponse>>> Reject(
        Guid applicationId, Guid documentId, [FromBody] RejectDocumentRequest request, CancellationToken cancellationToken)
    {
        DocumentResponse doc = await _service.RejectAsync(applicationId, documentId, request.Reason, cancellationToken);
        return Ok(ServiceResponses<DocumentResponse>.Ok(doc, "Document rejected."));
    }
}
