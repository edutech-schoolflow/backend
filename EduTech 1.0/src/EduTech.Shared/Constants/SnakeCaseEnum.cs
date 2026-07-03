using System.Text.Json;

namespace EduTech.Shared.Constants;

/// <summary>
/// Bidirectional mapping between an enum member and its snake_case wire/DB string. It uses the SAME
/// policy (<see cref="JsonNamingPolicy.SnakeCaseLower"/>) the API uses for JSON, so the value Dapper
/// stores in a VARCHAR column and the value serialized to the frontend are always identical
/// (e.g. <c>ClassLevel.JuniorSecondary</c> ⇄ <c>"junior_secondary"</c>). Maps are built once per type.
///
/// This is the single source of truth used by both <c>EnumStringHandler&lt;T&gt;</c> (storage) and the
/// services (parsing query-string filters). We deliberately store the STRING, never the int ordinal,
/// so reordering enum members can never corrupt data.
/// </summary>
public static class SnakeCaseEnum
{
    private static readonly JsonNamingPolicy Policy = JsonNamingPolicy.SnakeCaseLower;

    /// <summary>The snake_case wire/DB string for an enum value.</summary>
    public static string ToWire<T>(T value) where T : struct, Enum => Cache<T>.ToWireMap[value];

    /// <summary>Parse a snake_case string (case-insensitive) back to its enum value; false if unknown.</summary>
    public static bool TryParse<T>(string? raw, out T value) where T : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(raw) && Cache<T>.FromWireMap.TryGetValue(raw.Trim(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Parse a snake_case string that MUST be valid (e.g. a value read from our own DB column).
    /// Throws if it isn't — a bad value means corrupt data, not user error.
    /// </summary>
    public static T Parse<T>(string? raw) where T : struct, Enum =>
        TryParse(raw, out T value)
            ? value
            : throw new InvalidOperationException($"'{raw}' is not a valid {typeof(T).Name}.");

    private static class Cache<T> where T : struct, Enum
    {
        public static readonly IReadOnlyDictionary<T, string> ToWireMap;
        public static readonly IReadOnlyDictionary<string, T> FromWireMap;

        static Cache()
        {
            Dictionary<T, string> toWire = new Dictionary<T, string>();
            Dictionary<string, T> fromWire = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

            foreach (T member in Enum.GetValues<T>())
            {
                string wire = Policy.ConvertName(member.ToString());
                toWire[member] = wire;
                fromWire[wire] = member;
            }

            ToWireMap = toWire;
            FromWireMap = fromWire;
        }
    }
}
