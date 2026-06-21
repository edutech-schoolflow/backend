namespace EduTech.Shared.Constants;

/// <summary>Platform Admin sub-roles (least-privilege). Endpoints gate on these (spec §4).</summary>
public static class PlatformAdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string ComplianceReviewer = "compliance_reviewer";
    public const string Finance = "finance";
    public const string Support = "support";

    public static bool IsValid(string? role) =>
        role is SuperAdmin or ComplianceReviewer or Finance or Support;

    /// <summary>Can review/approve KYC: compliance reviewers and super admins.</summary>
    public static bool CanReviewKyc(string? role) => role is SuperAdmin or ComplianceReviewer;
}
