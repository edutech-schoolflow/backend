using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Storage;

/// <summary>Registers <see cref="IFileStorage"/>: real S3 when <c>Aws:S3:Bucket</c> is configured,
/// otherwise the local-disk dev fallback.</summary>
public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Explicit opt-in so the documented dummy S3 creds don't accidentally activate (broken) S3 in
        // dev. Flip Aws:S3:Enabled=true with real Bucket/keys for production.
        bool s3Enabled = configuration.GetValue("Aws:S3:Enabled", false)
            && !string.IsNullOrWhiteSpace(configuration["Aws:S3:Bucket"]);

        if (s3Enabled)
        {
            services.AddSingleton<IFileStorage, S3FileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        }

        return services;
    }
}
