using EduTech.Shared.Auth;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Students.Academics;

/// <summary>
/// Academics' answer to the SharedKernel port <see cref="ICurrentTermProvider"/> (behind
/// [RequiresCurrentTerm]). Lives here — not in Shared — because "does this school have a current
/// term" is an Academics question over Academics' tables (EDD-002 V5).
/// </summary>
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
