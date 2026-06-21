using EduTech.Shared.Models;

namespace EduTech.Shared.Exceptions;

/// <summary>
/// A handled application error. Carries the HTTP status, an integer <see cref="ErrorCode"/>
/// (see <c>ErrorCodes</c>), and optional field-level <see cref="ValidationErrors"/>. Surfaced as an
/// <c>ApiError</c> by GlobalExceptionMiddleware.
/// </summary>
public class AppErrorException : Exception
{
    public int StatusCode { get; }
    public int? ErrorCode { get; }
    public IReadOnlyList<ValidationError>? ValidationErrors { get; }

    /// <summary>
    /// The real reason, logged server-side but NEVER returned to the client. Use this when the public
    /// <c>message</c> is deliberately vague (e.g. "Registration failed") to avoid leaking why.
    /// </summary>
    public string? LogReason { get; }

    public AppErrorException(string message, int statusCode, int? errorCode = null,
        IReadOnlyList<ValidationError>? validationErrors = null, string? logReason = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ValidationErrors = validationErrors;
        LogReason = logReason;
    }
}
