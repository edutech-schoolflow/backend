using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Models;
using EduTech.Shared.Persistence;
using EduTech.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.School.Branding;

/// <summary>The school's logo (shown in the directory, workspace chrome and public profile).</summary>
[ApiController]
[Route("api/v1/school")]
[Authorize(Policy = "SchoolPortal")]
public sealed class SchoolBrandingController : ControllerBase
{
    private const long MaxLogoBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IEduTechRequestContext _context;
    private readonly IFileStorage _fileStorage;
    private readonly ISchoolBrandingRepository _repository;

    public SchoolBrandingController(IEduTechRequestContext context, IFileStorage fileStorage,
        ISchoolBrandingRepository repository)
    {
        _context = context;
        _fileStorage = fileStorage;
        _repository = repository;
    }

    /// <summary>Owner-only: upload (or replace) the school's logo. Returns the stored URL.</summary>
    [HttpPost("logo")]
    public async Task<ActionResult<ServiceResponses<object>>> UploadLogo(
        IFormFile? logo, CancellationToken cancellationToken)
    {
        if (!_context.IsOwner)
        {
            throw new AppErrorException("Only the school owner can change the logo.", 403, ErrorCodes.Forbidden);
        }

        if (!Guid.TryParse(_context.SchoolId, out Guid schoolId))
        {
            return Unauthorized();
        }

        if (logo is null || logo.Length == 0)
        {
            throw new AppErrorException("Choose a logo image to upload.", 400, ErrorCodes.ValidationError);
        }

        if (logo.Length > MaxLogoBytes)
        {
            throw new AppErrorException("The logo must be 5 MB or smaller.", 400, ErrorCodes.ValidationError);
        }

        string key = $"schools/{schoolId}/logo-{Guid.NewGuid():N}{Path.GetExtension(logo.FileName)}";
        await using Stream stream = logo.OpenReadStream();
        string url = await _fileStorage.UploadAsync(stream, key, logo.ContentType, cancellationToken);

        await _repository.SetLogoUrlAsync(schoolId, url, cancellationToken);
        return Ok(ServiceResponses<object>.Ok(new { logoUrl = url }, "Logo updated."));
    }
}

public interface ISchoolBrandingRepository
{
    Task SetLogoUrlAsync(Guid schoolId, string url, CancellationToken cancellationToken);
}

internal sealed class SchoolBrandingRepository : BaseRepository, ISchoolBrandingRepository
{
    public SchoolBrandingRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task SetLogoUrlAsync(Guid schoolId, string url, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE schools SET logo_url = @Url, updated_at = NOW() WHERE id = @Id",
            new { Id = schoolId, Url = url }, cancellationToken);
    }
}
