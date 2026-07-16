using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Students.Academics;

/// <summary>
/// Per-school academic calendar (years + terms). Owner or staff-with-school context (SchoolPortal);
/// reads need can_view_student_records, writes need can_manage_admissions (owner bypasses both).
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class AcademicCalendarController : ControllerBase
{
    private readonly IAcademicCalendarService _service;

    public AcademicCalendarController(IAcademicCalendarService service)
    {
        _service = service;
    }

    [HttpGet("api/v1/academic-years")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<AcademicYearResponse>>>> ListYears(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AcademicYearResponse> years = await _service.ListYearsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<AcademicYearResponse>>.Ok(years, "Academic years."));
    }

    [HttpPost("api/v1/academic-years")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<AcademicYearResponse>>> CreateYear(
        [FromBody] CreateAcademicYearRequest request, CancellationToken cancellationToken)
    {
        AcademicYearResponse year = await _service.CreateYearAsync(request, cancellationToken);
        return Ok(ServiceResponses<AcademicYearResponse>.Ok(year, "Academic year created."));
    }

    [HttpPut("api/v1/academic-years/{id:guid}/current")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> SetCurrentYear(Guid id,
        CancellationToken cancellationToken)
    {
        await _service.SetCurrentYearAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Current academic year set."));
    }

    [HttpPut("api/v1/academic-years/{id:guid}")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> UpdateYear(Guid id,
        [FromBody] UpdateAcademicYearRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateYearAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Session updated."));
    }

    [HttpDelete("api/v1/academic-years/{id:guid}")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> DeleteYear(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteYearAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Session deleted."));
    }

    [HttpGet("api/v1/terms")]
    [RequireCapability(Capabilities.Student.Read)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<TermResponse>>>> ListTerms(
        [FromQuery] Guid? academicYearId, CancellationToken cancellationToken)
    {
        IReadOnlyList<TermResponse> terms = await _service.ListTermsAsync(academicYearId, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<TermResponse>>.Ok(terms, "Terms."));
    }

    [HttpPost("api/v1/terms")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<TermResponse>>> CreateTerm(
        [FromBody] CreateTermRequest request, CancellationToken cancellationToken)
    {
        TermResponse term = await _service.CreateTermAsync(request, cancellationToken);
        return Ok(ServiceResponses<TermResponse>.Ok(term, "Term created."));
    }

    [HttpPut("api/v1/terms/{id:guid}/current")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> SetCurrentTerm(Guid id,
        CancellationToken cancellationToken)
    {
        await _service.SetCurrentTermAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Current term set."));
    }

    [HttpPut("api/v1/terms/{id:guid}")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> UpdateTerm(Guid id,
        [FromBody] UpdateTermDatesRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateTermDatesAsync(id, request, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Term updated."));
    }

    [HttpDelete("api/v1/terms/{id:guid}")]
    [RequireCapability(Capabilities.Admissions.Manage)]
    public async Task<ActionResult<ServiceResponses<string?>>> DeleteTerm(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteTermAsync(id, cancellationToken);
        return Ok(ServiceResponses<string?>.Ok(null, "Term deleted."));
    }
}
