using Dapper;

namespace EduTech.Shared.Persistence;

/// <summary>
/// One-time global Dapper setup. Call once at application startup
/// (done by <see cref="PersistenceServiceCollectionExtensions.AddEduTechPersistence"/>).
/// </summary>
public static class DapperConfiguration
{
    public static void Configure()
    {
        // Map snake_case columns (school_id) to .NET members (SchoolId) without per-column aliases.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
