using System.Text;
using EduTech.Shared.Storage;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace EduTech.Auth.Tests.Storage;

/// <summary>The dev fallback writes the file under wwwroot/uploads and returns its served URL.</summary>
public class LocalDiskFileStorageTests
{
    [Fact]
    public async Task Upload_WritesFile_AndReturnsServedUrl()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "edutech-test-" + Guid.NewGuid().ToString("N"));
        Mock<IWebHostEnvironment> environment = new();
        environment.SetupGet(e => e.WebRootPath).Returns(tempRoot);
        environment.SetupGet(e => e.ContentRootPath).Returns(tempRoot);

        try
        {
            LocalDiskFileStorage storage = new(environment.Object);

            using MemoryStream content = new(Encoding.UTF8.GetBytes("hello-kyc"));
            string url = await storage.UploadAsync(content, "kyc/abc/registration_cert.pdf", "application/pdf");

            Assert.Equal("/uploads/kyc/abc/registration_cert.pdf", url);

            string written = Path.Combine(tempRoot, "uploads", "kyc", "abc", "registration_cert.pdf");
            Assert.True(File.Exists(written));
            Assert.Equal("hello-kyc", await File.ReadAllTextAsync(written));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
