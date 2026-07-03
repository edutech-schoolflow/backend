using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Fees;

/// <summary>
/// Invoice lifecycle: a bursar drafts then issues; payments move it to partial then paid; void cancels.
/// paid/void are terminal. The guard gives a clean 409 on an illegal move (e.g. paying a draft invoice).
/// </summary>
internal static class InvoiceLifecycle
{
    public static readonly StateTransitions<InvoiceStatus> Rules = new(
        new Dictionary<InvoiceStatus, IReadOnlySet<InvoiceStatus>>
        {
            [InvoiceStatus.Draft]   = Set(InvoiceStatus.Issued, InvoiceStatus.Void),
            [InvoiceStatus.Issued]  = Set(InvoiceStatus.Partial, InvoiceStatus.Paid, InvoiceStatus.Void),
            [InvoiceStatus.Partial] = Set(InvoiceStatus.Paid, InvoiceStatus.Void),
            [InvoiceStatus.Paid]    = Set(),
            [InvoiceStatus.Void]    = Set(),
        },
        terminal: Set(InvoiceStatus.Paid, InvoiceStatus.Void));

    private static IReadOnlySet<InvoiceStatus> Set(params InvoiceStatus[] states) =>
        new HashSet<InvoiceStatus>(states);
}
