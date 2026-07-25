using System.IdentityModel.Tokens.Jwt;
using EduTech.Auth.Tokens;
using EduTech.Auth.Unified;
using EduTech.Shared.Persistence;
using EduTech.Workforce;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// The mint equivalence gate (EDD-012 B2d mint re-source) — the mint's analogue of the resolver Validation
/// Gate. It mints a token for EVERY active access context through BOTH the legacy minter (reads
/// school_owners / staff_affiliations) and the canonical minter (reads Employment/Position; staff_users only
/// for the user_id compat claim), and asserts the SEMANTIC claims are identical (ignoring iat/exp/nbf/jti)
/// plus the refresh actor key. Green here is the merge gate for the DI flip. Runs only when a seeded DB is
/// supplied via GATE_DB (the throwaway-PG harness); skipped in normal CI.
/// </summary>
public class MintEquivalenceGateTests
{
    private static readonly string[] IgnoredClaims = { "iat", "exp", "nbf", "jti" };

    [Fact]
    public async Task CanonicalMint_IsClaimEquivalentToLegacy_ForEverySeededContext()
    {
        string? conn = Environment.GetEnvironmentVariable("GATE_DB");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return;   // no DB → skip (normal CI). The harness supplies GATE_DB.
        }

        DapperConfiguration.Configure();   // production's global Dapper setup (snake_case column matching)
        NpgsqlConnectionFactory factory = new(conn);
        IAccessTokenIssuer issuer = new AccessTokenIssuer(Config());
        IContextMinter legacy = new LegacyContextMinter(new AuthContextRepository(factory),
            new StaffAffiliationRepository(factory), new StaffUserRepository(factory), issuer);
        IContextMinter canonical = new CanonicalContextMinter(factory, issuer);

        AuthContextRepository contexts = new(factory);
        int compared = 0;

        foreach ((Guid identityId, string phone) in await LoadIdentitiesWithContextsAsync(conn))
        {
            EduTech.Identity.Domain.Identity identity = new(identityId, "T", null, "T", phone, null, null,
                false, false, EduTech.Identity.Domain.IdentityStatus.Active, 0, null);

            foreach (AccessContextRow row in await contexts.ListAccessContextsAsync(identityId, default))
            {
                AuthContextItem context = new()
                {
                    Id = row.ReferenceId,
                    Type = row.Type,
                    AccessContextId = row.AccessContextId,
                    OrganizationId = row.OrganizationId,
                    MembershipId = row.MembershipId,
                    OrganizationName = row.OrganizationName,
                    OrganizationSlug = row.OrganizationSlug,
                    Role = row.Type == "owner" ? "owner" : row.Role
                };

                ContextMint legacyMint = await legacy.MintAsync(identity, context, default);
                ContextMint canonicalMint = await canonical.MintAsync(identity, context, default);

                Assert.Equal(legacyMint.RefreshActorType, canonicalMint.RefreshActorType);
                Assert.Equal(legacyMint.RefreshActorId, canonicalMint.RefreshActorId);
                Assert.Equal(Claims(legacyMint.Access.Token), Claims(canonicalMint.Access.Token));
                compared++;
            }
        }

        Assert.True(compared > 0, "the gate compared no contexts — seed the DB first");
    }

    private static SortedDictionary<string, string> Claims(string token)
    {
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        SortedDictionary<string, string> claims = new(StringComparer.Ordinal);
        foreach (System.Security.Claims.Claim c in jwt.Claims)
        {
            if (!IgnoredClaims.Contains(c.Type))
            {
                claims[c.Type] = c.Value;
            }
        }
        return claims;
    }

    private static async Task<List<(Guid IdentityId, string Phone)>> LoadIdentitiesWithContextsAsync(string conn)
    {
        List<(Guid, string)> rows = new();
        await using NpgsqlConnection db = new(conn);
        await db.OpenAsync();
        await using NpgsqlCommand cmd = new(
            "SELECT DISTINCT i.id, i.phone FROM identities i " +
            "JOIN access_contexts ac ON ac.identity_id = i.id AND ac.status = 'active'", db);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }
        return rows;
    }

    private static IConfiguration Config()
    {
        Mock<IConfiguration> config = new();
        config.Setup(c => c["Jwt:SigningKey"]).Returns("mint-gate-signing-key-at-least-sixty-four-bytes-long-for-hmac-512!!");
        config.Setup(c => c["Jwt:PlatformAdminSigningKey"]).Returns("mint-gate-admin-signing-key-at-least-sixty-four-bytes-long-hmac-5!");
        config.Setup(c => c["Jwt:Issuer"]).Returns("EduTech");
        config.Setup(c => c["Jwt:Audience"]).Returns("EduTechApp");
        return config.Object;
    }
}
