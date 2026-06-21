using Microsoft.AspNetCore.Hosting;

namespace EduTech.Shared.Storage;

/// <summary>
/// Dev fallback (no AWS creds): writes files under <c>wwwroot/uploads</c> and returns a
/// <c>/uploads/{key}</c> URL served by <c>UseStaticFiles</c>. NOT for production.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IWebHostEnvironment environment)
    {
        string webRoot = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        _root = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> UploadAsync(Stream content, string key, string contentType,
        CancellationToken cancellationToken = default)
    {
        string relative = key.Replace('\\', '/');
        string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using FileStream file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);

        return "/uploads/" + relative;
    }
}
