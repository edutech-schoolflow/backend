namespace EduTech.Shared.Models;

/// <summary>
/// Rich error response (kedco-style). Returned for every failed request. <see cref="Details"/> and
/// <see cref="StackTrace"/> are populated only in Development; null fields are omitted from JSON.
/// </summary>
public class ApiError
{
    public bool Success { get; set; } = false;

    public int StatusCode { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Machine-readable code for client-side handling (see <c>ErrorCodes</c>).</summary>
    public int? ErrorCode { get; set; }

    /// <summary>Exception detail — Development only.</summary>
    public string? Details { get; set; }

    /// <summary>Stack trace — Development only.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Field-level errors for 400 (validation) responses.</summary>
    public List<ValidationError>? ValidationErrors { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Request path that produced the error.</summary>
    public string? Path { get; set; }
}
