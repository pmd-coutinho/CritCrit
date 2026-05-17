namespace CritCrit.Api.Org.Features.Config;

public static class ConfigAuditActions
{
    public const string SchemaCreated = "config.schema.created";
    public const string SchemaArchived = "config.schema.archived";
    public const string SchemaRestored = "config.schema.restored";
    public const string DraftCreated = "config.schema.draft.created";
    public const string DraftUpdated = "config.schema.draft.updated";
    public const string DraftArchived = "config.schema.draft.archived";
    public const string VersionPublished = "config.schema.version.published";
    public const string AssignmentCreated = "config.assignment.created";
    public const string AssignmentArchived = "config.assignment.archived";
    public const string AssignmentRestored = "config.assignment.restored";
    public const string AssignmentUpgraded = "config.assignment.upgraded";
    public const string ValuesPatched = "config.values.patched";
}
