using System.Data;
using Dapper;

namespace EduTech.Shared.Persistence;

/// <summary>
/// Teaches Dapper to bind <see cref="DateOnly"/> (and, via the same handler, <c>DateOnly?</c>) to a
/// SQL <c>date</c>. Dapper doesn't handle DateOnly out of the box and throws when it sees one as a
/// parameter; Npgsql 8 maps it natively once Dapper passes the value through. Registered once in
/// <c>AddEduTechPersistence</c>.
/// </summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value;   // Npgsql 8 maps DateOnly -> date
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly dateOnly => dateOnly,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        string text => DateOnly.Parse(text),
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };
}
