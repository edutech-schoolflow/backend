namespace EduTech.Shared.Models;

/// <summary>
/// Envelope for SUCCESS responses: <c>{ success, message, data }</c>.
/// Failures are returned as <see cref="ApiError"/> by GlobalExceptionMiddleware, not through here.
/// </summary>
public class ServiceResponses<T>
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public T? data { get; set; }

    public static ServiceResponses<T> Ok(T data, string message = "Success") =>
        new() { success = true, message = message, data = data };
}
