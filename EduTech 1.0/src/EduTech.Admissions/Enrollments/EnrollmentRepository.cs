using EduTech.Admissions.Domain;
using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Admissions.Enrollments;

internal interface IEnrollmentRepository
{
    Task<Guid> EnrollAsync(Guid applicationId, Guid? offerId, Guid? childProfileId, CancellationToken cancellationToken);
    Task<Enrollment?> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task SaveAsync(Enrollment enrollment, CancellationToken cancellationToken);
}

internal sealed class EnrollmentRow
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid? OfferId { get; init; }
    public Guid? ChildProfileId { get; init; }
    public string Status { get; init; } = "active";
    public string? CancelledReason { get; init; }
    public DateTime EnrolledAt { get; init; }
}

internal sealed class EnrollmentRepository : TenantRepository, IEnrollmentRepository
{
    private const string Columns =
        "id AS Id, application_id AS ApplicationId, offer_id AS OfferId, child_profile_id AS ChildProfileId, " +
        "status, cancelled_reason AS CancelledReason, enrolled_at AS EnrolledAt";

    public EnrollmentRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
    }

    public Task<Guid> EnrollAsync(Guid applicationId, Guid? offerId, Guid? childProfileId, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<Guid>(
            """
            INSERT INTO enrollments (application_id, offer_id, school_id, child_profile_id, status)
            SELECT a.id, @OfferId, @SchoolId, @ChildProfileId, 'active'
            FROM admission_applications a
            WHERE a.id = @ApplicationId AND a.school_id = @SchoolId
            RETURNING id
            """,
            TenantParameters(new { ApplicationId = applicationId, OfferId = offerId, ChildProfileId = childProfileId }),
            cancellationToken);
    }

    public async Task<Enrollment?> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        EnrollmentRow? row = await QuerySingleOrDefaultAsync<EnrollmentRow>(
            $"SELECT {Columns} FROM enrollments WHERE application_id = @ApplicationId AND school_id = @SchoolId",
            TenantParameters(new { ApplicationId = applicationId }), cancellationToken);
        return row is null ? null : Rehydrate(row);
    }

    public Task SaveAsync(Enrollment enrollment, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE enrollments SET status = @Status, cancelled_reason = @CancelledReason, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = enrollment.Id, Status = ToDb(enrollment.Status), CancelledReason = enrollment.CancelledReason }),
            cancellationToken);
    }

    private static string ToDb(EnrollmentStatus status) =>
        status == EnrollmentStatus.Cancelled ? "cancelled" : "active";

    private static Enrollment Rehydrate(EnrollmentRow r) => new(
        r.Id, r.ApplicationId, r.OfferId, r.ChildProfileId,
        r.Status == "cancelled" ? EnrollmentStatus.Cancelled : EnrollmentStatus.Active,
        r.CancelledReason, r.EnrolledAt);
}
