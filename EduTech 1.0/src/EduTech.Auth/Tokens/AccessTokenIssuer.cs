using EduTech.Shared.Auth;
using Microsoft.Extensions.Configuration;

namespace EduTech.Auth.Tokens;

/// <summary>
/// Config-aware wrapper over <see cref="TokenVendor"/>: reads the JWT signing keys / issuer /
/// audience once and mints access tokens with the correct per-portal expiry.
/// </summary>
internal sealed class AccessTokenIssuer : IAccessTokenIssuer
{
    // Per Cross-Cutting Auth §X.1.
    private const int SchoolOwnerAccessMinutes = 30;
    private const int StaffAccessMinutes = 30;
    private const int ParentAccessMinutes = 30;
    private const int PlatformAdminAccessMinutes = 15;

    private readonly string _schoolSigningKey;
    private readonly string _staffSigningKey;
    private readonly string _parentSigningKey;
    private readonly string _identitySigningKey;
    private readonly string _platformAdminSigningKey;
    private readonly string _issuer;
    private readonly string _audience;

    public AccessTokenIssuer(IConfiguration configuration)
    {
        _schoolSigningKey = configuration["Jwt:SchoolSigningKey"]
            ?? throw new InvalidOperationException("Jwt:SchoolSigningKey is missing");
        _staffSigningKey = configuration["Jwt:StaffSigningKey"]
            ?? throw new InvalidOperationException("Jwt:StaffSigningKey is missing");
        _parentSigningKey = configuration["Jwt:ParentSigningKey"]
            ?? throw new InvalidOperationException("Jwt:ParentSigningKey is missing");
        _identitySigningKey = configuration["Jwt:IdentitySigningKey"] ?? _parentSigningKey;
        _platformAdminSigningKey = configuration["Jwt:PlatformAdminSigningKey"]
            ?? throw new InvalidOperationException("Jwt:PlatformAdminSigningKey is missing");
        _issuer = configuration["Jwt:Issuer"] ?? "EduTech";
        _audience = configuration["Jwt:Audience"] ?? "EduTechApp";
    }

    public AccessToken IssueSchoolOwner(Guid ownerId, Guid schoolId, string phone,
        string schoolStatus, string kycStatus, string? subdomain,
        Guid? identityId = null, Guid? contextId = null,
        Guid? membershipId = null, Guid? organizationId = null)
    {
        string token = TokenVendor.VendSchoolOwnerToken(
            _schoolSigningKey, _issuer, _audience,
            ownerId.ToString(), schoolId.ToString(), phone, schoolStatus, kycStatus, subdomain,
            SchoolOwnerAccessMinutes, identityId?.ToString(), contextId?.ToString(),
            membershipId?.ToString(), organizationId?.ToString());

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(SchoolOwnerAccessMinutes)
        };
    }

    public AccessToken IssueStaffIdentity(Guid staffUserId, string phone, string kycStatus)
    {
        string token = TokenVendor.VendStaffIdentityToken(
            _staffSigningKey, _issuer, _audience,
            staffUserId.ToString(), phone, kycStatus, StaffAccessMinutes);

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(StaffAccessMinutes)
        };
    }

    public AccessToken IssueStaffScoped(Guid staffUserId, Guid schoolId, Guid affiliationId, string phone,
        string role, string employmentType, string kycStatus, IReadOnlyDictionary<string, bool> features,
        Guid? identityId = null, Guid? contextId = null,
        Guid? membershipId = null, Guid? organizationId = null)
    {
        string token = TokenVendor.VendStaffScopedToken(
            _staffSigningKey, _issuer, _audience,
            staffUserId.ToString(), phone, schoolId.ToString(), affiliationId.ToString(),
            role, employmentType, kycStatus, features, StaffAccessMinutes,
            identityId?.ToString(), contextId?.ToString(),
            membershipId?.ToString(), organizationId?.ToString());

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(StaffAccessMinutes)
        };
    }

    public AccessToken IssueIdentity(Guid identityId, string phone)
    {
        // Signed with the identity key (falls back to the parent key if unconfigured); the
        // "identity" user_type keeps it out of every portal regardless of key.
        string token = TokenVendor.VendIdentityToken(
            _identitySigningKey, _issuer, _audience, identityId.ToString(), phone, ParentAccessMinutes);

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ParentAccessMinutes)
        };
    }

    public AccessToken IssueParent(Guid parentId, string phone, Guid? identityId = null, Guid? contextId = null,
        Guid? schoolId = null, Guid? membershipId = null, Guid? organizationId = null)
    {
        string token = TokenVendor.VendParentToken(
            _parentSigningKey, _issuer, _audience, parentId.ToString(), phone, ParentAccessMinutes,
            identityId?.ToString(), contextId?.ToString(), schoolId?.ToString(),
            membershipId?.ToString(), organizationId?.ToString());

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ParentAccessMinutes)
        };
    }

    public AccessToken IssuePlatformAdmin(Guid adminId, string role, string email)
    {
        string token = TokenVendor.VendPlatformAdminToken(
            _platformAdminSigningKey, _issuer, _audience,
            adminId.ToString(), role, email, PlatformAdminAccessMinutes);

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(PlatformAdminAccessMinutes)
        };
    }
}

