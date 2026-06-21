using System.Text.Json;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.Staff;

/// <summary>Reads a permission template's feature map (the JSONB column) for resolution.</summary>
internal interface IPermissionTemplateRepository
{
    Task<IReadOnlyDictionary<string, bool>?> GetFeaturesAsync(Guid templateId, CancellationToken cancellationToken);
}

internal sealed class PermissionTemplateRepository : BaseRepository, IPermissionTemplateRepository
{
    public PermissionTemplateRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<IReadOnlyDictionary<string, bool>?> GetFeaturesAsync(Guid templateId,
        CancellationToken cancellationToken)
    {
        // jsonb comes back as a JSON string via Npgsql when read as string.
        string? json = await QuerySingleOrDefaultAsync<string>(
            "SELECT features FROM permission_templates WHERE id = @Id",
            new { Id = templateId }, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
    }
}
