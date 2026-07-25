using System.IdentityModel.Tokens.Jwt;
using EduTech.Auth.Tokens;
using EduTech.Auth.Unified;
using EduTech.Shared.Constants;
using EduTech.Shared.Persistence;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;

namespace EduTech.Auth.Tests.Authentication;

/// <summary>
/// Canonical mint regression gate (EDD-012 B2d V1). Mints a token for EVERY active access context through
/// the LIVE <see cref="CanonicalContextMinter"/> against a REAL, seeded database and asserts the token
/// carries the canonical claims (identity + membership + context + organization, correct user_type, role
/// for staff) and NO legacy authorization (no can_* flag claims). This is the DB-integration coverage of
/// the canonical mint, kept after the legacy minter + the legacy-vs-canonical equivalence gate were deleted.
///
/// Runs only when a database is supplied via <c>GATE_DB</c> (the throwaway-PG harness sets it); otherwise
/// it no-ops, so the normal unit suite stays DB-free.
/// </summary>
public class MintCanonicalGateTests
{
    [Fact]
    public async Task CanonicalMint_ProducesCanonicalClaims_ForEverySeededContext()
    {
        string? conn = Environment.GetEnvironmentVariable("GATE_DB");
        if (string.IsNullOrWhiteSpace(conn))
        {
            return;   // no DB provided → skip (normal CI). The harness supplies GATE_DB.
        }

        DapperConfiguration.Configure();   // production's global Dapper setup (snake_case column matching)
        NpgsqlConnectionFactory factory = new(conn);
        IAccessTokenIssuer issuer = new AccessTokenIssuer(Config());
        IContextMinter minter = new CanonicalContextMinter(factory, issuer);
        AuthContextRepository contexts = new(factory);

        int minted = 0;
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
                    Role = row.Type == "owner" ? "owner" : row.Role
                };

                ContextMint mint = await minter.MintAsync(identity, context, default);
                Dictionary<string, string> claims = Claims(mint.Access.Token);

                // Canonical identity of the session — no legacy actor authority.
                Assert.Equal(identityId.ToString(), claims["identity_id"]);
                Assert.Equal(context.Id.ToString(), claims["context_id"]);
                Assert.Equal(context.MembershipId.ToString(), claims["membership_id"]);
                Assert.Equal(context.OrganizationId.ToString(), claims["organization_id"]);
                Assert.Equal(ExpectedUserType(context.Type), claims["user_type"]);
                if (context.Type == "staff")
                {
                    Assert.False(string.IsNullOrEmpty(claims.GetValueOrDefault("role")));
                }

                // Authorization is resolved server-side (B2b) — no flag claim rides the token.
                Assert.DoesNotContain(claims.Keys, k => k.StartsWith("can_", StringComparison.Ordinal));
                minted++;
            }
        }

        Assert.True(minted > 0, "the gate minted no contexts — seed the DB first");
    }

    private static string ExpectedUserType(string contextType) => contextType switch
    {
        "owner" => UserTypes.School,
        "staff" => UserTypes.Staff,
        "parent" => UserTypes.Parent,
        _ => contextType
    };

    private static Dictionary<string, string> Claims(string token)
    {
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Dictionary<string, string> claims = new(StringComparer.Ordinal);
        foreach (System.Security.Claims.Claim c in jwt.Claims)
        {
            claims[c.Type] = c.Value;
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
