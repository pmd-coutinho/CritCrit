namespace CritCrit.Api2.Todo;

public static class Events
{
    public sealed record TodoCreated(Guid Id, string Name, string Description, DateTime CreatedAt);

    public sealed record TodoCompleted();
}