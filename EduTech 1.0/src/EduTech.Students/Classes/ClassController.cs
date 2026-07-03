using EduTech.Shared.Auth;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Classes;

/// <summary>Classes, arms, and teacher assignments (SchoolPortal; reads view, writes manage; owner bypasses).</summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class ClassController : ControllerBase
{
    private readonly IClassService _service;

    public ClassController(IClassService service)
    {
        _service = service;
    }

    [HttpGet("api/v1/classes")]
    [RequireFeature(StaffFeatureFlags.ViewStudentRecords)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<SchoolClassResponse>>>> ListClasses(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SchoolClassResponse> classes = await _service.ListClassesAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<SchoolClassResponse>>.Ok(classes, "Classes."));
    }

    [HttpPost("api/v1/classes")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<SchoolClassResponse>>> CreateClass(
        [FromBody] CreateClassRequest request, CancellationToken cancellationToken)
    {
        SchoolClassResponse created = await _service.CreateClassAsync(request, cancellationToken);
        return Ok(ServiceResponses<SchoolClassResponse>.Ok(created, "Class created."));
    }

    [HttpGet("api/v1/classes/{id:guid}")]
    [RequireFeature(StaffFeatureFlags.ViewStudentRecords)]
    public async Task<ActionResult<ServiceResponses<SchoolClassResponse>>> GetClass(Guid id,
        CancellationToken cancellationToken)
    {
        SchoolClassResponse cls = await _service.GetClassAsync(id, cancellationToken);
        return Ok(ServiceResponses<SchoolClassResponse>.Ok(cls, "Class."));
    }

    [HttpDelete("api/v1/classes/{id:guid}")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> DeleteClass(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteClassAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Class deleted."));
    }

    [HttpPut("api/v1/classes/{id:guid}/class-teacher")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> SetClassLevelTeacher(Guid id,
        [FromBody] SetClassTeacherRequest request, CancellationToken cancellationToken)
    {
        await _service.SetClassLevelTeacherAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Class teacher updated."));
    }

    [HttpGet("api/v1/classes/{id:guid}/arms")]
    [RequireFeature(StaffFeatureFlags.ViewStudentRecords)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<ClassArmResponse>>>> ListArms(Guid id,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClassArmResponse> arms = await _service.ListArmsAsync(id, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<ClassArmResponse>>.Ok(arms, "Class arms."));
    }

    [HttpPost("api/v1/classes/{id:guid}/arms")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<ClassArmResponse>>> AddArm(Guid id,
        [FromBody] AddArmRequest request, CancellationToken cancellationToken)
    {
        ClassArmResponse arm = await _service.AddArmAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<ClassArmResponse>.Ok(arm, "Arm added."));
    }

    [HttpPut("api/v1/arms/{id:guid}/class-teacher")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> SetClassTeacher(Guid id,
        [FromBody] SetClassTeacherRequest request, CancellationToken cancellationToken)
    {
        await _service.SetClassTeacherAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Class teacher updated."));
    }

    [HttpPost("api/v1/arms/{id:guid}/subject-teachers")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<SubjectTeacherResponse>>> AddSubjectTeacher(Guid id,
        [FromBody] AddSubjectTeacherRequest request, CancellationToken cancellationToken)
    {
        SubjectTeacherResponse added = await _service.AddSubjectTeacherAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<SubjectTeacherResponse>.Ok(added, "Subject teacher added."));
    }

    [HttpDelete("api/v1/subject-teachers/{id:guid}")]
    [RequireFeature(StaffFeatureFlags.ManageAdmissions)]
    public async Task<ActionResult<ServiceResponses<string?>>> RemoveSubjectTeacher(Guid id,
        CancellationToken cancellationToken)
    {
        await _service.RemoveSubjectTeacherAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Subject teacher removed."));
    }
}
