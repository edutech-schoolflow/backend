using EduTech.Shared.Constants;

namespace EduTech.Students.Academics.Transition;

/// <summary>Where the school stands relative to the calendar clock.</summary>
public enum TransitionStatus
{
    /// <summary>No current term is set — the calendar needs attention before anything can be proposed.</summary>
    NoCurrentTerm,
    /// <summary>The current term is still running (or has no end date, i.e. manually managed).</summary>
    TermOngoing,
    /// <summary>The current term's end date has passed — a move to the next term/session is proposed.</summary>
    TransitionDue
}

/// <summary>
/// The auto-prepare + confirm contract: the platform detects and prepares the next term/session,
/// but only a school confirmation moves the CURRENT pointers (attendance, grades and fees all hang
/// off them — they are never flipped silently).
/// </summary>
public sealed class TransitionProposalResponse
{
    public required TransitionStatus Status { get; init; }

    public Term? CurrentTerm { get; init; }
    public string? CurrentSession { get; init; }
    public DateOnly? CurrentTermEndDate { get; init; }

    /// <summary>The proposed next term (only when a transition is due).</summary>
    public Term? NextTerm { get; init; }
    public int? NextSessionStartYear { get; init; }
    /// <summary>True when the move crosses into a new session — promotion must run first.</summary>
    public bool IsSessionBoundary { get; init; }
    /// <summary>True when the next term already exists (roll-forward prepared it); confirm creates it otherwise.</summary>
    public bool NextTermPrepared { get; init; }
    public Guid? NextTermId { get; init; }
    /// <summary>Session boundary only: active students not yet enrolled in the target session.</summary>
    public int? StudentsAwaitingPromotion { get; init; }
}
