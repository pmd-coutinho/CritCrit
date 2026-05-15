namespace CritCrit.Api.Observability.Logging;

public static partial class ObservabilityLogs
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Failed to persist denied audit event {Action} support_id={SupportId}")]
    public static partial void AuditDeniedWriteFailed(this ILogger logger, Exception exception, string action, string supportId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Invitation workflow failed invitation_id={InvitationId} failure_code={FailureCode} support_id={SupportId}")]
    public static partial void InvitationWorkflowFailed(this ILogger logger, Guid invitationId, string failureCode, string supportId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Invitation email dispatched invitation_id={InvitationId} attempt={Attempt} support_id={SupportId}")]
    public static partial void InvitationEmailDispatched(this ILogger logger, Guid invitationId, int attempt, string supportId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Invitation email send failed invitation_id={InvitationId} attempt={Attempt} final={Final} support_id={SupportId}")]
    public static partial void InvitationEmailSendFailed(this ILogger logger, Exception exception, Guid invitationId, int attempt, bool final, string supportId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Expired access grant tenant_id={TenantId} org_node_id={OrgNodeId} subject_id={SubjectId} support_id={SupportId}")]
    public static partial void AccessGrantExpired(this ILogger logger, Guid tenantId, Guid orgNodeId, Guid subjectId, string supportId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Cleaned redundant access grants tenant_id={TenantId} org_node_id={OrgNodeId} subject_id={SubjectId} count={Count} support_id={SupportId}")]
    public static partial void RedundantAccessGrantsCleaned(this ILogger logger, Guid tenantId, Guid orgNodeId, Guid subjectId, int count, string supportId);
}
