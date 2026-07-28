namespace EduTech.Shared.Events;

// EDD-017 — Identity publishes the canonical verification facts; Compliance runs the workflow.
// These are IDENTITY-level (not school-scoped), so they are deliberately NOT IAuditableEvent — the
// append-only identity_verifications table is the KYC audit trail. Consumers (later slices): notification,
// lifting staff read-only mode on IdentityVerificationSucceeded, etc.

/// <summary>An identity verification attempt has begun (status → pending).</summary>
public sealed class IdentityVerificationSubmittedEvent : DomainEvent
{
    public IdentityVerificationSubmittedEvent(Guid identityId) => IdentityId = identityId;

    public Guid IdentityId { get; }
}

/// <summary>An attempt passed at the provider — the person is now verified (status → verified).</summary>
public sealed class IdentityVerificationSucceededEvent : DomainEvent
{
    public IdentityVerificationSucceededEvent(Guid identityId) => IdentityId = identityId;

    public Guid IdentityId { get; }
}

/// <summary>An attempt failed at the provider (retryable).</summary>
public sealed class IdentityVerificationFailedEvent : DomainEvent
{
    public IdentityVerificationFailedEvent(Guid identityId, string? reason)
    {
        IdentityId = identityId;
        Reason = reason;
    }

    public Guid IdentityId { get; }
    public string? Reason { get; }
}

/// <summary>The identity's verification is rejected (status → rejected); the person may resubmit.</summary>
public sealed class IdentityVerificationRejectedEvent : DomainEvent
{
    public IdentityVerificationRejectedEvent(Guid identityId) => IdentityId = identityId;

    public Guid IdentityId { get; }
}
