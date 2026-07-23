using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Shared.Phone;
using Microsoft.Extensions.Logging;

namespace EduTech.Students.Students;

/// <summary>
/// The Admissions → Students handoff (EDD-014 Slice 9 — the finale). Students consumes the platform's
/// <see cref="StudentEnrolled"/> event and creates a real Student from it, reusing the same
/// <see cref="IStudentRepository.CreateAsync"/> machinery the manual enrolment uses (parent + child
/// profile + admission number, one transaction). This is the ONLY coupling between the two contexts:
/// Admissions owns prospective learners, Students owns enrolled learners, and the event is the bridge —
/// Students never reaches back into Admissions tables, Admissions never writes a Student.
///
/// A Student needs a class, a date of birth and a gender. The event carries them from the accepted
/// offer + application, but the current Admissions model leaves them optional, so when any is missing
/// the handoff is logged and skipped rather than forced — the record is completed via the normal
/// student flow. (Surfaced platform seam: for fully-automatic creation, StudentEnrolled should
/// *guarantee* class/DOB/gender — a tightening of the Admissions offer/application, tracked separately.)
///
/// Runs synchronously in the enrolling request's scope, so the school (tenant) context is the enrolling
/// school; the publisher isolates handler failures, so a hiccup here never rolls back the enrolment.
/// </summary>
internal sealed class EnrollStudentOnStudentEnrolled : IDomainEventHandler<StudentEnrolled>
{
    private readonly IStudentRepository _students;
    private readonly ILogger<EnrollStudentOnStudentEnrolled> _logger;

    public EnrollStudentOnStudentEnrolled(IStudentRepository students,
        ILogger<EnrollStudentOnStudentEnrolled> logger)
    {
        _students = students;
        _logger = logger;
    }

    public async Task HandleAsync(StudentEnrolled domainEvent, CancellationToken cancellationToken)
    {
        // A Student is placed into a class — no class, no student record yet.
        if (domainEvent.ClassId is not Guid classId
            || !await _students.ClassExistsAsync(classId, cancellationToken))
        {
            _logger.LogWarning("StudentEnrolled {ApplicationId}: no known class on the offer; " +
                "deferring student creation to data completion.", domainEvent.ApplicationId);
            return;
        }

        if (domainEvent.DateOfBirth is not DateOnly dateOfBirth
            || !Enum.TryParse(domainEvent.Gender, ignoreCase: true, out Gender gender)
            || !Enum.IsDefined(gender))
        {
            _logger.LogWarning("StudentEnrolled {ApplicationId}: date of birth / gender missing; " +
                "deferring student creation to data completion.", domainEvent.ApplicationId);
            return;
        }

        string? phone = PhoneNumber.Normalize(domainEvent.GuardianPhone);
        if (phone is null)
        {
            _logger.LogWarning("StudentEnrolled {ApplicationId}: no valid guardian phone; " +
                "deferring student creation to data completion.", domainEvent.ApplicationId);
            return;
        }

        (string firstName, string? middleName, string lastName) = SplitName(domainEvent.StudentName);

        StudentInsert insert = new()
        {
            FirstName = firstName,
            MiddleName = middleName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            ClassId = classId,
            Parent = new ParentLink { Phone = phone }
        };

        (Guid studentId, string admissionNumber) =
            await _students.CreateAsync(insert, Array.Empty<GuardianDto>(), cancellationToken);

        _logger.LogInformation("StudentEnrolled {ApplicationId} → student {StudentId} ({AdmissionNumber}).",
            domainEvent.ApplicationId, studentId, admissionNumber);
    }

    // The applicant carries a single name; split it into first / middle / last so the child profile
    // reads naturally. One token → first == last (a lone name is better than a blank surname).
    private static (string First, string? Middle, string Last) SplitName(string name)
    {
        string[] parts = (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => ("Unknown", null, "Unknown"),
            1 => (parts[0], null, parts[0]),
            2 => (parts[0], null, parts[1]),
            _ => (parts[0], string.Join(' ', parts[1..^1]), parts[^1])
        };
    }
}
