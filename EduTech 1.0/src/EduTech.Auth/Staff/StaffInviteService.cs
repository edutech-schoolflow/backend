using System.Security.Cryptography;
using System.Text;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Notifications;
using EduTech.Shared.Persistence;
using EduTech.Shared.Phone;
using Microsoft.Extensions.Configuration;

namespace EduTech.Auth.Staff;

/// <summary>School-side staff invitations (Actor 2, Path B).</summary>
public interface IStaffInviteService
{
    /// <summary>
    /// Invites a staff member to the CURRENT school (from the SchoolAuth context). Enforces full-time
    /// exclusivity, creates/finds the global staff identity, creates the pending affiliation + invite
    /// token, and sends the invite link by SMS.
    /// </summary>
    Task<InviteStaffResponse> InviteAsync(InviteStaffRequest request, CancellationToken cancellationToken);

    /// <summary>Re-sends the invite for a still-pending staff member (new token + SMS).</summary>
    Task<InviteStaffResponse> ResendInviteAsync(Guid affiliationId, CancellationToken cancellationToken);
}

internal sealed class StaffInviteService : IStaffInviteService
{
    private const int InviteExpiryHours = 72;

    private static readonly HashSet<string> InvitableRoles = new HashSet<string>
    {
        StaffRoles.SchoolAdmin, StaffRoles.Principal, StaffRoles.VicePrincipal,
        StaffRoles.Teacher, StaffRoles.Bursar, StaffRoles.Registrar
    };

    private readonly IEduTechRequestContext _requestContext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IStaffUserRepository _staffUsers;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IStaffInviteTokenRepository _inviteTokens;
    private readonly INotificationDispatcher _notifications;
    private readonly string _inviteBaseUrl;

    public StaffInviteService(
        IEduTechRequestContext requestContext,
        IDbConnectionFactory connectionFactory,
        IStaffUserRepository staffUsers,
        IStaffAffiliationRepository affiliations,
        IStaffInviteTokenRepository inviteTokens,
        INotificationDispatcher notifications,
        IConfiguration configuration)
    {
        _requestContext = requestContext;
        _connectionFactory = connectionFactory;
        _staffUsers = staffUsers;
        _affiliations = affiliations;
        _inviteTokens = inviteTokens;
        _notifications = notifications;
        _inviteBaseUrl = configuration["App:StaffInviteBaseUrl"] ?? "http://localhost:3000/staff/register";
    }

    public async Task<InviteStaffResponse> InviteAsync(InviteStaffRequest request, CancellationToken cancellationToken)
    {
        // ── Validate ──────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new AppErrorException("First and last name are required.", 400, ErrorCodes.ValidationError);
        }

        if (!InvitableRoles.Contains(request.Role))
        {
            throw new AppErrorException("Select a valid staff role.", 400, ErrorCodes.ValidationError);
        }

        if (!EmploymentTypes.IsValid(request.EmploymentType))
        {
            throw new AppErrorException("Select full-time or part-time.", 400, ErrorCodes.ValidationError);
        }

