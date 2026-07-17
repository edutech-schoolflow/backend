using EduTech.Auth.Unified;
using EduTech.Shared.Events;
using System.Security.Cryptography;
using System.Text;
using EduTech.Auth.Otp;
using EduTech.Auth.RefreshTokens;
using EduTech.Auth.Security;
using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using EduTech.Workforce;
using EduTech.Membership;
using EduTech.Membership.Domain;
using EduTech.People;



namespace EduTech.Auth.Staff;

/// <summary>Staff-side invite acceptance (Actor 2, Path B step 2).</summary>
public interface IStaffInviteAcceptService
{
    /// <summary>Returns invite details for the welcome screen (school, role, expiry, has-account).</summary>
    Task<InviteDetailsResponse> ValidateAsync(string token, CancellationToken cancellationToken);

    /// <summary>Sends the phone-verification OTP for a new-account acceptance.</summary>
    Task SendOtpAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts the invite. New account: password + OTP create the account and activate the affiliation.
    /// Existing account: the caller must be authenticated as that staff member. Re-checks full-time
    /// exclusivity, then logs the staff member in (identity tokens).
    /// </summary>
    Task<StaffTokensResult> AcceptAsync(AcceptInviteRequest request, Guid? authenticatedStaffUserId,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken);
}

internal sealed class StaffInviteAcceptService : IStaffInviteAcceptService
{
    private const int MinPasswordLength = 8;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IStaffUserRepository _staffUsers;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IStaffInviteTokenRepository _inviteTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly INotificationDispatcher _notifications;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IAuthContextRepository _identityLinks;
    private readonly IMembershipRepository _memberships;
    private readonly IEmploymentRepository _employments;
    private readonly IAccessContextProjector _projector;
    private readonly IDomainEventPublisher _events;

    public StaffInviteAcceptService(
        IDbConnectionFactory connectionFactory,
        IStaffUserRepository staffUsers,
        IStaffAffiliationRepository affiliations,
        IStaffInviteTokenRepository inviteTokens,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        INotificationDispatcher notifications,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService,
        IAuthContextRepository identityLinks,
        IMembershipRepository memberships,
        IEmploymentRepository employments,
        IAccessContextProjector projector,
        IDomainEventPublisher events)
    {
        _connectionFactory = connectionFactory;
        _staffUsers = staffUsers;
        _affiliations = affiliations;
        _inviteTokens = inviteTokens;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _notifications = notifications;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
        _identityLinks = identityLinks;
        _memberships = memberships;
        _employments = employments;
        _projector = projector;
        _events = events;

    }

    public async Task<InviteDetailsResponse> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        (StaffInviteTokenRow invite, StaffAffiliationRow affiliation) = await ResolveInviteAsync(token, cancellationToken);

        StaffInviteDetailsRow? details = await _affiliations.GetInviteDetailsAsync(invite.AffiliationId, cancellationToken);
        StaffAccountState? state = await _staffUsers.GetAccountStateAsync(affiliation.StaffUserId, cancellationToken);

