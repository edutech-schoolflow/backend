namespace EduTech.Shared.Authorization;

/// <summary>
/// One capability in the authorization model (EDD-006). A capability is the canonical unit of
/// permission — a dotted <c>resource.action</c> key (e.g. <c>attendance.record</c>). Roles are
/// collections of capabilities; endpoints gate on them via <c>[RequireCapability]</c>.
///
/// <para><b>The legacy flag is a property of the capability, not a peer concept.</b> During the
/// strangler migration the token still carries the 13 <c>can_*</c> feature flags, so a capability
/// records which flag it currently projects to via <see cref="LegacyFlag"/>. When Sprint C slims
/// the JWT, that field is nulled and the capability is otherwise unaffected — flags have no
/// independent existence.</para>
/// </summary>
public sealed record CapabilityDefinition(
    string Key,           // "attendance.record" — canonical id (dotted resource.action)
    string DisplayName,   // "Record student attendance"
    string Description,
    string Module,        // "Attendance" — the owning bounded context
    string? LegacyFlag);  // StaffFeatureFlags.MarkStudentAttendance — null once the flag is retired
