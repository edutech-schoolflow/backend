using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduTech.Shared.Constants;
using Microsoft.IdentityModel.Tokens;

namespace EduTech.Shared.Auth;

public static class TokenVendor
{
    /// <summary>
    /// Core token issuer. Key must be at least 64 bytes for HmacSha512.
    /// </summary>
    public static string VendToken(string signingKey, string issuer, string audience,
        IEnumerable<Claim> claims, int expiryMinutes = 1440)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);
        SigningCredentials credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha512Signature);

        SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            SigningCredentials = credentials
        };

        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    /// <summary>
    /// Issues a SCHOOL-SCOPED staff token for the active affiliation. Carries the active school,
    /// affiliation id, role, employment type, and platform kyc_status. Authorization is NOT in the
    /// token (EDD-012 B2c.2) — capabilities resolve server-side from context_id (B2b).
    /// 30-minute access token. Portal: staff.schoolflow.com. Signing key: Jwt:StaffSigningKey.
    /// </summary>
    public static string VendStaffScopedToken(string signingKey, string issuer, string audience,
        string userId, string phone, string schoolId, string affiliationId, string role,
        string employmentType, string kycStatus,
        int expiryMinutes = 30,
        string? identityId = null, string? contextId = null,
        string? membershipId = null, string? organizationId = null)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", userId),
            new Claim("user_type", UserTypes.Staff),
            new Claim("is_owner", "false"),
            new Claim("school_id", schoolId),
            new Claim("affiliation_id", affiliationId),
            new Claim("role", role),
            new Claim("phone", phone)
        };
        // EDD-012 B2c.3d (JWT Simplification): the transitional claims active_school_id / employment_type /
        // kyc_status are retired (0 readers, Appendix B). The 13 permission flags left in B2c.2; there is
        // no mint-time feature resolution any more. employmentType/kycStatus remain as parameters (dead
        // plumbing) until B2d retires the mint signatures with the legacy actor pipeline.
        _ = (employmentType, kycStatus);

        if (identityId is not null) claims.Add(new Claim("identity_id", identityId));
        if (contextId is not null) claims.Add(new Claim("context_id", contextId));
        // Canonical identity + organization of the context (EDD-012 B2c.1). Additive; authorization
        // resolves from context_id, not these.
        if (membershipId is not null) claims.Add(new Claim("membership_id", membershipId));
        if (organizationId is not null) claims.Add(new Claim("organization_id", organizationId));

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }

    /// <summary>
    /// Issues an IDENTITY-ONLY staff token — for a standalone staff member with no active school
    /// affiliation. No active_school_id, role, or feature claims; just the global identity + platform
    /// KYC status. (A school-scoped token is issued separately when an affiliation is selected.)
    /// 30-minute access token. Signing key: Jwt:StaffSigningKey.
    /// </summary>
    public static string VendStaffIdentityToken(string signingKey, string issuer, string audience,
        string userId, string phone, string kycStatus, int expiryMinutes = 30)
    {
        _ = kycStatus;  // B2c.3d: kyc_status claim retired (0 readers); parameter kept until B2d.
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", userId),
            new Claim("user_type", UserTypes.Staff),
            new Claim("is_owner", "false"),
            new Claim("phone", phone)
        };

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }

    /// <summary>
    /// Issues an access token for the school owner (legal proprietor).
    /// isOwner === true — bypasses all feature/role checks.
    /// Carries school_status/kyc_status so the API can enforce read-only mode (pre-KYC) without a
    /// DB hit. Portal: {subdomain}.schoolflow.com. Signing key: Jwt:SchoolSigningKey.
    /// 30-minute access token per Cross-Cutting Auth §X.1.
    /// </summary>
    public static string VendSchoolOwnerToken(string signingKey, string issuer, string audience,
        string userId, string schoolId, string phone, string schoolStatus, string kycStatus,
        string? subdomain, int expiryMinutes = 30,
        string? identityId = null, string? contextId = null,
        string? membershipId = null, string? organizationId = null)
    {
        // B2c.3d: school_status / kyc_status / subdomain claims retired (0 readers, Appendix B). The
        // parameters remain as dead plumbing until B2d retires the mint signatures.
        _ = (schoolStatus, kycStatus, subdomain);
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", userId),
            new Claim("user_type", UserTypes.School),
            new Claim("school_id", schoolId),
            new Claim("is_owner", "true"),
            new Claim("role", "owner"),
            new Claim("phone", phone)
        };

        if (identityId is not null) claims.Add(new Claim("identity_id", identityId));
        if (contextId is not null) claims.Add(new Claim("context_id", contextId));
        // Canonical identity + organization of the context (EDD-012 B2c.1). Additive.
        if (membershipId is not null) claims.Add(new Claim("membership_id", membershipId));
        if (organizationId is not null) claims.Add(new Claim("organization_id", organizationId));

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }

    /// <summary>
    /// Issues a token for a parent. Parents are SCHOOL-AGNOSTIC — NO school_id claim; access to a
    /// child's data is authorized per-request via the parent→child→student links (spec §3.3).
    /// Portal: parent.schoolflow.com. Signing key: Jwt:ParentSigningKey. 30-minute access token.
    /// </summary>
    /// <summary>
    /// Identity-scope token (EDD-001): a signed-in PERSON with no organization context yet. Grants
    /// only the identity surface (/auth/me, onboarding actions) — every portal policy requires a
    /// different user_type, so it opens no school/parent/staff doors.
    /// </summary>
    public static string VendIdentityToken(string signingKey, string issuer, string audience,
        string identityId, string phone, int expiryMinutes = 30)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", identityId),
            new Claim("user_type", "identity"),
            new Claim("is_owner", "false"),
            new Claim("role", "identity"),
            new Claim("phone", phone)
        };

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }

    public static string VendParentToken(string signingKey, string issuer, string audience,
        string userId, string phone, int expiryMinutes = 30,
        string? identityId = null, string? contextId = null, string? schoolId = null,
        string? membershipId = null, string? organizationId = null)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", userId),
            new Claim("user_type", UserTypes.Parent),
            new Claim("is_owner", "false"),
            new Claim("role", UserTypes.Parent),
            new Claim("phone", phone)
        };

        // Org-context token (EDD-001/FE-001): every request carries WHO (identity) and WHERE (context).
        if (identityId is not null) claims.Add(new Claim("identity_id", identityId));
        if (contextId is not null) claims.Add(new Claim("context_id", contextId));
        // A parent membership is organization-scoped (EDD-002 revision): the school it belongs to lets
        // parent queries bind @SchoolId + @ParentId, the same structural guard tenant data gets.
        if (schoolId is not null) claims.Add(new Claim("school_id", schoolId));
        // Canonical identity + organization of the context (EDD-012 B2c.1). Additive.
        if (membershipId is not null) claims.Add(new Claim("membership_id", membershipId));
        if (organizationId is not null) claims.Add(new Claim("organization_id", organizationId));

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }

    /// <summary>
    /// Issues a token for a SchoolFlow internal platform admin. The <paramref name="role"/> is the
    /// sub-role (super_admin | compliance_reviewer | finance | support), which endpoints gate on.
    /// Portal: admin.schoolflow.com. Signing key: Jwt:PlatformAdminSigningKey.
    /// 15-minute access token per Cross-Cutting Auth §X.1.
    /// </summary>
    public static string VendPlatformAdminToken(string signingKey, string issuer, string audience,
        string adminId, string role, string email, int expiryMinutes = 15)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("user_id", adminId),
            new Claim("user_type", UserTypes.PlatformAdmin),
            new Claim("is_owner", "false"),
            new Claim("role", role),
            new Claim("email", email)
        };

        return VendToken(signingKey, issuer, audience, claims, expiryMinutes);
    }
}