        return new InviteDetailsResponse
        {
            FirstName = state?.FirstName,
            LastName = state?.LastName,
            SchoolName = details?.SchoolName,
            Role = affiliation.Role,
            EmploymentType = affiliation.EmploymentType,
            ExpiresAt = invite.ExpiresAt,
            HasAccount = state?.HasPassword ?? false
        };
    }

    public async Task SendOtpAsync(string token, CancellationToken cancellationToken)
    {
        (StaffInviteTokenRow invite, StaffAffiliationRow affiliation) = await ResolveInviteAsync(token, cancellationToken);

        string code = await _otpService.GenerateAsync(
            OtpPurpose.StaffInviteVerification, affiliation.StaffUserId, invite.Phone, cancellationToken);

        await _notifications.SendSmsAsync(invite.Phone,
            $"Your SchoolFlow verification code is {code}. It expires in 5 minutes.", cancellationToken);
    }

    public async Task<StaffTokensResult> AcceptAsync(AcceptInviteRequest request, Guid? authenticatedStaffUserId,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        (StaffInviteTokenRow invite, StaffAffiliationRow affiliation) =
            await ResolveInviteAsync(request.Token, cancellationToken);

        Guid staffUserId = affiliation.StaffUserId;
        StaffAccountState state = await _staffUsers.GetAccountStateAsync(staffUserId, cancellationToken)
            ?? throw new AppErrorException("This invitation is no longer valid.", 400, ErrorCodes.InviteInvalid,
                logReason: "Invite accept: staff_user missing.");

        // Exclusivity re-check — state may have changed since the invite was sent.
        bool isFullTime = affiliation.EmploymentType == EmploymentTypes.FullTime;
        if (await _affiliations.HasActiveFullTimeAsync(staffUserId, cancellationToken))
        {
            throw new AppErrorException(
                "You have a full-time role at another school. End it before joining another.",
                409, ErrorCodes.Conflict);
        }

        if (isFullTime && await _affiliations.HasAnyActiveAsync(staffUserId, cancellationToken))
        {
            throw new AppErrorException(
                "End your other school affiliations before accepting a full-time role.",
                409, ErrorCodes.Conflict);
        }

        if (!state.HasPassword)
        {
            await AcceptNewAccountAsync(request, invite, affiliation, staffUserId, cancellationToken);
        }
        else
        {
            await AcceptExistingAccountAsync(invite, affiliation, staffUserId, authenticatedStaffUserId,
                cancellationToken);
        }

        // Identity platform (EDD-001): the accepted employment links to its identity + position, and
        // the fact is published (audited via IAuditableEvent; Authorization/Communication react later).
        StaffIdentityLink link = await _identityLinks.EnsureStaffIdentityLinksAsync(staffUserId, affiliation.Id,
            cancellationToken);

        // Canonical belonging edge (EDD-007) + working relationship (EDD-009): the active affiliation
        // IS a 'staff' membership + employment, driven through the Membership/People contexts.
        await _employments.EnsureFromAffiliationAsync(affiliation.Id, cancellationToken);
        if (link.IdentityId is Guid identityId)
        {
            await _memberships.EnsureActiveAsync(identityId, affiliation.SchoolId, MembershipKind.Staff,
                cancellationToken);
            // Project the staff access_context from the canonical edges just written (EDD-012 B2a).
            await _projector.ProjectForIdentityAsync(identityId, cancellationToken);
        }

        await _events.PublishAsync(new EmploymentActivated(affiliation.Id, affiliation.SchoolId,
            staffUserId, affiliation.Role, link.Name), cancellationToken);

        return await IssueTokensAsync(staffUserId, state.Phone, state.KycStatus, ipAddress, userAgent,
            cancellationToken);
    }

    private async Task AcceptNewAccountAsync(AcceptInviteRequest request, StaffInviteTokenRow invite,
        StaffAffiliationRow affiliation, Guid staffUserId, CancellationToken cancellationToken)
    {
        if (request.Password is null || request.Password.Length < MinPasswordLength)
        {
            throw new AppErrorException($"Password must be at least {MinPasswordLength} characters.",
                400, ErrorCodes.ValidationError);
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new AppErrorException("Enter the verification code sent to your phone.",
                400, ErrorCodes.ValidationError);
        }

        OtpVerifyResult otp = await _otpService.VerifyAsync(
            OtpPurpose.StaffInviteVerification, staffUserId, request.Code.Trim(), cancellationToken);
        if (otp != OtpVerifyResult.Success)
        {
            throw MapOtpFailure(otp);
        }

        string passwordHash = _passwordHasher.Hash(request.Password);

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _staffUsers.CompleteAccountAsync(staffUserId, passwordHash, transaction.Transaction, cancellationToken);
        await _affiliations.ActivateAsync(affiliation.Id, transaction.Transaction, cancellationToken);
        await _inviteTokens.MarkUsedAsync(invite.Id, transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AcceptExistingAccountAsync(StaffInviteTokenRow invite, StaffAffiliationRow affiliation,
        Guid staffUserId, Guid? authenticatedStaffUserId, CancellationToken cancellationToken)
    {
        if (authenticatedStaffUserId is null || authenticatedStaffUserId.Value != staffUserId)
        {
            throw new AppErrorException("Log in to accept this invitation.", 401, ErrorCodes.Unauthorized,
                logReason: "Invite accept (existing account) without matching authentication.");
        }

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _affiliations.ActivateAsync(affiliation.Id, transaction.Transaction, cancellationToken);
        await _inviteTokens.MarkUsedAsync(invite.Id, transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<(StaffInviteTokenRow Invite, StaffAffiliationRow Affiliation)> ResolveInviteAsync(
        string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new AppErrorException("This invitation link is invalid.", 400, ErrorCodes.InviteInvalid,
                logReason: "Invite: empty token.");
        }

        StaffInviteTokenRow? invite = await _inviteTokens.GetByHashAsync(HashToken(rawToken), cancellationToken);
        if (invite is null || invite.UsedAt is not null)
        {
            throw new AppErrorException("This invitation link is invalid or already used.",
                400, ErrorCodes.InviteInvalid, logReason: "Invite: token not found or already used.");
        }

        if (invite.ExpiresAt < DateTime.UtcNow)
        {
            throw new AppErrorException("This invitation has expired. Ask your school to resend it.",
                400, ErrorCodes.InviteExpired, logReason: "Invite: token expired.");
        }

        StaffAffiliationRow? affiliation = await _affiliations.GetByIdAsync(invite.AffiliationId, cancellationToken);
        if (affiliation is null || affiliation.Status != "invited")
        {
            throw new AppErrorException("This invitation is no longer valid.", 400, ErrorCodes.InviteInvalid,
                logReason: "Invite: affiliation missing or not in 'invited' status.");
        }

        return (invite, affiliation);
    }

    private async Task<StaffTokensResult> IssueTokensAsync(Guid staffUserId, string phone, string kycStatus,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        AccessToken access = _accessTokenIssuer.IssueStaffIdentity(staffUserId, phone, kycStatus);

        RefreshTokenIssue refresh = await _refreshTokenService.IssueAsync(
            AuthActorTypes.Staff, staffUserId, ipAddress, userAgent, cancellationToken);

        return new StaffTokensResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresAt = refresh.ExpiresAt
        };
    }

    private static AppErrorException MapOtpFailure(OtpVerifyResult result)
    {
        return result switch
        {
            OtpVerifyResult.Expired => new AppErrorException("Verification code has expired. Request a new one.",
                400, ErrorCodes.OtpExpired, logReason: "Invite accept: OTP expired."),
            OtpVerifyResult.TooManyAttempts => new AppErrorException("Too many incorrect attempts. Request a new code.",
                429, ErrorCodes.TooManyRequests, logReason: "Invite accept: too many OTP attempts."),
            _ => new AppErrorException("Invalid verification code.", 400, ErrorCodes.InvalidOtp,
                logReason: "Invite accept: invalid or no active OTP.")
        };
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
