using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.People.Domain;

/// <summary>
/// The Position aggregate (EDD-008): a job an organization can employ someone into. Positions are the
/// hinge between Organization, Employment, and Capabilities — Employment references a Position, and a
/// Position's permission template resolves its capabilities. "Owner" is a Position, not a special
/// table or role, so ownership can transfer without anything structural breaking.
///
/// Reference data by nature: a platform-seeded global default (<see cref="OrganizationId"/> null) or
/// an organization's own position. House style — guard invariants in-aggregate.
/// </summary>
internal sealed class Position
{
    public Position(Guid id, Guid? organizationId, string slug, string name, bool isAcademic)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new AppErrorException("A position needs a slug.", 400, ErrorCodes.ValidationError,
                logReason: "Position created without a slug.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppErrorException("A position needs a name.", 400, ErrorCodes.ValidationError,
                logReason: "Position created without a name.");
        }

        Id = id;
        OrganizationId = organizationId;
        Slug = slug;
        Name = name;
        IsAcademic = isAcademic;
    }

    public Guid Id { get; }

    /// <summary>Null = a platform-seeded global default; otherwise the organization that owns it.</summary>
    public Guid? OrganizationId { get; }

    public string Slug { get; }
    public string Name { get; }
    public bool IsAcademic { get; }

    /// <summary>A global default is available to every organization; an org position is that org's own.</summary>
    public bool IsGlobalDefault => OrganizationId is null;
}
