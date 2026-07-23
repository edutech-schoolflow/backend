using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Membership.Domain;

public enum MembershipStatus
{
    Active,
    Ended
}

/// <summary>
/// The Membership aggregate (EDD-007): the canonical belonging edge — one identity belongs to one
/// organization in one capacity (<see cref="MembershipKind"/>). It is the source of truth for adult
/// membership; Employment and Access Context reference/derive from it, never the reverse.
///
/// House style: intent methods that enforce invariants in-aggregate (throw on violation). The edge
/// itself is keyed by (identity, organization, kind) — the DB carries a matching unique constraint.
/// </summary>
internal sealed class Membership
{
    public Membership(Guid id, Guid identityId, Guid organizationId, string kind,
        MembershipStatus status, DateTime joinedAt, DateTime? endedAt)
    {
        if (!MembershipKind.IsValid(kind))
        {
            throw new AppErrorException("Unknown membership kind.", 400, ErrorCodes.ValidationError,
                logReason: $"Membership created with invalid kind '{kind}'.");
        }

        Id = id;
        IdentityId = identityId;
        OrganizationId = organizationId;
        Kind = kind;
        Status = status;
        JoinedAt = joinedAt;
        EndedAt = endedAt;
    }

    public Guid Id { get; }
    public Guid IdentityId { get; }
    public Guid OrganizationId { get; }
    public string Kind { get; }
    public MembershipStatus Status { get; private set; }
    public DateTime JoinedAt { get; }
    public DateTime? EndedAt { get; private set; }

    /// <summary>Re-activates an ended membership (idempotent if already active): clears the end marker.</summary>
    public void Activate()
    {
        Status = MembershipStatus.Active;
        EndedAt = null;
    }

    /// <summary>Ends the membership. Ending an already-ended membership is a no-op (idempotent).</summary>
    public void End(DateTime nowUtc)
    {
        if (Status == MembershipStatus.Ended)
        {
            return;
        }

        Status = MembershipStatus.Ended;
        EndedAt = nowUtc;
    }
}
