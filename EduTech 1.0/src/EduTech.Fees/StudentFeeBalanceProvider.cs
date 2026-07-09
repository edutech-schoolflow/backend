using EduTech.Shared.Persistence;
using EduTech.Shared.Ports;

namespace EduTech.Fees;

/// <summary>
/// Finance's implementation of the <see cref="IStudentFeeBalanceProvider"/> port: the same
/// parent-pull balance rule the parent fee view uses — approved + active fee types of the student's
/// school's CURRENT term, applicable to their class, compulsory or subscribed, minus successful
/// payments. Not tenant-scoped: parent-facing callers span schools, so it keys by student ids.
/// </summary>
internal sealed class StudentFeeBalanceProvider : BaseRepository, IStudentFeeBalanceProvider
{
    public StudentFeeBalanceProvider(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetOutstandingAsync(
        IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        IReadOnlyList<(Guid StudentId, decimal Outstanding)> rows =
            await QueryAsync<(Guid, decimal)>(
                """
                SELECT st.id AS StudentId,
                       COALESCE(SUM(GREATEST(ft.amount - COALESCE(paid.total, 0), 0)), 0) AS Outstanding
                FROM students st
                JOIN terms t   ON t.school_id = st.school_id AND t.is_current = TRUE
                JOIN fee_types ft ON ft.school_id = st.school_id AND ft.term_id = t.id
                                 AND ft.approval_status = 'approved' AND ft.is_active = TRUE
                JOIN fee_type_classes ftc ON ftc.fee_type_id = ft.id AND ftc.class_id = st.class_id
                LEFT JOIN LATERAL (
                    SELECT SUM(p.base_amount) AS total
                    FROM payments p
                    WHERE p.student_id = st.id AND p.fee_type_id = ft.id AND p.status = 'successful'
                ) paid ON TRUE
                WHERE st.id = ANY(@StudentIds)
                  AND (ft.category = 'compulsory'
                       OR EXISTS (SELECT 1 FROM fee_subscriptions fs
                                  WHERE fs.student_id = st.id AND fs.fee_type_id = ft.id))
                GROUP BY st.id
                """,
                new { StudentIds = studentIds.ToArray() }, cancellationToken);

        return rows.ToDictionary(r => r.Item1, r => r.Item2);
    }
}
