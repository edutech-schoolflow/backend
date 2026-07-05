using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Shared.Auth;

/// <summary>Tenant-scoped: does the current school have a term flagged is_current?</summary>
internal sealed class CurrentTermProvider : TenantRepository, ICurrentTermProvider
{
    public CurrentTermProvider(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public async Task<bool> HasCurrentTermAsync(CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM terms WHERE school_id = @SchoolId AND is_current = TRUE",
            TenantParameters(), cancellationToken) > 0;
    }
}
