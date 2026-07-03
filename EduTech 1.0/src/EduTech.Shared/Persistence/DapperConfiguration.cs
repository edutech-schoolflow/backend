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

        // Dapper can't bind DateOnly out of the box — teach it to (covers DateOnly + DateOnly?).
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        // NOTE: domain enums are NOT registered as type handlers here — Dapper deliberately ignores
        // custom handlers for enums (LookupDbType maps any enum to its underlying int before checking
        // typeHandlers), so a handler would silently store ORDINALS. Instead, repositories convert
        // enum <-> snake_case string at the query boundary via SnakeCaseEnum. Enums live in the domain
        // and JSON layers; the DB sees the string.
    }
}
