using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduTech.Compliance.IdentityVerification;

/// <summary>
/// Real Dojah NIN verification. Config: <c>Dojah:BaseUrl</c> (default https://api.dojah.io),
/// <c>Dojah:AppId</c>, <c>Dojah:ApiKey</c>. Calls the NIN lookup endpoint; a successful entity
/// response means the NIN is valid. (Requires your Dojah credentials — see appsettings.)
/// </summary>
public sealed class DojahNinVerifier : IIdentityVerifier
{
    private readonly HttpClient _http;
    private readonly ILogger<DojahNinVerifier> _logger;

    public DojahNinVerifier(HttpClient http, IConfiguration configuration, ILogger<DojahNinVerifier> logger)
    {
        _http = http;
        _logger = logger;

        string baseUrl = configuration["Dojah:BaseUrl"] ?? "https://api.dojah.io";
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("AppId");
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("AppId", configuration["Dojah:AppId"] ?? string.Empty);
        _http.DefaultRequestHeaders.Add("Authorization", configuration["Dojah:ApiKey"] ?? string.Empty);
    }

    public async Task<NinVerificationResult> VerifyNinAsync(string nin, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync($"api/v1/kyc/nin?nin={nin}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Dojah NIN verification returned {Status}.", (int)response.StatusCode);
                return NinVerificationResult.Fail("Could not verify your NIN. Please check and try again.");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(body);

            // Dojah returns { "entity": { ... } } for a valid NIN.
            bool hasEntity = doc.RootElement.TryGetProperty("entity", out JsonElement entity)
                && entity.ValueKind == JsonValueKind.Object;

            return hasEntity
                ? NinVerificationResult.Ok()
                : NinVerificationResult.Fail("NIN not found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dojah NIN verification failed.");
            return NinVerificationResult.Fail("Identity verification is temporarily unavailable.");
        }
    }
}
