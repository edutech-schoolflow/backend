using EduTech.Shared.Audit;
using EduTech.Shared.Constants;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Students.Students;
using EduTech.Students.Students.Commands;
using Moq;

namespace EduTech.Auth.Tests.Students;

public class StudentServiceTests
{
    private readonly Mock<IStudentRepository> _repo = new();
    private readonly Mock<IDomainEventPublisher> _events = new();
    private readonly Mock<IAuditLogRepository> _audit = new();

    private StudentService CreateSut() =>
        new(_repo.Object, new StudentCommandInvoker(_events.Object), _events.Object, _audit.Object);

    private static readonly Guid Cls = Guid.NewGuid();

    private static CreateStudentRequest ValidRequest() => new CreateStudentRequest
    {
        FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male,
        DateOfBirth = new DateOnly(2015, 4, 12), ClassId = Cls,
        Parent = new ParentLinkRequest { Phone = "+2348030000001", FirstName = "Mr", LastName = "Adebayo" }
    };

    private static StudentRow Row(Guid id, string status = "active") => new StudentRow
    {
        Id = id, ChildProfileId = Guid.NewGuid(), FirstName = "Tolu", LastName = "Adebayo",
        DateOfBirth = new DateOnly(2015, 4, 12), Gender = "male", AdmissionNumber = "GFA/2025/001",
        ClassId = Cls, Status = status, CreatedAt = DateTime.UtcNow
    };

    private void ClassExists() =>
        _repo.Setup(r => r.ClassExistsAsync(Cls, It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
    public async Task Create_InvalidClass_Throws400()
    {
        _repo.Setup(r => r.ClassExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12), ClassId = Cls },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ArmNotInClass_Throws400()
    {
        Guid arm = Guid.NewGuid();
        ClassExists();
        _repo.Setup(r => r.ArmInClassAsync(arm, Cls, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(() => CreateSut().CreateAsync(
            new CreateStudentRequest { FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12), ClassId = Cls, ClassArmId = arm },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_ReturnsAdmissionNumberAndActive()
    {
        Guid id = Guid.NewGuid();
        ClassExists();
        _repo.Setup(r => r.CreateAsync(It.IsAny<StudentInsert>(), It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((id, "GFA/2025/001"));
        _repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Row(id));
        _repo.Setup(r => r.GetGuardiansAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GuardianRow>());

        StudentResponse res = await CreateSut().CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(id, res.Id);
        Assert.Equal("GFA/2025/001", res.AdmissionNumber);
        Assert.Equal(StudentStatus.Active, res.Status);
        Assert.Equal(Gender.Male, res.Gender);
        // the new student is enrolled into the class and linked to a parent by phone (the primary guardian)
        _repo.Verify(r => r.CreateAsync(
            It.Is<StudentInsert>(i => i.Parent.Phone == "+2348030000001" && i.ClassId == Cls),
            It.IsAny<IReadOnlyList<GuardianDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingParentPhone_Throws400()
    {
        ClassExists();
        CreateStudentRequest req = new CreateStudentRequest
        {
            FirstName = "Tolu", LastName = "Adebayo", Gender = Gender.Male, DateOfBirth = new DateOnly(2015, 4, 12),
            ClassId = Cls
            // no Parent.Phone
        };

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().CreateAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_GuardianMissingPhone_Throws400()
    {
        ClassExists();
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
        _repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Row(id, "active"));
        _repo.Setup(r => r.SetStatusIfAsync(id, StudentStatus.Active, StudentStatus.Withdrawn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().WithdrawAsync(id, CancellationToken.None);

        _repo.Verify(r => r.SetStatusIfAsync(id, StudentStatus.Active, StudentStatus.Withdrawn, It.IsAny<CancellationToken>()),
            Times.Once);
        // the command publishes a lifecycle event (which the audit observer records)
        _events.Verify(p => p.PublishAsync(It.IsAny<StudentLifecycleEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Withdraw_NotFound_Throws404()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((StudentRow?)null);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().WithdrawAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Withdraw_AlreadyWithdrawn_Throws409()
    {
        // The conditional update changes nothing (student isn't active), so the command reports a conflict.
        Guid id = Guid.NewGuid();
        _repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Row(id, "withdrawn"));
        _repo.Setup(r => r.SetStatusIfAsync(id, StudentStatus.Active, StudentStatus.Withdrawn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().WithdrawAsync(id, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Transfer_TargetArmMissing_Throws400()
    {
        Guid id = Guid.NewGuid();
        Guid arm = Guid.NewGuid();
        _repo.Setup(r => r.ClassArmExistsAsync(arm, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().TransferAsync(id, new TransferStudentRequest { ClassArmId = arm }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    // ── parent lookup (Add-Student modal: link vs create) ────────────────────────

    [Fact]
    public async Task LookupParent_InvalidPhone_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().LookupParentAsync("not-a-phone", CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task LookupParent_UnknownPhone_ReturnsNotFound()
    {
        _repo.Setup(r => r.LookupParentByPhoneAsync("+2348030000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParentLookupRow?)null);

        ParentLookupResponse res = await CreateSut().LookupParentAsync("08030000001", CancellationToken.None);

        Assert.False(res.Found);
        Assert.Null(res.Name);
        Assert.Null(res.Status);
    }

    [Fact]
    public async Task LookupParent_RegisteredParent_ReturnsRegisteredWithFullName()
    {
        _repo.Setup(r => r.LookupParentByPhoneAsync("+2348030000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentLookupRow { FirstName = "Ada", MiddleName = "N", LastName = "Obi", HasPassword = true });

        ParentLookupResponse res = await CreateSut().LookupParentAsync("08030000001", CancellationToken.None);

        Assert.True(res.Found);
        Assert.Equal("Ada N Obi", res.Name);
        Assert.Equal("registered", res.Status);
    }

    [Fact]
    public async Task LookupParent_PendingSchoolSeededParent_ReturnsPending()
    {
        _repo.Setup(r => r.LookupParentByPhoneAsync("+2348030000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentLookupRow { FirstName = "Guardian", LastName = "Guardian", HasPassword = false });

        ParentLookupResponse res = await CreateSut().LookupParentAsync("08030000001", CancellationToken.None);

        Assert.True(res.Found);
        Assert.Equal("pending", res.Status);
    }
}
