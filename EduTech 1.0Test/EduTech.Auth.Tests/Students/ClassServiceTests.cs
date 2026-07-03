using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Classes;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class ClassServiceTests
{
    private readonly Mock<IClassRepository> _repo = new();

    private ClassService CreateSut() => new(_repo.Object);

    [Fact]
    public async Task CreateClass_MissingLevel_Throws400()
    {
        // Level omitted → null → service rejects (the enum makes an *invalid* value unrepresentable here).
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateClassAsync(
            new CreateClassRequest { Name = "JSS 1", Arms = new() { "A" } }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateClass_NoArms_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateClassAsync(
            new CreateClassRequest { Name = "JSS 1", Level = ClassLevel.JuniorSecondary, Arms = new() }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateClass_Valid_CreatesWithDistinctUppercasedArms()
    {
        Guid classId = Guid.NewGuid();
        _repo.Setup(r => r.CreateClassWithArmsAsync("JSS 1", ClassLevel.JuniorSecondary, 1,
                It.IsAny<IReadOnlyList<(string, Guid?)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(classId);

        SchoolClassResponse res = await CreateSut().CreateClassAsync(
            new CreateClassRequest { Name = " JSS 1 ", Level = ClassLevel.JuniorSecondary, Order = 1, Arms = new() { "a", "A", "b" } },
            CancellationToken.None);

        Assert.Equal(classId, res.Id);
        Assert.Equal(ClassLevel.JuniorSecondary, res.Level);
        Assert.Equal(2, res.ArmsCount);   // a/A deduped → A, B
        _repo.Verify(r => r.CreateClassWithArmsAsync("JSS 1", ClassLevel.JuniorSecondary, 1,
            It.Is<IReadOnlyList<(string Arm, Guid? T)>>(l => l.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClass_TeacherPerArmNotActive_Throws400()
    {
        Guid affiliation = Guid.NewGuid();
        _repo.Setup(r => r.AffiliationActiveAsync(affiliation, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateClassAsync(
            new CreateClassRequest
            {
                Name = "JSS 1", Level = ClassLevel.JuniorSecondary, Arms = new() { "A" },
                TeacherPerArm = new() { ["A"] = affiliation }
            },
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        _repo.Verify(r => r.CreateClassWithArmsAsync(It.IsAny<string>(), It.IsAny<ClassLevel>(), It.IsAny<int>(),
            It.IsAny<IReadOnlyList<(string, Guid?)>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteClass_NotFound_Throws404()
    {
        _repo.Setup(r => r.ClassExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().DeleteClassAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListArms_AttachesSubjectTeachersToTheRightArm()
    {
        Guid classId = Guid.NewGuid();
        Guid armA = Guid.NewGuid();
        Guid armB = Guid.NewGuid();
        _repo.Setup(r => r.ClassExistsAsync(classId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ListArmsAsync(classId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClassArmRow>
        {
            new ClassArmRow { Id = armA, ClassId = classId, ClassName = "JSS 1", Arm = "A", StudentsCount = 3 },
            new ClassArmRow { Id = armB, ClassId = classId, ClassName = "JSS 1", Arm = "B", StudentsCount = 0 }
        });
        _repo.Setup(r => r.ListSubjectTeachersForClassAsync(classId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SubjectTeacherRow>
            {
                new SubjectTeacherRow { Id = Guid.NewGuid(), ClassArmId = armA, Subject = "Maths", TeacherName = "Mr T" }
            });

        IReadOnlyList<ClassArmResponse> arms = await CreateSut().ListArmsAsync(classId, CancellationToken.None);

        Assert.Equal("JSS 1A", arms[0].FullName);
        Assert.Single(arms[0].SubjectTeachers);
        Assert.Equal("Maths", arms[0].SubjectTeachers[0].Subject);
        Assert.Empty(arms[1].SubjectTeachers);
    }

    [Fact]
    public async Task AddSubjectTeacher_ArmMissing_Throws404()
    {
        _repo.Setup(r => r.ArmExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().AddSubjectTeacherAsync(
            Guid.NewGuid(), new AddSubjectTeacherRequest { TeacherAffiliationId = Guid.NewGuid(), Subject = "Maths" },
            CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }
}
