using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Decision aggregate (EDD-014 Slice 6): approved/conditional/waitlisted/rejected/withdrawn;
/// a conditional decision must state conditions; only approved/conditional can produce an Offer.
/// </summary>
public class DecisionTests
{
    private static Decision New(DecisionOutcome outcome, string? conditions = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), outcome, conditions, null, null, DateTime.UtcNow);

    [Fact]
    public void Conditional_WithoutConditions_Throws() =>
        Assert.Throws<AppErrorException>(() => New(DecisionOutcome.Conditional));

    [Fact]
    public void Conditional_WithConditions_Ok()
    {
        Decision d = New(DecisionOutcome.Conditional, "Pass the resit");
        Assert.Equal("Pass the resit", d.Conditions);
    }

    [Theory]
    [InlineData(DecisionOutcome.Approved, true)]
    [InlineData(DecisionOutcome.Conditional, true)]
    [InlineData(DecisionOutcome.Waitlisted, false)]
    [InlineData(DecisionOutcome.Rejected, false)]
    [InlineData(DecisionOutcome.Withdrawn, false)]
    public void CanProduceOffer_OnlyApprovedOrConditional(DecisionOutcome outcome, bool expected)
    {
        Decision d = outcome == DecisionOutcome.Conditional ? New(outcome, "x") : New(outcome);
        Assert.Equal(expected, d.CanProduceOffer);
    }
}
