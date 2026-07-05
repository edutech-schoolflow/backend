namespace EduTech.Shared.Events;


public interface IDomainEvent
{
    
    DateTime OccurredAt { get; }
}