        string? phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            throw new AppErrorException("Enter a valid Nigerian phone number.", 400, ErrorCodes.ValidationError);
        }

        Guid schoolId = CurrentSchoolId();
        Guid? invitedBy = Guid.TryParse(_requestContext.UserId, out Guid inviter) ? inviter : null;
        bool isFullTime = request.EmploymentType == EmploymentTypes.FullTime;

        // ── Exclusivity + duplicate pre-checks (against any existing identity) ─
        Guid? existingStaffUserId = await _staffUsers.GetIdByPhoneAsync(phone, cancellationToken);
        StaffAffiliationRow? existingAffiliation = null;

        if (existingStaffUserId is Guid existingId)
        {
            // Same school FIRST — a duplicate in your own school must get the accurate message,
            // not the cross-school "elsewhere" one (the exclusivity checks below are school-agnostic).
            existingAffiliation = await _affiliations.GetAsync(existingId, schoolId, cancellationToken);
            if (existingAffiliation is not null
                && (existingAffiliation.Status == "active" || existingAffiliation.Status == "invited"))
            {
                throw new AppErrorException(
                    "You already have a staff member with same details.", 409, ErrorCodes.Conflict);
            }

            // Then cross-school exclusivity (these are genuinely "elsewhere").
            if (await _affiliations.HasActiveFullTimeAsync(existingId, cancellationToken))
            {
                throw new AppErrorException(
                    "This person is a full-time staff member elsewhere and can't be added.",
                    409, ErrorCodes.Conflict);
            }

            if (isFullTime && await _affiliations.HasAnyActiveAsync(existingId, cancellationToken))
            {
                throw new AppErrorException(
                    "This person already works at another school and can't be added full-time.",
                    409, ErrorCodes.Conflict);
            }
        }

        // ── Atomic: identity + affiliation + token ────────────────────────────
        string rawToken = GenerateToken();
        DateTime expiresAt = DateTime.UtcNow.AddHours(InviteExpiryHours);

        await using (DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken))
        {
            Guid staffUserId = existingStaffUserId
                ?? await _staffUsers.CreatePendingAsync(request.FirstName.Trim(),
                    string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                    request.LastName.Trim(), phone, transaction.Transaction, cancellationToken);

            Guid affiliationId;
            if (existingAffiliation is not null)
            {
                // Resigned/inactive slot — re-invite it (unique(staff_user_id, school_id) blocks a new insert).
                affiliationId = existingAffiliation.Id;
                await _affiliations.ReInviteAsync(affiliationId, request.Role, request.Position?.Trim(),
                    request.EmploymentType, invitedBy, transaction.Transaction, cancellationToken);
            }
            else
            {
                affiliationId = await _affiliations.CreateInvitedAsync(staffUserId, schoolId, request.Role,
                    request.Position?.Trim(), request.EmploymentType, invitedBy, transaction.Transaction,
                    cancellationToken);
            }

            await _inviteTokens.CreateAsync(affiliationId, phone, HashToken(rawToken), expiresAt,
                transaction.Transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // ── Deliver (after commit) ────────────────────────────────────────────
        string inviteLink = $"{_inviteBaseUrl}?token={rawToken}";
        await _notifications.SendSmsAsync(phone,
            $"You've been invited to join a school on SchoolFlow. Accept your invite: {inviteLink} " +
            $"(expires in {InviteExpiryHours} hours).", cancellationToken);

        return new InviteStaffResponse { InviteLink = inviteLink, ExpiresAt = expiresAt };
    }

    public async Task<InviteStaffResponse> ResendInviteAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();

        StaffDirectoryRow? staff = await _affiliations.GetForSchoolAsync(affiliationId, schoolId, cancellationToken);
        if (staff is null)
        {
            throw new AppErrorException("Staff member not found.", 404, ErrorCodes.NotFound);
        }

        if (staff.Status != "invited")
        {
            throw new AppErrorException("This staff member has already accepted their invite.",
                409, ErrorCodes.Conflict);
        }

        string rawToken = GenerateToken();
        DateTime expiresAt = DateTime.UtcNow.AddHours(InviteExpiryHours);

        await using (DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken))
        {
            await _inviteTokens.CreateAsync(affiliationId, staff.Phone, HashToken(rawToken), expiresAt,
                transaction.Transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        string inviteLink = $"{_inviteBaseUrl}?token={rawToken}";
        await _notifications.SendSmsAsync(staff.Phone,
            $"You've been invited to join a school on SchoolFlow. Accept your invite: {inviteLink} " +
            $"(expires in {InviteExpiryHours} hours).", cancellationToken);

        return new InviteStaffResponse { InviteLink = inviteLink, ExpiresAt = expiresAt };
    }

    private Guid CurrentSchoolId()
    {
        if (string.IsNullOrWhiteSpace(_requestContext.SchoolId)
            || !Guid.TryParse(_requestContext.SchoolId, out Guid schoolId))
        {
            throw new AppErrorException("No school context on this request.", 403, ErrorCodes.Forbidden);
        }

        return schoolId;
    }

    private static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
