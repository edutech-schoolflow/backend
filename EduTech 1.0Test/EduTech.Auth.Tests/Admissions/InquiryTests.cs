using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Inquiry aggregate (EDD-014 Slice 2): new → contacted → visit_booked → (converted | closed);
/// converted/closed are terminal. Requires a prospective name + a contact phone.
/// </summary>
public class InquiryTests
{
    private static Inquiry New(InquiryStatus status = InquiryStatus.New) =>
        new(Guid.NewGuid(), Guid.NewGuid(), cycleId: null, "Ada", "Mrs Obi", "+2348000000001", null, null,
            status, convertedApplicationId: null, DateTime.UtcNow);

    [Fact]
    public void BlankProspectiveName_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new Inquiry(Guid.NewGuid(), Guid.NewGuid(), null, " ", null, "+234800", null, null,
                InquiryStatus.New, null, DateTime.UtcNow));

    [Fact]
    public void BlankPhone_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new Inquiry(Guid.NewGuid(), Guid.NewGuid(), null, "Ada", null, "  ", null, null,
                InquiryStatus.New, null, DateTime.UtcNow));

    [Fact]
    public void Lifecycle_ContactVisitConvert()
    {
        Inquiry i = New();

        i.MarkContacted();
        Assert.Equal(InquiryStatus.Contacted, i.Status);

        DateTime visit = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        i.BookVisit(visit);
        Assert.Equal(InquiryStatus.VisitBooked, i.Status);
        Assert.Equal(visit, i.VisitAt);

        Guid appId = Guid.NewGuid();
        i.Convert(appId);
        Assert.Equal(InquiryStatus.Converted, i.Status);
        Assert.Equal(appId, i.ConvertedApplicationId);
    }

    [Fact]
    public void Converted_RejectsFurtherMutation()
    {
        Inquiry i = New(InquiryStatus.Converted);
        AppErrorException ex = Assert.Throws<AppErrorException>(() => i.MarkContacted());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Closed_RejectsBookVisit()
    {
        Inquiry i = New(InquiryStatus.Closed);
        Assert.Throws<AppErrorException>(() => i.BookVisit(null));
    }
}
