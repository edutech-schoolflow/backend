namespace EduTech.Shared.Authorization;

/// <summary>
/// The immutable set of capabilities effective in one workspace (context) right now (EDD-013). The
/// output of <see cref="ICapabilityResolver"/> — the only thing authorization ever asks about.
/// </summary>
public sealed class CapabilitySet
{
    private readonly HashSet<string> _capabilities;

    private CapabilitySet(HashSet<string> capabilities) => _capabilities = capabilities;

    public static readonly CapabilitySet Empty = new(new HashSet<string>(0, StringComparer.Ordinal));

    public static CapabilitySet Of(IEnumerable<string> capabilities) =>
        new(new HashSet<string>(capabilities, StringComparer.Ordinal));

    /// <summary>Every registered capability — the owner's set (owner grants everything).</summary>
    public static CapabilitySet All => Of(CapabilityRegistry.All.Select(c => c.Key));

    public bool Has(string capability) => _capabilities.Contains(capability);

    public IReadOnlyCollection<string> Keys => _capabilities;
}
