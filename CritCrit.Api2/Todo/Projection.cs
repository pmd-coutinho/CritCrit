using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace CritCrit.Api2.Todo;

public class Projection : SingleStreamProjection<Domain.Todo, Guid>
{
    public void Apply(Events.TodoCreated @event, Domain.Todo todo)
    {
        todo.Id = @event.Id;
        todo.Name = @event.Name;
        todo.Description = @event.Description;
        todo.CreatedAt = DateTime.UtcNow;
    }
    
    public void Apply(Events.TodoCompleted @event, Domain.Todo todo)
    {
        todo.IsComplete = true;
        todo.CompletedAt = DateTime.UtcNow;
    }
}