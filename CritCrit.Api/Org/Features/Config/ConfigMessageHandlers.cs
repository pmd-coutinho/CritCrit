using Microsoft.Extensions.Logging;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// V1 no-op handler — logs invalidation so trackedHttpCall tests can assert
/// delivery + future cache consumers have a clean seam to slot into.
/// </summary>
public static class ConfigInvalidationHandler
{
    public static Task Handle(ConfigInvalidationRequested message, ILogger<ConfigInvalidationRequested> logger)
    {
        logger.LogInformation(
            "ConfigInvalidationRequested kind={Kind} tenant={Tenant} scope={Scope} schema={Schema} version={Version} keys={Keys}",
            message.Kind,
            message.TenantId,
            message.ScopeOrgNodeId,
            message.SchemaCode,
            message.SchemaVersion,
            message.AffectedKeys.Count);
        return Task.CompletedTask;
    }
}
