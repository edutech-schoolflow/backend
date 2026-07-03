using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Identity;

/// <summary>
/// Real Dojah NIN/BVN lookup. Config: <c>Dojah:BaseUrl</c> (sandbox https://sandbox.dojah.io /
/// production https://api.dojah.io), <c>Dojah:AppId</c>, <c>Dojah:ApiKey</c> (the secret key — sent
/// raw in Authorization, NOT "Bearer"). A successful <c>entity</c> response means the number is valid.
/// Endpoints + header style mirror the verified Dojah docs (and KEDCO's working integration).
/// </summary>
public sealed class DojahIdentityVerifier : IIdentityVerifier
{
    private readonly HttpClient _http;
    private readonly ILogger<DojahIdentityVerifier> _logger;
    private readonly string _baseUrl;
    private readonly string _appId;
    private readonly string _apiKey;

    public DojahIdentityVerifier(HttpClient http, IConfiguration configuration, ILogger<DojahIdentityVerifier> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (configuration["Dojah:BaseUrl"] ?? "https://api.dojah.io").TrimEnd('/');
        _appId = configuration["Dojah:AppId"] ?? string.Empty;
        _apiKey = configuration["Dojah:ApiKey"] ?? string.Empty;
    }

    public Task<IdentityVerificationResult> VerifyNinAsync(string nin, string expectedName,
        CancellationToken cancellationToken = default)
        => LookupAsync($"/api/v1/kyc/nin?nin={Uri.EscapeDataString(nin)}", "NIN", expectedName, cancellationToken);

    public Task<IdentityVerificationResult> VerifyBvnAsync(string bvn, string expectedName,
        CancellationToken cancellationToken = default)
        => LookupAsync($"/api/v1/kyc/bvn/full?bvn={Uri.EscapeDataString(bvn)}", "BVN", expectedName, cancellationToken);

    private async Task<IdentityVerificationResult> LookupAsync(string path, string label, string expectedName,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
            request.Headers.Add("AppId", _appId);
            request.Headers.Add("Authorization", _apiKey);   // raw secret key, NOT "Bearer <key>"
            request.Headers.Add("Accept", "application/json");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return IdentityVerificationResult.Fail($"{label} not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Dojah {Label} verification returned {Status}.", label, (int)response.StatusCode);
                return IdentityVerificationResult.Fail($"Could not verify the {label}. Please try again.");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(body);

            // Dojah returns { "entity": { ... } } for a valid number.
            if (!doc.RootElement.TryGetProperty("entity", out JsonElement entity)
                || entity.ValueKind != JsonValueKind.Object)
            {
                return IdentityVerificationResult.Fail($"{label} not found.");
            }

            // The number is real — now confirm it belongs to the person submitting it.
            string? first = GetString(entity, "first_name");
            string? last = GetString(entity, "last_name");
            if (!NameMatcher.Matches(expectedName, first, last))
            {
                _logger.LogWarning("Dojah {Label} name mismatch.", label);
                return IdentityVerificationResult.Fail($"The {label} doesn't match the name on your account.");
            }

            return IdentityVerificationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dojah {Label} verification failed.", label);
            return IdentityVerificationResult.Fail("Identity verification is temporarily unavailable.");
        }
    }

    private static string? GetString(JsonElement entity, string property)
        => entity.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
