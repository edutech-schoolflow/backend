namespace EduTech.Shared.Events;

/// <summary>
/// StudentEnrolled (EDD-011/014) — the handoff from Admissions to Students. Admissions raises it when
/// an accepted offer becomes a real place; the Students module consumes it to create the Student. This
/// is the ONLY bridge between the two contexts (Admissions owns prospective learners, Students owns
/// enrolled learners), so it carries everything Students needs — no reach-back into Admissions tables.
///
/// A cross-module contract event, so it lives in the shared Event Catalog (like GuardianLinkedEvent),
/// referenced by both the publisher and the consumer.
/// </summary>
public sealed class StudentEnrolled : DomainEvent, IAuditableEvent
{
    public StudentEnrolled(Guid enrollmentId, Guid applicationId, Guid schoolId, Guid? childProfileId,
        string studentName, DateOnly? dateOfBirth, string? gender, string guardianPhone, Guid? classId,
        string? academicYear)
    {
        EnrollmentId = enrollmentId;
        ApplicationId = applicationId;
        SchoolId = schoolId;
        ChildProfileId = childProfileId;
        StudentName = studentName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        GuardianPhone = guardianPhone;
        ClassId = classId;
        AcademicYear = academicYear;
    }

    public Guid EnrollmentId { get; }
    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public Guid? ChildProfileId { get; }
    public string StudentName { get; }
    public DateOnly? DateOfBirth { get; }
    public string? Gender { get; }
    public string GuardianPhone { get; }
    public Guid? ClassId { get; }
    public string? AcademicYear { get; }

    public string Action => "admissions.student.enrolled";
    public string EntityType => "enrollment";
    public Guid EntityId => EnrollmentId;
    public string Summary => $"{StudentName} enrolled.";
    public string? Metadata => null;
}
