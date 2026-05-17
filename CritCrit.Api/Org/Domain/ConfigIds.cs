namespace CritCrit.Api.Org.Domain;

public readonly record struct ConfigSchemaId(Guid Value)
{
    public static ConfigSchemaId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ConfigDraftId(Guid Value)
{
    public static ConfigDraftId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ConfigAssignmentId(Guid Value)
{
    public static ConfigAssignmentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
