using EduTech.Shared.Exceptions;
using EduTech.Shared.Lifecycle;

namespace EduTech.Auth.Tests.Lifecycle;

public class StateTransitionsTests
{
    private enum Sample { Draft, Issued, Paid }

    private static readonly StateTransitions<Sample> Rules = new(
        new Dictionary<Sample, IReadOnlySet<Sample>>
        {
            [Sample.Draft]  = new HashSet<Sample> { Sample.Issued },
            [Sample.Issued] = new HashSet<Sample> { Sample.Paid },
            [Sample.Paid]   = new HashSet<Sample>(),   // terminal
        },
        terminal: new HashSet<Sample> { Sample.Paid });

    [Fact]
    public void Require_LegalTransition_DoesNotThrow()
    {
        Rules.Require(Sample.Draft, Sample.Issued);
        Rules.Require(Sample.Issued, Sample.Paid);
    }

    [Fact]
    public void Require_IllegalTransition_Throws409()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() => Rules.Require(Sample.Paid, Sample.Draft));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Require_SameState_IsNoOp()
    {
        Rules.Require(Sample.Paid, Sample.Paid);   // idempotent — must not throw
    }

    [Fact]
    public void Terminal_And_CanTransition_Reported()
    {
        Assert.True(Rules.IsTerminal(Sample.Paid));
        Assert.False(Rules.IsTerminal(Sample.Draft));
        Assert.True(Rules.CanTransition(Sample.Draft, Sample.Issued));
        Assert.False(Rules.CanTransition(Sample.Draft, Sample.Paid));
    }
}
