using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Organization.Domain;

public enum OrganizationStatus
{
    Active,
    Suspended,
    Archived
}

/// <summary>
/// The Organization aggregate (EDD-010): the platform root — the account/company that owns everything
/// (Slack's Workspace, not its channels). It stays as boring as Identity, owning only *institutional
/// identity*: name, slug, type, status, and the owner (as a Membership). Schools, memberships,
/// employments, positions and branding all hang off it — they are never inside it.
/// </summary>
internal sealed class Organization
{
    public Organization(Guid id, string name, string slug, string type, OrganizationStatus status,
        Guid? ownerMembershipId, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppErrorException("An organization must have a name.", 400,
                ErrorCodes.ValidationError, logReason: "Organization created without a name.");
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new AppErrorException("An organization must have a slug.", 400,
                ErrorCodes.ValidationError, logReason: "Organization created without a slug.");
        }

        if (!OrganizationType.IsValid(type))
        {
            throw new AppErrorException("Unknown organization type.", 400, ErrorCodes.ValidationError,
                logReason: $"Organization created with invalid type '{type}'.");
        }

        Id = id;
        Name = name;
        Slug = slug;
        Type = type;
        Status = status;
        OwnerMembershipId = ownerMembershipId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public string Slug { get; }
    public string Type { get; }
    public OrganizationStatus Status { get; private set; }
    public Guid? OwnerMembershipId { get; private set; }
    public DateTime CreatedAt { get; }

    /// <summary>Renames the organization (slug is stable — it's the URL identity).</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppErrorException("An organization must have a name.", 400,
                ErrorCodes.ValidationError, logReason: "Organization renamed to blank.");
        }

        Name = name;
    }

    public void Suspend() => Status = OrganizationStatus.Suspended;

    public void Archive() => Status = OrganizationStatus.Archived;

    /// <summary>Restores a suspended/archived organization to active.</summary>
    public void Activate() => Status = OrganizationStatus.Active;

    public void TransferOwnership(Guid ownerMembershipId) => OwnerMembershipId = ownerMembershipId;
}
