using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Students.Students;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class StudentServiceTests
{
    private readonly Mock<IStudentRepository> _repo = new();

    private StudentService CreateSut() => new(_repo.Object);

    private static CreateStudentRequest ValidRequest() => new CreateStudentRequest
    {
        FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male,
        DateOfBirth = new DateOnly(2015, 4, 12),
        Parent = new ParentLinkRequest { Phone = "+2348030000001", FirstName = "Mr", LastName = "Adebayo" }
    };

    [Fact]
    public async Task Create_MissingName_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12) },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_MissingGender_Throws400()
    {
        // Gender omitted → null → service rejects (an invalid gender is unrepresentable with the enum).
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "Tolu", LastName = "Adebayo", DateOfBirth = new DateOnly(2015, 4, 12) },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_FutureDob_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ClassArmMissing_Throws400()
    {
        Guid arm = Guid.NewGuid();
        _repo.Setup(r => r.ClassArmExistsAsync(arm, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12), ClassArmId = arm },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_ReturnsAdmissionNumberAndActive()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((id, "GFA/2025/001"));
        _repo.Setup(r => r.GetGuardiansAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GuardianRow>());

        StudentResponse res = await CreateSut().CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(id, res.Id);
        Assert.Equal("GFA/2025/001", res.AdmissionNumber);
        Assert.Equal(StudentStatus.Active, res.Status);
        Assert.Equal(Gender.Male, res.Gender);
        // the new student is linked to a parent by phone (the primary guardian)
        _repo.Verify(r => r.CreateAsync(
            It.Is<StudentInsert>(i => i.Parent.Phone == "+2348030000001"),
            It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingParentPhone_Throws400()
    {
        CreateStudentRequest req = new CreateStudentRequest
        {
            FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12)
            // no Parent.Phone
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_GuardianMissingPhone_Throws400()
    {
        CreateStudentRequest req = ValidRequest();
        req.Guardians.Add(new GuardianDto { Name = "Mama", Phone = "", Relationship = "mother" });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Get_NotFound_Throws404()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((StudentRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().GetAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Withdraw_Active_TransitionsToWithdrawn()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.GetStatusAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("active");
        _repo.Setup(r => r.SetStatusIfAsync(id, StudentStatus.Active, StudentStatus.Withdrawn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().WithdrawAsync(id, CancellationToken.None);

        _repo.Verify(r => r.SetStatusIfAsync(id, StudentStatus.Active, StudentStatus.Withdrawn, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Withdraw_NotFound_Throws404()
    {
        _repo.Setup(r => r.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().WithdrawAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Withdraw_AlreadyWithdrawn_IsNoOp()
    {
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.GetStatusAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("withdrawn");

        await CreateSut().WithdrawAsync(id, CancellationToken.None);

        _repo.Verify(r => r.SetStatusIfAsync(It.IsAny<Guid>(), It.IsAny<StudentStatus>(), It.IsAny<StudentStatus>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Transfer_TargetArmMissing_Throws400()
    {
        Guid id = Guid.NewGuid();
        Guid arm = Guid.NewGuid();
        _repo.Setup(r => r.ExistsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ClassArmExistsAsync(arm, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().TransferAsync(id, new TransferStudentRequest { ClassArmId = arm }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }
}
