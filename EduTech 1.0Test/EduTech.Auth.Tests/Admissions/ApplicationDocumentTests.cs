using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The ApplicationDocument aggregate (EDD-014 Slice 4): pending → uploaded → (verified | rejected);
/// a rejected document can be re-uploaded; verify/reject require an uploaded document.
/// </summary>
public class ApplicationDocumentTests
{
    private static ApplicationDocument New(DocumentStatus status = DocumentStatus.Pending, string? url = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "birth_certificate", required: true, status, url, null, null, DateTime.UtcNow);

    [Fact]
    public void BlankDocType_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new ApplicationDocument(Guid.NewGuid(), Guid.NewGuid(), " ", true, DocumentStatus.Pending,
                null, null, null, DateTime.UtcNow));

    [Fact]
    public void Upload_SetsUploadedWithUrl()
    {
        ApplicationDocument d = New();
        d.Upload("https://cdn/x.pdf");
        Assert.Equal(DocumentStatus.Uploaded, d.Status);
        Assert.Equal("https://cdn/x.pdf", d.FileUrl);
    }

    [Fact]
    public void Upload_BlankUrl_Throws() => Assert.Throws<AppErrorException>(() => New().Upload("  "));

    [Fact]
    public void Verify_FromUploaded_Succeeds()
    {
        ApplicationDocument d = New(DocumentStatus.Uploaded, "https://cdn/x.pdf");
        Guid who = Guid.NewGuid();
        d.Verify(who);
        Assert.Equal(DocumentStatus.Verified, d.Status);
    }

    [Fact]
    public void Verify_FromPending_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() => New().Verify(null));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Reject_RequiresReason_AndSetsNotes()
    {
        ApplicationDocument d = New(DocumentStatus.Uploaded, "https://cdn/x.pdf");
        Assert.Throws<AppErrorException>(() => d.Reject(" "));

        d.Reject("Blurry scan");
        Assert.Equal(DocumentStatus.Rejected, d.Status);
        Assert.Equal("Blurry scan", d.Notes);
    }

    [Fact]
    public void Rejected_CanReupload()
    {
        ApplicationDocument d = New(DocumentStatus.Rejected, "https://cdn/old.pdf");
        d.Upload("https://cdn/new.pdf");
        Assert.Equal(DocumentStatus.Uploaded, d.Status);
        Assert.Null(d.Notes); // rejection note cleared
    }

    [Fact]
    public void Verified_CannotReupload() =>
        Assert.Throws<AppErrorException>(() => New(DocumentStatus.Verified, "https://cdn/x.pdf").Upload("https://cdn/y.pdf"));
}
