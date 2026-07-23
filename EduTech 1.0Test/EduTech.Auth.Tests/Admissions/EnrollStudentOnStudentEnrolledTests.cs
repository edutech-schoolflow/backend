using EduTech.Shared.Events;
using EduTech.Students.Students;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Admissions → Students handoff (EDD-014 Slice 9). The Students module consumes StudentEnrolled and
/// creates a Student via the same CreateAsync machinery as manual enrolment — the only bridge between
/// the two contexts. A Student needs a class + date of birth + gender; when any is missing the handoff
/// is skipped (data completed later) rather than creating a half-formed record. Verifies the seam's
/// decision without a database (the CreateAsync path itself is covered by StudentServiceTests).
/// </summary>
public class EnrollStudentOnStudentEnrolledTests
{
    private static readonly Guid ClassId = Guid.NewGuid();

    private readonly Mock<IStudentRepository> _students = new();

    public EnrollStudentOnStudentEnrolledTests() =>
        _students.Setup(r => r.ClassExistsAsync(ClassId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private EnrollStudentOnStudentEnrolled Sut() =>
        new(_students.Object, NullLogger<EnrollStudentOnStudentEnrolled>.Instance);

    private static StudentEnrolled Event(Guid? classId = null, DateOnly? dob = null,
        string? gender = "male", string phone = "+2348030000001", string name = "Ada Grace Obi") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), childProfileId: null, name,
            dob ?? new DateOnly(2015, 4, 3), gender, phone, classId ?? ClassId, "2026/2027");

    [Fact]
    public async Task Enrolled_WithClassDobGender_CreatesStudent()
    {
        _students.Setup(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((Guid.NewGuid(), "SCH/2026/001"));

        await Sut().HandleAsync(Event(), CancellationToken.None);

        _students.Verify(r => r.CreateAsync(
            It.Is<StudentInsert>(s => s.FirstName == "Ada" && s.MiddleName == "Grace" && s.LastName == "Obi"
                && s.ClassId == ClassId && s.Parent.Phone == "+2348030000001"),
            It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoClass_SkipsCreation()
    {
        // A prospective learner with no offered class isn't yet a Student.
        await Sut().HandleAsync(new StudentEnrolled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "Ada Obi", new DateOnly(2015, 4, 3), "male", "+2348030000001", classId: null, "2026/2027"),
            CancellationToken.None);

        _students.Verify(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoDateOfBirth_SkipsCreation()
    {
        await Sut().HandleAsync(new StudentEnrolled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "Ada Obi", dateOfBirth: null, "male", "+2348030000001", ClassId, "2026/2027"),
            CancellationToken.None);

        _students.Verify(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownClass_SkipsCreation()
    {
        await Sut().HandleAsync(Event(classId: Guid.NewGuid()), CancellationToken.None);

        _students.Verify(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unspecified")]
    public async Task MissingOrUnknownGender_SkipsCreation(string? gender)
    {
        await Sut().HandleAsync(Event(gender: gender), CancellationToken.None);

        _students.Verify(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidGuardianPhone_SkipsCreation()
    {
        await Sut().HandleAsync(Event(phone: "not-a-phone"), CancellationToken.None);

        _students.Verify(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SingleWordName_UsesItForFirstAndLast()
    {
        _students.Setup(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((Guid.NewGuid(), "SCH/2026/002"));

        await Sut().HandleAsync(Event(name: "Ada"), CancellationToken.None);

        _students.Verify(r => r.CreateAsync(
            It.Is<StudentInsert>(s => s.FirstName == "Ada" && s.MiddleName == null && s.LastName == "Ada"),
            It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
