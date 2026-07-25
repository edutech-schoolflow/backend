using EduTech.Shared.Authorization;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// Canonical authorization regression gate (EDD-015 / B2d V1). Runs the LIVE
/// <see cref="CanonicalCapabilityResolver"/> against a REAL, seeded database and asserts the capability set
/// for every active access context resolves correctly from the canonical graph
/// (AccessContext → Membership → Employment → Position → template/overrides). This is the DB-integration
/// coverage of the canonical resolver, kept after the legacy resolver + the legacy-vs-canonical equivalence
/// gate were deleted (their migration job done — see EDD-012 V1 reframe).
///
/// Runs only when a database is supplied via <c>GATE_DB</c> (the throwaway-PG harness sets it); otherwise it
/// no-ops, so the normal unit suite stays DB-free.
/// </summary>
public class CapabilityCanonicalGateTests
{
    [Fact]
    public async Task CanonicalResolver_ResolvesCorrectly_ForEverySeededContext()
    {
        string? conn = Environment.GetEnvironmentVariable("GATE_DB");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return;   // no DB provided → skip (normal CI). The harness supplies GATE_DB.
        }

        DapperConfiguration.Configure();   // production's global Dapper setup (snake_case column matching)
        NpgsqlConnectionFactory factory = new(conn);
        ICapabilityResolver resolver = new CanonicalCapabilityResolver(factory, new MemoryCache(new MemoryCacheOptions()));

        List<(Guid ContextId, string Type)> contexts = await LoadContextsAsync(conn);
        Assert.NotEmpty(contexts);   // the seed must produce active contexts, or the gate proves nothing

        string allCaps = string.Join(",", CapabilitySet.All.Keys.OrderBy(k => k, StringComparer.Ordinal));
        int owners = 0, parents = 0, staff = 0;

        foreach ((Guid contextId, string type) in contexts)
        {
            CapabilitySet caps = await resolver.GetCapabilitiesAsync(contextId, CancellationToken.None);
            switch (type)
            {
                case "owner":   // owner grants every capability (replaces the is_owner bypass)
                    Assert.Equal(allCaps, string.Join(",", caps.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                    owners++;
                    break;
                case "parent":  // parent has no workspace capabilities
                    Assert.Empty(caps.Keys);
                    parents++;
                    break;
                case "staff":   // resolves from the canonical permission model without error
                    staff++;
                    break;
            }
        }

        Assert.True(owners > 0 && parents > 0 && staff > 0,
            $"seed must cover owner, parent, and staff (got owner={owners} parent={parents} staff={staff})");
    }

    private static async Task<List<(Guid, string)>> LoadContextsAsync(string conn)
    {
        List<(Guid, string)> rows = new();
        await using NpgsqlConnection db = new(conn);
        await db.OpenAsync();
        await using NpgsqlCommand cmd = new(
            "SELECT reference_id, type FROM access_contexts WHERE status = 'active'", db);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }
        return rows;
    }
}
