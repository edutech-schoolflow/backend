using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Middleware;

/// <summary>
/// Logs one structured line per request. Request/response JSON bodies are FIELD-redacted: any
/// property whose name looks like a secret (password, otp, code, pin, token, nin, bvn, secret, totp)
/// is masked anywhere in the JSON tree — so secrets never reach the logs regardless of endpoint.
/// Non-JSON bodies (e.g. file uploads) are omitted rather than dumped.
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    private static readonly HashSet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "confirmPassword", "currentPassword", "newPassword",
        "pin", "paymentPin", "otp", "code",
        "token", "accessToken", "refreshToken", "refresh_token",
        "secret", "totp", "nin", "bvn"
    };

    public RequestResponseLoggingMiddleware(RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpRequest request = context.Request;

        string? ipAddress = request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();

        // Capture the request body only for JSON (avoids loading huge file uploads into memory).
        string requestBodyLog = "[omitted]";
        if (IsJson(request.ContentType))
        {
            request.EnableBuffering();
            string raw = await new StreamReader(request.Body, leaveOpen: true).ReadToEndAsync();
            request.Body.Position = 0;
            requestBodyLog = Redact(raw);
        }

        Stream originalBodyStream = context.Response.Body;
        using MemoryStream responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            stopwatch.Stop();

            string responseBodyLog = "[omitted]";
            if (IsJson(context.Response.ContentType))
            {
                context.Response.Body.Seek(0, SeekOrigin.Begin);
                string raw = await new StreamReader(context.Response.Body, leaveOpen: true).ReadToEndAsync();
                responseBodyLog = Redact(raw);
            }

            context.Response.Body.Seek(0, SeekOrigin.Begin);

            string? userId = context.User?.FindFirst("user_id")?.Value;
            string? schoolId = context.User?.FindFirst("school_id")?.Value;
            string? userType = context.User?.FindFirst("user_type")?.Value;

            _logger.LogInformation("API LOG: {@LogData}", new
            {
                Timestamp = DateTime.UtcNow,
                Path = request.Path.ToString(),
                request.Method,
                UserId = userId ?? "anonymous",
                SchoolId = schoolId,
                UserType = userType,
                IpAddress = ipAddress,
                RequestBody = requestBodyLog,
                ResponseBody = responseBodyLog,
                context.Response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds
            });
        }
        finally
        {
            context.Response.Body = originalBodyStream;
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }

    private static bool IsJson(string? contentType)
    {
        return contentType is not null
            && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Re-serializes the JSON with sensitive field values masked. Non-JSON → omitted.</summary>
    private static string Redact(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                WriteRedacted(document.RootElement, writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return "[redacted non-json body]";
        }
    }

    private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (SensitiveFields.Contains(property.Name))
                    {
                        writer.WriteString(property.Name, "***");
                    }
                    else
                    {
                        writer.WritePropertyName(property.Name);
                        WriteRedacted(property.Value, writer);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteRedacted(item, writer);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
