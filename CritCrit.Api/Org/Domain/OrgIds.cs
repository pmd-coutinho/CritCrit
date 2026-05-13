namespace CritCrit.Api.Org.Domain;

public readonly record struct OrgNodeId(Guid Value)
{
    public static OrgNodeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SubjectId(Guid Value)
{
    public static SubjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
