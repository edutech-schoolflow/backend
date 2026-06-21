using Dapper;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Shared.Persistence;

/// <summary>
/// Base for repositories over PER-SCHOOL (tenant-scoped) tables.
///
/// SchoolFlow is HYBRID multi-tenant: global identities (staff_users, parents, child_profiles,
/// platform_admins) have no school_id, while operational tables do. This base binds the current
/// tenant — resolved from the JWT via <see cref="IEduTechRequestContext"/> — as @SchoolId so callers
/// never source it by hand, and it throws if there is no school context.
///
/// CONVENTION (code-review rule): every tenant query MUST filter by @SchoolId, e.g.
///     WHERE school_id = @SchoolId
/// and every insert MUST set school_id = @SchoolId. A tenant-table query without @SchoolId is a bug.
/// Build the Dapper parameters via <see cref="TenantParameters"/>.
/// </summary>
public abstract class TenantRepository : BaseRepository
{
    private readonly IEduTechRequestContext _requestContext;

    protected TenantRepository(IDbConnectionFactory connectionFactory,
        IEduTechRequestContext requestContext)
        : base(connectionFactory)
    {
        _requestContext = requestContext;
    }

    /// <summary>
    /// The current tenant (school) for this request. Throws 403 if absent — a tenant repository
    /// must never run a query without a school context.
    /// </summary>
    protected Guid CurrentSchoolId
    {
        get
        {
            string? schoolId = _requestContext.SchoolId;
            if (string.IsNullOrWhiteSpace(schoolId) || !Guid.TryParse(schoolId, out Guid parsed))
            {
                throw new AppErrorException(
                    "No school context on this request.", 403, ErrorCodes.Forbidden);
            }

            return parsed;
        }
    }

    /// <summary>
    /// Builds Dapper parameters with the current tenant pre-bound as @SchoolId. Pass any extra
    /// parameters as an anonymous object; they are merged in. The SQL must still reference @SchoolId.
    /// </summary>
    protected DynamicParameters TenantParameters(object? parameters = null)
    {
        DynamicParameters result = new DynamicParameters(parameters);
        result.Add("SchoolId", CurrentSchoolId);
        return result;
    }
}
