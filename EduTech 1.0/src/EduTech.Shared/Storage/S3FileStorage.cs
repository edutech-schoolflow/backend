using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace EduTech.Shared.Storage;

/// <summary>
/// Real S3 storage (production). Config keys: <c>Aws:S3:Bucket</c>, <c>Aws:S3:Region</c>, and
/// optionally <c>Aws:S3:AccessKey</c>/<c>SecretKey</c> (omit to use the ambient IAM role) and
/// <c>Aws:S3:PublicBaseUrl</c> (e.g. a CloudFront domain).
/// </summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _publicBaseUrl;

    public S3FileStorage(IConfiguration configuration)
    {
        _bucket = configuration["Aws:S3:Bucket"]
            ?? throw new InvalidOperationException("Aws:S3:Bucket is not configured.");
        string region = configuration["Aws:S3:Region"] ?? "us-east-1";
        string? accessKey = configuration["Aws:S3:AccessKey"];
        string? secretKey = configuration["Aws:S3:SecretKey"];
        _publicBaseUrl = configuration["Aws:S3:PublicBaseUrl"]
            ?? $"https://{_bucket}.s3.{region}.amazonaws.com";

        RegionEndpoint regionEndpoint = RegionEndpoint.GetBySystemName(region);
        _s3 = string.IsNullOrWhiteSpace(accessKey)
            ? new AmazonS3Client(regionEndpoint)
            : new AmazonS3Client(accessKey, secretKey, regionEndpoint);
    }

    public async Task<string> UploadAsync(Stream content, string key, string contentType,
        CancellationToken cancellationToken = default)
    {
        PutObjectRequest request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        return $"{_publicBaseUrl.TrimEnd('/')}/{key}";
    }
}
