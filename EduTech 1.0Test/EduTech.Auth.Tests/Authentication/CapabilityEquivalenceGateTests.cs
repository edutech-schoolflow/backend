using EduTech.Shared.Authorization;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// EDD-015 / B2d.1 — the Validation Gate. The merge gate for Stage 3: it runs the live
/// <see cref="CapabilityResolver"/> and the parallel <see cref="CanonicalCapabilityResolver"/> against a
/// REAL, seeded database and proves they produce the exact same capability set for EVERY active access
/// context — not cherry-picked scenarios. If even one context diverges, Stage 3 (swapping the live
/// resolver onto the canonical model) does not merge.
///
/// Runs only when a database is supplied via the <c>GATE_DB</c> connection string (the throwaway-PG harness
/// sets it); otherwise it no-ops, so the normal unit suite stays DB-free.
/// </summary>
public class CapabilityEquivalenceGateTests
{
    [Fact]
    public async Task Legacy_And_Canonical_Resolve_Identically_For_Every_Active_Context()
    {
        string? conn = Environment.GetEnvironmentVariable("GATE_DB");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return;   // no DB provided → skip (normal CI). The harness supplies GATE_DB.
        }

        NpgsqlConnectionFactory factory = new(conn);
        ICapabilityResolver legacy = new CapabilityResolver(factory, new MemoryCache(new MemoryCacheOptions()));
        ICapabilityResolver canonical = new CanonicalCapabilityResolver(factory, new MemoryCache(new MemoryCacheOptions()));

        await using NpgsqlConnection db = new(conn);
        await db.OpenAsync();
        List<Guid> contexts = new();
        await using (NpgsqlCommand cmd = new("SELECT reference_id FROM access_contexts WHERE status = 'active'", db))
        await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                contexts.Add(reader.GetGuid(0));
            }
        }

        Assert.NotEmpty(contexts);   // the seed must produce active contexts, or the gate proves nothing

        List<string> mismatches = new();
        foreach (Guid contextId in contexts)
        {
            CapabilitySet legacySet = await legacy.GetCapabilitiesAsync(contextId, CancellationToken.None);
            CapabilitySet canonicalSet = await canonical.GetCapabilitiesAsync(contextId, CancellationToken.None);

            string l = string.Join(",", legacySet.Keys.OrderBy(k => k, StringComparer.Ordinal));
            string c = string.Join(",", canonicalSet.Keys.OrderBy(k => k, StringComparer.Ordinal));
            if (l != c)
            {
                mismatches.Add($"context {contextId}: legacy=[{l}] canonical=[{c}]");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {contexts.Count} contexts diverged:\n" + string.Join("\n", mismatches));
    }
}
