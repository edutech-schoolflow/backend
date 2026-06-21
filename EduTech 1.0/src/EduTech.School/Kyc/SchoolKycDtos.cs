using Microsoft.AspNetCore.Http;

namespace EduTech.School.Kyc;

/// <summary>Multipart KYC submission: school details + proprietor + bank + the 5 typed documents.</summary>
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

    // Proprietor identity.
    public string ProprietorName { get; init; } = string.Empty;
    public string ProprietorIdType { get; init; } = string.Empty;
    public string ProprietorIdNumber { get; init; } = string.Empty;
    public string ProprietorPhone { get; init; } = string.Empty;
    public string ProprietorEmail { get; init; } = string.Empty;
    public string ProprietorNin { get; init; } = string.Empty;   // 11 digits — encrypted, never returned
    public string ProprietorBvn { get; init; } = string.Empty;   // 11 digits — encrypted, never returned

    // Settlement bank account.
    public string BankName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;   // current | savings

    // Documents.
    public IFormFile? RegistrationCert { get; init; }
    public IFormFile? OperatingLicence { get; init; }
    public IFormFile? ProofOfAddress { get; init; }
    public IFormFile? ProprietorIdFront { get; init; }
    public IFormFile? ProprietorIdBack { get; init; }
}

/// <summary>KYC submission state returned to the owner (mirrors the frontend KycSubmission).</summary>
public sealed class KycSubmissionResponse
{
    public required Guid SchoolId { get; init; }
    public required string Status { get; init; }   // not_submitted | under_review | approved | rejected
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? SchoolMessage { get; init; }

    public string? ProprietorName { get; init; }
    public string? ProprietorIdType { get; init; }
    public string? ProprietorIdNumber { get; init; }
    public string? ProprietorPhone { get; init; }
    public string? ProprietorEmail { get; init; }

    public string? BankName { get; init; }
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public string? AccountType { get; init; }

    public required IReadOnlyList<KycDocumentResponse> Documents { get; init; }
}

public sealed class KycDocumentResponse
{
    public required string Type { get; init; }
    public required string Url { get; init; }
    public required string Status { get; init; }
    public string? Notes { get; init; }
}
