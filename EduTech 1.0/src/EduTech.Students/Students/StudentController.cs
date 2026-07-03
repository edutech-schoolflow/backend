using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Students;

/// <summary>School-side student records (SchoolPortal; reads view, writes manage; owner bypasses).</summary>
[ApiController]
[Route("api/v1/students")]
[Authorize(Policy = "SchoolPortal")]
public sealed class StudentController : ControllerBase
{
    private readonly IStudentService _service;

    public StudentController(IStudentService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequireFeature(StaffFeatureFlags.ViewStudentRecords)]
    public async Task<ActionResult<ServiceResponses<StudentListResponse>>> List(
        [FromQuery] Guid? classId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        StudentListResponse result = await _service.ListAsync(classId, status, page, limit, cancellationToken);
        return Ok(ServiceResponses<StudentListResponse>.Ok(result, "Students."));
    }

    [HttpGet("{id:guid}")]
    [RequireFeature(StaffFeatureFlags.ViewStudentRecords)]
    public async Task<ActionResult<ServiceResponses<StudentResponse>>> Get(Guid id, CancellationToken cancellationToken)
    {
        StudentResponse student = await _service.GetAsync(id, cancellationToken);
        return Ok(ServiceResponses<StudentResponse>.Ok(student, "Student."));
    }

    [HttpPost]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<StudentResponse>>> Create(
        [FromBody] CreateStudentRequest request, CancellationToken cancellationToken)
    {
        StudentResponse student = await _service.CreateAsync(request, cancellationToken);
        return Ok(ServiceResponses<StudentResponse>.Ok(student, "Student admitted."));
    }

    [HttpPut("{id:guid}/contact")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> UpdateContact(Guid id,
        [FromBody] UpdateGuardiansRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateGuardiansAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Guardians updated."));
    }

    [HttpPost("{id:guid}/withdraw")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> Withdraw(Guid id, CancellationToken cancellationToken)
    {
        await _service.WithdrawAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Student withdrawn."));
    }

    [HttpPost("{id:guid}/re-admit")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> ReAdmit(Guid id, CancellationToken cancellationToken)
    {
        await _service.ReAdmitAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Student re-admitted."));
    }

    [HttpPost("{id:guid}/transfer")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> Transfer(Guid id,
        [FromBody] TransferStudentRequest request, CancellationToken cancellationToken)
    {
        await _service.TransferAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Student transferred."));
    }

    /// <summary>End-of-session promotion: advance/repeat/graduate a set of students into a target session.</summary>
    [HttpPost("promote")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<PromotionResultResponse>>> Promote(
        [FromBody] PromoteStudentsRequest request, CancellationToken cancellationToken)
    {
        PromotionResultResponse result = await _service.PromoteAsync(request, cancellationToken);
        return Ok(ServiceResponses<PromotionResultResponse>.Ok(result, "Students promoted."));
    }
}
