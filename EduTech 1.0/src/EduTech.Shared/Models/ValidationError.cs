namespace EduTech.Shared.Models;

/// <summary>A single field-level validation failure (for 400 responses).</summary>
public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? AttemptedValue { get; set; }
}
