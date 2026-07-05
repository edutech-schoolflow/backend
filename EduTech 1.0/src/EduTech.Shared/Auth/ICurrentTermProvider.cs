namespace EduTech.Shared.Auth;

/// <summary>Whether the current school (from the request context) has an active/current term set.</summary>
public interface ICurrentTermProvider
{
    Task<bool> HasCurrentTermAsync(CancellationToken cancellationToken);
}
