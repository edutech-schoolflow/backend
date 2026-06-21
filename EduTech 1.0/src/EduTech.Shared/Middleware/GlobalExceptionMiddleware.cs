using System.Text.Json;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Middleware;

public class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionMiddleware(RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger, IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppErrorException ex)
        {
            // Expected business condition — log the real reason + code + path, but NOT a stack trace
            // (stack traces are for genuine bugs). LogReason is what we know; the client sees only the
            // vague public message.
            _logger.LogWarning("Handled error from {Ip} [code {Code}] {Method} {Path}: {Reason}",
                ClientIp(context), ex.ErrorCode, context.Request.Method, context.Request.Path,
                ex.LogReason ?? ex.Message);

            ApiError error = BuildError(context, ex.StatusCode, ex.Message, ex.ErrorCode);
            error.ValidationErrors = ex.ValidationErrors?.ToList();
            await WriteAsync(context, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception from {Ip}", ClientIp(context));

            ApiError error = BuildError(context, StatusCodes.Status500InternalServerError,
                "An internal server error occurred. Please try again later.", ErrorCodes.Unknown);

            // Diagnostics only in Development.
            if (_environment.IsDevelopment())
            {
                error.Details = ex.Message;
                error.StackTrace = ex.StackTrace;
            }

            await WriteAsync(context, error);
        }
    }

    private static ApiError BuildError(HttpContext context, int statusCode, string message, int? errorCode)
    {
        return new ApiError
        {
            StatusCode = statusCode,
            Message = message,
            ErrorCode = errorCode,
            Path = context.Request.Path,
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task WriteAsync(HttpContext context, ApiError error)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = error.StatusCode;

        JsonSerializerOptions options = _environment.IsDevelopment()
            ? new JsonSerializerOptions(JsonOptions) { WriteIndented = true }
            : JsonOptions;

        await context.Response.WriteAsync(JsonSerializer.Serialize(error, options));
    }

    private static string? ClientIp(HttpContext context)
    {
        return context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();
    }
}
