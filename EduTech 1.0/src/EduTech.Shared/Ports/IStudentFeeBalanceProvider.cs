namespace EduTech.Shared.Ports;

/// <summary>
/// SharedKernel PORT (EDD-002 V1): "what do these students owe right now?" is a Finance question.
/// Finance implements and registers this; consumers (e.g. Academics' parent-facing reads) depend on
/// the abstraction only — no reference to the Fees module and no queries against its tables.
/// </summary>
public interface IStudentFeeBalanceProvider
{
    /// <summary>
    /// Outstanding balance per student for their school's current term (compulsory fees + subscribed
    /// optional ones, minus successful payments). Students with nothing owed may be absent from the map.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetOutstandingAsync(IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken);
}
