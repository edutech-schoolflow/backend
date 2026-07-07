using EduTech.Shared.Events;
using Microsoft.Extensions.Logging;

namespace EduTech.Students.Classes;

/// <summary>
/// Observer: when a school goes live, give it the standard 6-3-3 class ladder for its type (Nursery /
/// Primary 1–6 / JSS 1–3 / SSS 1–3) so it isn't setting classes up by hand. Idempotent via the
/// provisioner; failures are isolated by the publisher, so a hiccup never blocks activation. Runs
/// alongside the calendar provisioning on the same event.
/// </summary>
internal sealed class ProvisionClassesOnSchoolActivated : IDomainEventHandler<SchoolActivatedEvent>
{
    private readonly ISchoolClassProvisioner _provisioner;
    private readonly ILogger<ProvisionClassesOnSchoolActivated> _logger;

    public ProvisionClassesOnSchoolActivated(ISchoolClassProvisioner provisioner,
        ILogger<ProvisionClassesOnSchoolActivated> logger)
    {
        _provisioner = provisioner;
        _logger = logger;
    }

    public async Task HandleAsync(SchoolActivatedEvent domainEvent, CancellationToken cancellationToken)
    {
        if (await _provisioner.ProvisionIfMissingAsync(domainEvent.SchoolId, cancellationToken))
        {
            _logger.LogInformation("Provisioned standard classes for newly-activated school {SchoolId}.",
                domainEvent.SchoolId);
        }
    }
}
