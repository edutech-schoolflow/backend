using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Identity.Domain;

public enum IdentityStatus
{
    Pending,   // exists (possibly pre-created by a school) but not yet claimed/verified
    Active,
    Suspended
}

/// <summary>
/// The Identity aggregate (EDD-001/EDD-003): the global account for one person, keyed by phone.
/// Owns authentication state and enforces its invariants in-aggregate (house style — intent methods
/// that throw). It never references a school: relationships live in Membership/Employment.
///
/// Persistence note: the CLAIM invariant is additionally guarded at the database
/// (<c>UPDATE ... WHERE password_hash IS NULL</c>) so a concurrent claim can never overwrite a real
/// account — same discipline the parent claim-on-register flow proved.
/// </summary>
internal sealed class Identity
{
    public const int MaxFailedLogins = 5;
    public const int LockoutMinutes = 15;

    public Identity(Guid id, string firstName, string? middleName, string lastName, string phone,
        string? email, string? passwordHash, bool phoneVerified, bool emailVerified,
        IdentityStatus status, int failedLoginCount, DateTime? lockedUntil)
    {
        Id = id;
        FirstName = firstName;
        MiddleName = middleName;
        LastName = lastName;
        Phone = phone;
        Email = email;
        PasswordHash = passwordHash;
        PhoneVerified = phoneVerified;
        EmailVerified = emailVerified;
        Status = status;
        FailedLoginCount = failedLoginCount;
        LockedUntil = lockedUntil;
    }

    public Guid Id { get; }
    public string FirstName { get; private set; }
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; }
    public string Phone { get; }
    public string? Email { get; private set; }
    public string? PasswordHash { get; private set; }
    public bool PhoneVerified { get; private set; }
    public bool EmailVerified { get; private set; }
    public IdentityStatus Status { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTime? LockedUntil { get; private set; }

    /// <summary>Unclaimed = pre-created (e.g. by a school admitting a student) and never registered.</summary>
    public bool IsClaimed => PasswordHash is not null;

    // ── Claim (registration against an existing phone) ────────────────────────────────────────

    /// <summary>
    /// A phone that already has a password is a genuine duplicate — registration is rejected.
    /// A pending, password-less identity is claimable: the registrant becomes its owner.
    /// </summary>
    public void EnsureClaimable()
    {
        if (IsClaimed)
        {
            throw new AppErrorException("Registration failed. Something went wrong.", 409,
                ErrorCodes.PhoneTaken, logReason: "Identity claim blocked: phone already registered.");
        }
    }

    /// <summary>Claims the identity: the person's own name is authoritative over any placeholder.</summary>
    public void Claim(string firstName, string? middleName, string lastName, string? email, string passwordHash)
    {
        EnsureClaimable();
        FirstName = firstName;
        MiddleName = middleName;
        LastName = lastName;
        Email = email ?? Email;
        PasswordHash = passwordHash;
        // Not Active yet — activation requires phone verification (OTP).
    }

    // ── Authentication ─────────────────────────────────────────────────────────────────────────

    /// <summary>Pre-credential checks: suspended and locked accounts never reach password comparison.</summary>
    public void EnsureCanAttemptLogin(DateTime nowUtc)
    {
        if (Status == IdentityStatus.Suspended)
        {
            throw new AppErrorException("This account is suspended.", 403, ErrorCodes.AccountInactive,
                logReason: "Identity login: suspended.");
        }

        if (LockedUntil is DateTime until && until > nowUtc)
        {
            throw new AppErrorException("Account locked after failed attempts. Try again later.",
                429, ErrorCodes.AccountLocked, logReason: "Identity login: locked out.");
        }
    }

    /// <summary>A wrong password (or unclaimed account) counts toward lockout: 5 strikes → 15 minutes.</summary>
    public void RecordFailedLogin(DateTime nowUtc)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockedUntil = nowUtc.AddMinutes(LockoutMinutes);
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    /// <summary>Login requires a verified phone — OTP gates access, which is what makes claiming safe.</summary>
    public void EnsureLoginComplete()
    {
        if (!PhoneVerified)
        {
            throw new AppErrorException("Please verify your phone number before logging in.",
                403, ErrorCodes.PhoneNotVerified, logReason: "Identity login: phone not verified.");
        }
    }

    // ── Verification / lifecycle ───────────────────────────────────────────────────────────────

    /// <summary>Phone verification activates a claimed identity (pending → active).</summary>
    public void VerifyPhone()
    {
        PhoneVerified = true;
        if (IsClaimed && Status == IdentityStatus.Pending)
        {
            Status = IdentityStatus.Active;
        }
    }

    public void VerifyEmail() => EmailVerified = true;

    public void Suspend() => Status = IdentityStatus.Suspended;

    /// <summary>
    /// An identity with active relationships must not be deleted — memberships/employments reference
    /// it. (Cross-aggregate: the caller supplies the count; the rule lives here.)
    /// </summary>
    public void EnsureDeletable(int activeRelationships)
    {
        if (activeRelationships > 0)
        {
            throw new AppErrorException(
                "This account still belongs to or works for an organization and can't be deleted.",
                409, ErrorCodes.Conflict);
        }
    }
}
