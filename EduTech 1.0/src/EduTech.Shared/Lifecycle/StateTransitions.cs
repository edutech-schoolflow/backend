using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Shared.Lifecycle;

/// <summary>
/// A reusable, declarative state machine for a status enum: which states may follow which, plus an
/// optional set of terminal states. Services call <see cref="Require"/> before persisting a status
/// change so an illegal jump (e.g. paid -> draft) fails with a clean 409 instead of corrupting the
/// lifecycle. Pair it with a conditional UPDATE in the repository for race-safe enforcement —
/// <c>... WHERE id = @Id AND status = @From</c> — since the in-memory check alone has a read/write race.
/// </summary>
public sealed class StateTransitions<TState> where TState : struct, Enum
{
    private readonly IReadOnlyDictionary<TState, IReadOnlySet<TState>> _allowed;
    private readonly IReadOnlySet<TState> _terminal;

    public StateTransitions(
        IReadOnlyDictionary<TState, IReadOnlySet<TState>> allowed,
        IReadOnlySet<TState>? terminal = null)
    {
        _allowed = allowed;
        _terminal = terminal ?? new HashSet<TState>();
    }

    /// <summary>True if <paramref name="to"/> is a permitted next state from <paramref name="from"/>.</summary>
    public bool CanTransition(TState from, TState to) =>
        _allowed.TryGetValue(from, out IReadOnlySet<TState>? next) && next.Contains(to);

    /// <summary>True if the state has no permitted outgoing transitions (e.g. approved, paid).</summary>
    public bool IsTerminal(TState state) => _terminal.Contains(state);

    /// <summary>
    /// No-op if already in the target state (idempotent); otherwise throws 409 when the transition
    /// isn't allowed. The public message names the states; the real reason is logged, not leaked.
    /// </summary>
    public void Require(TState from, TState to)
    {
        if (from.Equals(to))
        {
            return;
        }

        if (!CanTransition(from, to))
        {
            throw new AppErrorException(
                $"Cannot change status from {from} to {to}.", 409, ErrorCodes.Conflict,
                logReason: $"Illegal {typeof(TState).Name} transition {from} -> {to}.");
        }
    }
}
