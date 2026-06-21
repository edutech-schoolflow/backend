namespace EduTech.Shared.Storage;

/// <summary>
/// Stores an uploaded file and returns its public URL. Backed by S3 in production; a local-disk
/// implementation stands in for dev so the app builds/tests/runs without cloud credentials.
/// </summary>
public interface IFileStorage
{
    /// <summary><paramref name="key"/> is the object path (e.g. <c>kyc/{schoolId}/registration_cert.pdf</c>).</summary>
    Task<string> UploadAsync(Stream content, string key, string contentType,
        CancellationToken cancellationToken = default);
}
