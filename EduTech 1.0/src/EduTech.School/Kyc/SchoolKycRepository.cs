using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.School.Kyc;

internal interface ISchoolKycRepository
{
    Task<string?> GetKycStatusAsync(Guid schoolId, CancellationToken cancellationToken);

    Task UpdateSchoolDetailsAsync(Guid schoolId, SchoolDetails details,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpsertSubmissionAsync(Guid schoolId, KycSubmissionRow row, string encryptedNin, string encryptedBvn,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpsertDocumentAsync(Guid schoolId, string type, string url,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task SetKycStatusAsync(Guid schoolId, string status,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task<KycSubmissionRow?> GetSubmissionAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KycDocumentRow>> GetDocumentsAsync(Guid schoolId, CancellationToken cancellationToken);
}

internal sealed class SchoolDetails
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Address { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string Phone { get; init; }
    public required string Email { get; init; }
}

internal sealed class KycSubmissionRow
{
    public string? ProprietorName { get; init; }
    public string? BankName { get; init; }
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? SchoolMessage { get; init; }
}

internal sealed class KycDocumentRow
{
    public string Type { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

internal sealed class SchoolKycRepository : BaseRepository, ISchoolKycRepository
{
    public SchoolKycRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<string?> GetKycStatusAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>(
            "SELECT kyc_status FROM schools WHERE id = @Id", new { Id = schoolId }, cancellationToken);
    }

    public Task UpdateSchoolDetailsAsync(Guid schoolId, SchoolDetails details,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE schools
            SET name = @Name, type = @Type, address = @Address, city = @City, state = @State,
                phone = @Phone, email = @Email, updated_at = NOW()
            WHERE id = @Id
            """,
            new
            {
                Id = schoolId, details.Name, details.Type, details.Address,
                details.City, details.State, details.Phone, details.Email
            },
            cancellationToken, transaction);
    }

    public Task UpsertSubmissionAsync(Guid schoolId, KycSubmissionRow row, string encryptedNin,
        string encryptedBvn, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO school_kyc
                (school_id, proprietor_name, proprietor_nin, proprietor_bvn, bank_name, account_number,
                 account_name, submitted_at)
            VALUES
                (@SchoolId, @ProprietorName, @EncryptedNin, @EncryptedBvn, @BankName, @AccountNumber,
                 @AccountName, NOW())
            ON CONFLICT (school_id) DO UPDATE SET
                proprietor_name = EXCLUDED.proprietor_name,
                proprietor_nin = EXCLUDED.proprietor_nin,
                proprietor_bvn = EXCLUDED.proprietor_bvn,
                bank_name = EXCLUDED.bank_name,
                account_number = EXCLUDED.account_number,
                account_name = EXCLUDED.account_name,
                submitted_at = NOW(), reviewed_at = NULL, admin_notes = NULL, school_message = NULL,
                updated_at = NOW()
            """,
            new
            {
                SchoolId = schoolId, row.ProprietorName, EncryptedNin = encryptedNin, EncryptedBvn = encryptedBvn,
                row.BankName, row.AccountNumber, row.AccountName
            },
            cancellationToken, transaction);
    }

    public Task UpsertDocumentAsync(Guid schoolId, string type, string url,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO school_kyc_documents (school_id, type, url, status)
            VALUES (@SchoolId, @Type, @Url, 'pending')
            ON CONFLICT (school_id, type) DO UPDATE SET
                url = EXCLUDED.url, status = 'pending', notes = NULL, uploaded_at = NOW()
            """,
            new { SchoolId = schoolId, Type = type, Url = url }, cancellationToken, transaction);
    }

    public Task SetKycStatusAsync(Guid schoolId, string status,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE schools SET kyc_status = @Status, updated_at = NOW() WHERE id = @Id",
            new { Id = schoolId, Status = status }, cancellationToken, transaction);
    }

    public Task<KycSubmissionRow?> GetSubmissionAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<KycSubmissionRow>(
            """
            SELECT proprietor_name AS ProprietorName, bank_name AS BankName,
                   account_number AS AccountNumber, account_name AS AccountName,
                   submitted_at AS SubmittedAt, reviewed_at AS ReviewedAt, school_message AS SchoolMessage
            FROM school_kyc WHERE school_id = @Id
            """,
            new { Id = schoolId }, cancellationToken);
    }

    public Task<IReadOnlyList<KycDocumentRow>> GetDocumentsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QueryAsync<KycDocumentRow>(
            "SELECT type AS Type, url AS Url, status AS Status, notes AS Notes " +
            "FROM school_kyc_documents WHERE school_id = @Id ORDER BY type",
            new { Id = schoolId }, cancellationToken);
    }
}
