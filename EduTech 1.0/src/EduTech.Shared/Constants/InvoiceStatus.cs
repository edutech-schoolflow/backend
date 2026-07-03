namespace EduTech.Shared.Constants;

/// <summary>
/// Invoice lifecycle — a fixed, closed set. Stored as the snake_case string on <c>invoices.status</c>.
/// draft -> issued (visible to parents) -> partial -> paid; void is the cancel path. paid/void terminal.
/// </summary>
public enum InvoiceStatus
{
    Draft,
    Issued,
    Partial,
    Paid,
    Void
}
