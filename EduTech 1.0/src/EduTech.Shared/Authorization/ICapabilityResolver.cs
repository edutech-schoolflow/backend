namespace EduTech.Shared.Authorization;

/// <summary>
/// The single, actor-neutral authorization API of the platform (EDD-013). Given only a
/// <c>context_id</c> (the current workspace), it derives the capabilities effective there right now.
///
/// <para><b>Golden rule: authorization is derived, never embedded.</b> Authentication identifies the
/// workspace; this resolves what may be done in it — at request time, server-side, off the token. No
/// module ever queries positions, permission templates, or overrides directly; every authorization
/// question in the platform goes through this one service, so future capabilities (delegated admin,
/// temporary/emergency access, time-bound roles) change one component, not every module.</para>
/// </summary>
public interface ICapabilityResolver
{
    /// <summary>True if the workspace grants the capability right now.</summary>
    Task<bool> HasCapabilityAsync(Guid contextId, string capability, CancellationToken cancellationToken);

    /// <summary>Every capability effective in the workspace right now.</summary>
    Task<CapabilitySet> GetCapabilitiesAsync(Guid contextId, CancellationToken cancellationToken);

    /// <summary>Drops the cached set for a context after a permission change (template/override/role/employment).</summary>
    void Invalidate(Guid contextId);
}
