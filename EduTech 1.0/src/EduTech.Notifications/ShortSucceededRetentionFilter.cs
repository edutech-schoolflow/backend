using Hangfire.States;
using Hangfire.Storage;

namespace EduTech.Notifications;

/// <summary>
/// Shortens how long SUCCEEDED jobs linger in storage (Hangfire default 24h → 1h). OTP texts ride in
/// job args, so delivered ones shouldn't sit around. Failed jobs keep the default retention so they
/// stay inspectable for debugging.
/// </summary>
public sealed class ShortSucceededRetentionFilter : IApplyStateFilter
{
    private static readonly TimeSpan SucceededRetention = TimeSpan.FromHours(1);

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState.Name == SucceededState.StateName)
        {
            context.JobExpirationTimeout = SucceededRetention;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
