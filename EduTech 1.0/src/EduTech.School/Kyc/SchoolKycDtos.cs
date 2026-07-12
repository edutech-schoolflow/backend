using Microsoft.AspNetCore.Http;

namespace EduTech.School.Kyc;

/// <summary>
/// Multipart KYC submission: school details + digitally-verified proprietor (NIN/BVN) + bank + the
/// CAC document. Proprietor ID documents are not collected — NIN/BVN verification replaces them.
/// </summary>
public sealed class SubmitKycRequest
{
    // School profile (stored on the schools row).
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;   // nursery | primary | secondary | combined
    public string Address { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    // School GPS (proof of address) — persisted to schools.location_lat/location_lng.
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }

    // Proprietor identity — verified digitally (NIN + BVN), name-matched to the registry.
    public string ProprietorFirstName { get; init; } = string.Empty;
    public string? ProprietorMiddleName { get; init; }
    public string ProprietorLastName { get; init; } = string.Empty;
    public string ProprietorNin { get; init; } = string.Empty;   // 11 digits — encrypted, never returned
    public string ProprietorBvn { get; init; } = string.Empty;   // 11 digits — encrypted, never returned

    // Settlement bank account.
    public string BankName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    // Business registration (CAC) — the name as registered, plus the certificate.
    public string? BusinessName { get; init; }
    public IFormFile? RegistrationCert { get; init; }
}

/// <summary>
/// KYC status returned to the owner — status metadata only. The submitted details (proprietor name,
/// NIN/BVN, bank account, uploaded documents) are NEVER echoed back: the frontend doesn't use them
/// once submitted, and they are sensitive. Admin review reads them through its own internal path.
/// </summary>
public sealed class KycSubmissionResponse
{
    public required string Status { get; init; }   // not_submitted | under_review | approved | rejected
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? SchoolMessage { get; init; }     // e.g. the rejection reason

    // School identity captured at creation — prefilled and locked in the KYC form (not editable there).
    public string? SchoolName { get; init; }
    public string? SchoolType { get; init; }
    public string? SchoolState { get; init; }

    // Proprietor name from the owning identity — prefilled and locked in the KYC form.
    public string? ProprietorFirstName { get; init; }
    public string? ProprietorMiddleName { get; init; }
    public string? ProprietorLastName { get; init; }
}
