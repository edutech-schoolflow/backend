using EduTech.Auth.Staff;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EduTech.Auth.Tests.Staff;

/// <summary>
/// The crux of multi-school staff: FULL-TIME EXCLUSIVITY. A full-timer can hold no other affiliation,
/// and you can't add someone full-time if they already work elsewhere. These checks run before the
/// (untestable-in-unit) transaction, so we assert each one rejects with 409 / 400 up front.
/// </summary>
public class StaffInviteServiceTests
{
    private readonly Mock<IEduTechRequestContext> _context = new();
    private readonly Mock<IDbConnectionFactory> _db = new();
    private readonly Mock<IStaffUserRepository> _staffUsers = new();
    private readonly Mock<IStaffAffiliationRepository> _affiliations = new();
    private readonly Mock<IStaffInviteTokenRepository> _inviteTokens = new();
    private readonly Mock<INotificationDispatcher> _sms = new();
    private readonly Mock<IConfiguration> _config = new();

    public StaffInviteServiceTests()
    {
        // A valid school + inviter context for every test (set before the exclusivity checks run).
        _context.Setup(c => c.SchoolId).Returns(Guid.NewGuid().ToString());
        _context.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());
    }

    private StaffInviteService CreateSut()
    {
        return new StaffInviteService(
            _context.Object, _db.Object, _staffUsers.Object, _affiliations.Object,
            _inviteTokens.Object, _sms.Object, _config.Object);
    }

    private static InviteStaffRequest Request(string employmentType = EmploymentTypes.PartTime,
        string role = StaffRoles.Teacher)
    {
        return new InviteStaffRequest
        {
            FirstName = "Amaka",
            LastName = "Okonkwo",
            Phone = "08055667788",
            Role = role,
            EmploymentType = employmentType
        };
    }

    [Fact]
    public async Task Invite_PersonIsFullTimeElsewhere_Throws409()
    {
        Guid existing = Guid.NewGuid();
        _staffUsers.Setup(s => s.GetIdByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _affiliations.Setup(a => a.HasActiveFullTimeAsync(existing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().InviteAsync(Request(), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
    }

    [Fact]
    public async Task Invite_FullTimeWhenPersonAlreadyHasActiveAffiliation_Throws409()
    {
        Guid existing = Guid.NewGuid();
        _staffUsers.Setup(s => s.GetIdByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _affiliations.Setup(a => a.HasActiveFullTimeAsync(existing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _affiliations.Setup(a => a.HasAnyActiveAsync(existing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().InviteAsync(Request(EmploymentTypes.FullTime), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
    }

    [Fact]
    public async Task Invite_PersonAlreadyInvitedOrActiveAtThisSchool_Throws409()
    {
        Guid existing = Guid.NewGuid();
        _staffUsers.Setup(s => s.GetIdByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _affiliations.Setup(a => a.HasActiveFullTimeAsync(existing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _affiliations.Setup(a => a.HasAnyActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _affiliations.Setup(a => a.GetAsync(existing, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffAffiliationRow { Id = Guid.NewGuid(), Status = "active" });

        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().InviteAsync(Request(), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Invite_InvalidRole_Throws400BeforeAnyLookup()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().InviteAsync(Request(role: "wizard"), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
        _staffUsers.Verify(s => s.GetIdByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
