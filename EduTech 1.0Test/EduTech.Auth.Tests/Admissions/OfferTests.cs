using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Offer aggregate (EDD-014 Slice 7): issued → (accepted | declined | withdrawn | lapsed); only
/// an outstanding (issued) offer may respond; the rest are terminal.
/// </summary>
public class OfferTests
{
    private static Offer New(OfferStatus status = OfferStatus.Issued) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null, "2027/2028", "Termly", null, null,
            DateTime.UtcNow.AddDays(14), status, null, DateTime.UtcNow);

    [Fact]
    public void Accept_FromIssued_SetsAcceptedWithTimestamp()
    {
        Offer o = New();
        o.Accept(new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(OfferStatus.Accepted, o.Status);
        Assert.NotNull(o.RespondedAt);
    }

    [Fact]
    public void Decline_FromIssued_SetsDeclined()
    {
        Offer o = New();
        o.Decline(DateTime.UtcNow);
        Assert.Equal(OfferStatus.Declined, o.Status);
    }

    [Fact]
    public void Withdraw_FromIssued_SetsWithdrawn()
    {
        Offer o = New();
        o.Withdraw();
        Assert.Equal(OfferStatus.Withdrawn, o.Status);
    }

    [Fact]
    public void Lapse_FromIssued_SetsLapsed()
    {
        Offer o = New();
        o.Lapse();
        Assert.Equal(OfferStatus.Lapsed, o.Status);
    }

    [Theory]
    [InlineData(OfferStatus.Accepted)]
    [InlineData(OfferStatus.Declined)]
    [InlineData(OfferStatus.Withdrawn)]
    [InlineData(OfferStatus.Lapsed)]
    public void Respond_WhenTerminal_Throws(OfferStatus status)
    {
        Offer o = New(status);
        AppErrorException ex = Assert.Throws<AppErrorException>(() => o.Accept(DateTime.UtcNow));
        Assert.Equal(409, ex.StatusCode);
    }
}
