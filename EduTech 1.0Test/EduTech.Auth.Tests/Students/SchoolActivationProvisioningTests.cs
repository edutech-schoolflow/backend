using EduTech.Shared.Events;
using EduTech.Students.Academics.Transition;
using EduTech.Students.Classes;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EduTech.Auth.Tests.Students;

/// <summary>
/// Integration-style tests for what happens when a school is activated: they wire up the REAL
/// <see cref="IDomainEventPublisher"/> and BOTH real handlers through a DI container (only the two
/// provisioners are mocked), so they verify the cross-module Observer fan-out that unit tests can't —
/// one <see cref="SchoolActivatedEvent"/> must drive both calendar and class provisioning, and a failure
/// in one handler must not stop the other or the activation.
/// </summary>
public class SchoolActivationProvisioningTests
{
    private static (ServiceProvider Sp, Mock<ISchoolCalendarProvisioner> Calendar, Mock<ISchoolClassProvisioner> Classes)
        Build(Action<Mock<ISchoolCalendarProvisioner>, Mock<ISchoolClassProvisioner>>? arrange = null)
    {
        Mock<ISchoolCalendarProvisioner> calendar = new();
        Mock<ISchoolClassProvisioner> classes = new();
        arrange?.Invoke(calendar, classes);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDomainEvents(); // the real publisher
        services.AddScoped(_ => calendar.Object);
        services.AddScoped(_ => classes.Object);
        // Exactly how the app registers them (StudentsServiceCollectionExtensions).
        services.AddScoped<IDomainEventHandler<SchoolActivatedEvent>, ProvisionCalendarOnSchoolActivated>();
        services.AddScoped<IDomainEventHandler<SchoolActivatedEvent>, ProvisionClassesOnSchoolActivated>();

        return (services.BuildServiceProvider(), calendar, classes);
    }

    [Fact]
    public async Task Activation_FansOutToBothCalendarAndClassProvisioning()
    {
        Guid school = Guid.NewGuid();
        (ServiceProvider sp, Mock<ISchoolCalendarProvisioner> calendar, Mock<ISchoolClassProvisioner> classes) = Build();

        using IServiceScope scope = sp.CreateScope();
        IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

        await publisher.PublishAsync(new SchoolActivatedEvent(school), CancellationToken.None);

        calendar.Verify(p => p.ProvisionIfMissingAsync(school, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        classes.Verify(p => p.ProvisionIfMissingAsync(school, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activation_OneHandlerThrows_OtherStillRunsAndPublishDoesNotThrow()
    {
        Guid school = Guid.NewGuid();
        (ServiceProvider sp, _, Mock<ISchoolClassProvisioner> classes) = Build((calendar, _) =>
            calendar.Setup(p => p.ProvisionIfMissingAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("calendar provisioning blew up")));

        using IServiceScope scope = sp.CreateScope();
        IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

        // The publisher isolates handler failures — activation is never undone by a provisioning hiccup.
        await publisher.PublishAsync(new SchoolActivatedEvent(school), CancellationToken.None);

        // The class provisioning still ran despite the calendar handler throwing.
        classes.Verify(p => p.ProvisionIfMissingAsync(school, It.IsAny<CancellationToken>()), Times.Once);
    }
}
