using EduTech.Identity.Domain;
using EduTech.Shared.Exceptions;
using IdentityAggregate = EduTech.Identity.Domain.Identity;

namespace EduTech.Auth.Tests.Identity;

/// <summary>
/// Pure domain tests for the Identity aggregate (EDD-001/003) — no database, no mocks.
/// </summary>
public class IdentityAggregateTests
{
    private static IdentityAggregate Make(string? passwordHash = null, bool phoneVerified = false,
        IdentityStatus status = IdentityStatus.Pending, int failed = 0, DateTime? lockedUntil = null)
        => new IdentityAggregate(Guid.NewGuid(), "Ada", null, "Obi", "+2348030000001", null,
            passwordHash, phoneVerified, false, status, failed, lockedUntil);

    private static readonly DateTime Now = new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc);

    // ── claim ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Claim_PendingUnclaimed_AdoptsNameAndPassword()
    {
        IdentityAggregate identity = Make(); // school-seeded placeholder, no password

        identity.Claim("Bola", "T", "Ade", "bola@x.com", "hashed");

        Assert.True(identity.IsClaimed);
        Assert.Equal("Bola", identity.FirstName);
        Assert.Equal("Ade", identity.LastName);
        Assert.Equal(IdentityStatus.Pending, identity.Status); // active only after phone verification
    }

    [Fact]
    public void Claim_AlreadyClaimed_Throws409()
    {
        IdentityAggregate identity = Make(passwordHash: "existing");

        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => identity.Claim("X", null, "Y", null, "new"));
        Assert.Equal(409, ex.StatusCode);
    }

    // ── login guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureCanAttemptLogin_Suspended_Throws403()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Make(status: IdentityStatus.Suspended).EnsureCanAttemptLogin(Now));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void EnsureCanAttemptLogin_Locked_Throws429_AndExpiredLockPasses()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Make(lockedUntil: Now.AddMinutes(5)).EnsureCanAttemptLogin(Now));
        Assert.Equal(429, ex.StatusCode);

        Make(lockedUntil: Now.AddMinutes(-1)).EnsureCanAttemptLogin(Now); // expired lock — no throw
    }

    [Fact]
    public void RecordFailedLogin_FifthStrike_LocksFor15Minutes()
    {
        IdentityAggregate identity = Make(passwordHash: "h", failed: 4);

        identity.RecordFailedLogin(Now);

        Assert.Equal(Now.AddMinutes(IdentityAggregate.LockoutMinutes), identity.LockedUntil);
        Assert.Equal(0, identity.FailedLoginCount); // counter resets with the lock
    }

    [Fact]
    public void RecordSuccessfulLogin_ClearsFailuresAndLock()
    {
        IdentityAggregate identity = Make(passwordHash: "h", failed: 3, lockedUntil: Now.AddMinutes(-1));

        identity.RecordSuccessfulLogin();

        Assert.Equal(0, identity.FailedLoginCount);
        Assert.Null(identity.LockedUntil);
    }

    [Fact]
    public void EnsureLoginComplete_UnverifiedPhone_Throws403()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Make(passwordHash: "h").EnsureLoginComplete());
        Assert.Equal(403, ex.StatusCode);
    }

    // ── verification / lifecycle ───────────────────────────────────────────────────

    [Fact]
    public void VerifyPhone_ClaimedIdentity_Activates()
    {
        IdentityAggregate identity = Make(passwordHash: "h");

        identity.VerifyPhone();

        Assert.True(identity.PhoneVerified);
        Assert.Equal(IdentityStatus.Active, identity.Status);
    }

    [Fact]
    public void VerifyPhone_UnclaimedIdentity_StaysPending()
    {
        IdentityAggregate identity = Make(); // no password yet

        identity.VerifyPhone();

        Assert.True(identity.PhoneVerified);
        Assert.Equal(IdentityStatus.Pending, identity.Status); // claim still required
    }

    [Fact]
    public void EnsureDeletable_WithActiveRelationships_Throws409()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(
            () => Make(passwordHash: "h").EnsureDeletable(activeRelationships: 2));
        Assert.Equal(409, ex.StatusCode);

        Make().EnsureDeletable(activeRelationships: 0); // no throw
    }
}
