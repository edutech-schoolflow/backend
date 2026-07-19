using System.Security.Claims;
using EduTech.Shared.Constants;

namespace EduTech.Shared.Auth;

/// <summary>
/// Portal-eligibility gates for the multi-persona authorization policies (EDD-012 B2c.3a).
///
/// <para><b>Authorization must never rely on cryptographic separation.</b> A single signing key now
/// authenticates every identity/portal token, so a scheme (or the key that validated the token) can no
/// longer tell one persona from another — it only proves the token is authentic. Any business
/// restriction (which portal, which actor) is therefore expressed <i>explicitly</i> as authorization,
/// here on the <c>user_type</c> claim. These restore, in the open, the gating that per-portal signing
/// keys used to enforce implicitly for <c>SchoolPortal</c> and <c>ComplianceActor</c>.</para>
/// </summary>
public static class PortalGates
{
    /// <summary>School-management surface — the owner or a staff member (never a parent or a bare identity).</summary>
    public static bool IsSchoolOrStaff(ClaimsPrincipal user) =>
        user.FindFirst("user_type")?.Value is UserTypes.School or UserTypes.Staff;

    /// <summary>Compliance actions — a staff member or a parent (never the owner or a bare identity).</summary>
    public static bool IsStaffOrParent(ClaimsPrincipal user) =>
        user.FindFirst("user_type")?.Value is UserTypes.Staff or UserTypes.Parent;
}
