using EduTech.Shared.Events;
using Microsoft.Extensions.Logging;

namespace EduTech.Students.Academics.Transition;

/// <summary>
/// Observer: when a school goes live (KYC approved + made visible), give it a ready academic calendar
/// immediately rather than waiting for the nightly sweep — so attendance, fees and terms work from day
/// one. Idempotent via the provisioner; failures are isolated by the publisher and the daily sweep is
/// the safety net, so a hiccup here never blocks activation.
/// </summary>
internal sealed class ProvisionCalendarOnSchoolActivated : IDomainEventHandler<SchoolActivatedEvent>
{
    private readonly ISchoolCalendarProvisioner _provisioner;
    private readonly ILogger<ProvisionCalendarOnSchoolActivated> _logger;

    public ProvisionCalendarOnSchoolActivated(ISchoolCalendarProvisioner provisioner,
        ILogger<ProvisionCalendarOnSchoolActivated> logger)
    {
        _provisioner = provisioner;
        _logger = logger;
    }

    public async Task HandleAsync(SchoolActivatedEvent domainEvent, CancellationToken cancellationToken)
    {
        // School days are West Africa Time (UTC+1, no DST).
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        bool provisioned = await _provisioner.ProvisionIfMissingAsync(domainEvent.SchoolId, today, cancellationToken);
        if (provisioned)
        {
            _logger.LogInformation("Provisioned academic calendar for newly-activated school {SchoolId}.",
                domainEvent.SchoolId);
        }
    }
}
