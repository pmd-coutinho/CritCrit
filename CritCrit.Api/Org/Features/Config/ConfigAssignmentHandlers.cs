using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Config;

public static class ConfigAssignmentHandlers
{
    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments")]
    public static async Task<IReadOnlyList<ConfigAssignmentResponse>> ListAssignments(
        string brandId,
        string nodeId,
        bool? includeArchived,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseOrgNode(nodeId, out var orgNodeId, out _))
            throw new DomainException("Invalid org node ID.");

        await using var session = SessionFactory.TenantSession(store, tenant);
        var node = await OrgValidation.LoadNodeAsync(session, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted)
            throw new DomainException("Org node not found.", 404);

        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(session, actor, node, OrgRole.Viewer, ct);

        var query = session.Query<ConfigAssignmentReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value && x.RootOrgNodeId == orgNodeId.Value);

        var rows = includeArchived == true
            ? await query.OrderBy(x => x.SchemaCode).ToListAsync(ct)
            : await query.Where(x => !x.Archived).OrderBy(x => x.SchemaCode).ToListAsync(ct);

        return rows.Select(ToResponse).ToArray();
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments")]
    public static async Task<IResult> CreateAssignment(
        string brandId,
        string nodeId,
        AssignConfigSchemaRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        IMartenOutbox outbox,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        ConfigCode.EnsureValidSchemaCode(request.SchemaCode);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var orgNodeId, out _))
            throw new DomainException("Invalid org node ID.");

        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(tenantSession, actor);

        var node = await OrgValidation.LoadNodeAsync(tenantSession, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Cannot assign config to an inactive or missing org node.");

        // Schema + version must be live on the platform side.
        await using var platformSession = store.QuerySession();
        var schemaCode = ConfigCode.Normalize(request.SchemaCode);
        var schema = await platformSession.Query<ConfigSchemaReadModel>()
            .Where(x => x.CodeNormalized == schemaCode)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException($"Schema '{schemaCode}' not found.", 404);
        if (schema.Archived)
            throw new DomainException("Cannot assign an archived schema.");

        var snapshot = await platformSession.LoadAsync<ConfigSchemaVersionReadModel>(
            ConfigSchemaVersionReadModel.BuildId(schema.Code, request.SchemaVersion), ct)
            ?? throw new DomainException($"Schema version {request.SchemaVersion} not found.", 404);

        // Duplicate active assignment same root/schema is rejected.
        var dup = await tenantSession.Query<ConfigAssignmentReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value
                        && x.RootOrgNodeId == orgNodeId.Value
                        && x.SchemaCode == schema.Code
                        && !x.Archived)
            .FirstOrDefaultAsync(ct);
        if (dup is not null)
            throw new DomainException("An active assignment for this schema already exists at this node.", 409);

        var assignmentId = ConfigAssignmentId.New();
        var now = TimeProvider.System.GetUtcNow();
        tenantSession.Events.StartStream<ConfigAssignmentReadModel>(assignmentId.Value,
            new ConfigSchemaAssigned(
                assignmentId,
                tenant.TenantId,
                orgNodeId,
                schema.Code,
                snapshot.Version,
                now,
                actor.ExternalId));

        audit.Record(tenantSession, new AuditRecord(
            ConfigAuditActions.AssignmentCreated,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Reason: request.Reason,
            Details: new
            {
                AssignmentId = assignmentId.Value,
                NodeId = orgNodeId.Value,
                schema.Code,
                Version = snapshot.Version
            }));

        outbox.Enroll(tenantSession);
        await outbox.PublishAsync(new ConfigInvalidationRequested(
            ConfigChangeKind.AssignmentChanged,
            tenant.TenantId.Value,
            orgNodeId.Value,
            schema.Code,
            snapshot.Version,
            []));

        await tenantSession.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId.Value}",
            new ConfigAssignmentResponse(
                assignmentId.Value,
                node.PublicId,
                schema.Code,
                snapshot.Version,
                false,
                1,
                now,
                null));
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/archive")]
    [EmptyResponse]
    public static async Task ArchiveAssignment(
        string brandId,
        string nodeId,
        Guid assignmentId,
        ArchiveConfigAssignmentRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        IMartenOutbox outbox,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var doc = await session.LoadAsync<ConfigAssignmentReadModel>(assignmentId, ct)
            ?? throw new DomainException("Assignment not found.", 404);
        if (doc.TenantId != tenant.TenantId.Value)
            throw new DomainException("Assignment does not belong to this brand tenant.");
        if (doc.Archived)
            throw new DomainException("Assignment is already archived.");
        if (doc.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {doc.Version}.", 409);

        var now = TimeProvider.System.GetUtcNow();
        session.Events.Append(doc.Id, new ConfigAssignmentArchived(new ConfigAssignmentId(doc.Id), request.Reason, now));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.AssignmentArchived,
            AuditCategories.Config,
            AuditSeverities.Warn,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Reason: request.Reason,
            Details: new { AssignmentId = doc.Id, doc.SchemaCode, doc.SchemaVersion }));

        outbox.Enroll(session);
        await outbox.PublishAsync(new ConfigInvalidationRequested(
            ConfigChangeKind.AssignmentChanged,
            tenant.TenantId.Value,
            doc.RootOrgNodeId,
            doc.SchemaCode,
            doc.SchemaVersion,
            []));

        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/restore")]
    [EmptyResponse]
    public static async Task RestoreAssignment(
        string brandId,
        string nodeId,
        Guid assignmentId,
        RestoreConfigAssignmentRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        IMartenOutbox outbox,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var doc = await session.LoadAsync<ConfigAssignmentReadModel>(assignmentId, ct)
            ?? throw new DomainException("Assignment not found.", 404);
        if (doc.TenantId != tenant.TenantId.Value)
            throw new DomainException("Assignment does not belong to this brand tenant.");
        if (!doc.Archived)
            throw new DomainException("Assignment is not archived.");
        if (doc.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {doc.Version}.", 409);

        var now = TimeProvider.System.GetUtcNow();
        session.Events.Append(doc.Id, new ConfigAssignmentRestored(new ConfigAssignmentId(doc.Id), request.Reason, now));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.AssignmentRestored,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Reason: request.Reason,
            Details: new { AssignmentId = doc.Id, doc.SchemaCode, doc.SchemaVersion }));

        outbox.Enroll(session);
        await outbox.PublishAsync(new ConfigInvalidationRequested(
            ConfigChangeKind.AssignmentChanged,
            tenant.TenantId.Value,
            doc.RootOrgNodeId,
            doc.SchemaCode,
            doc.SchemaVersion,
            []));

        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade-preview")]
    public static async Task<ConfigAssignmentUpgradePreviewResponse> UpgradePreview(
        string brandId,
        string nodeId,
        Guid assignmentId,
        UpgradeConfigAssignmentRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        var (assignment, currentVersion, targetVersion) = await LoadAssignmentAndVersionsAsync(
            store, tenant, assignmentId, request.TargetSchemaVersion, ct);

        var compatible = new List<string>();
        var removed = new List<string>();
        var incompat = new List<string>();
        var currentByCode = currentVersion.Definition.Keys.ToDictionary(k => k.Code, StringComparer.OrdinalIgnoreCase);
        var targetByCode = targetVersion.Definition.Keys.ToDictionary(k => k.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var (code, oldKey) in currentByCode)
        {
            if (!targetByCode.TryGetValue(code, out var newKey))
                removed.Add(code);
            else if (oldKey.ValueType != newKey.ValueType)
                incompat.Add(code);
            else
                compatible.Add(code);
        }

        // Estimate impacted local value rows so the UI can show "N nodes have local values for keys that will be ignored".
        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        var localBags = await tenantSession.Query<ConfigNodeValueReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value && x.SchemaCode == assignment.SchemaCode)
            .ToListAsync(ct);
        var impacted = localBags.Count(bag => bag.Entries.Keys.Any(k => removed.Contains(k, StringComparer.OrdinalIgnoreCase) || incompat.Contains(k, StringComparer.OrdinalIgnoreCase)));

        return new ConfigAssignmentUpgradePreviewResponse(
            assignment.SchemaCode,
            assignment.SchemaVersion,
            targetVersion.Version,
            compatible,
            removed,
            incompat,
            impacted,
            Publishable: true);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade")]
    public static async Task<ConfigAssignmentResponse> UpgradeAssignment(
        string brandId,
        string nodeId,
        Guid assignmentId,
        UpgradeConfigAssignmentRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        IMartenOutbox outbox,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        var (assignment, _, target) = await LoadAssignmentAndVersionsAsync(
            store, tenant, assignmentId, request.TargetSchemaVersion, ct);

        if (assignment.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {assignment.Version}.", 409);
        if (assignment.Archived)
            throw new DomainException("Cannot upgrade an archived assignment.");

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var now = TimeProvider.System.GetUtcNow();
        session.Events.Append(assignment.Id, new ConfigAssignmentUpgraded(
            new ConfigAssignmentId(assignment.Id),
            assignment.SchemaCode,
            assignment.SchemaVersion,
            target.Version,
            now,
            actor.ExternalId));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.AssignmentUpgraded,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Reason: request.Reason,
            Details: new
            {
                AssignmentId = assignment.Id,
                assignment.SchemaCode,
                From = assignment.SchemaVersion,
                To = target.Version
            }));

        outbox.Enroll(session);
        await outbox.PublishAsync(new ConfigInvalidationRequested(
            ConfigChangeKind.AssignmentChanged,
            tenant.TenantId.Value,
            assignment.RootOrgNodeId,
            assignment.SchemaCode,
            target.Version,
            []));

        await session.SaveChangesAsync(ct);

        var refreshed = await session.LoadAsync<ConfigAssignmentReadModel>(assignment.Id, ct);
        return ToResponse(refreshed!);
    }

    // ─── helpers ───

    private static async Task<(ConfigAssignmentReadModel Assignment, ConfigSchemaVersionReadModel Current, ConfigSchemaVersionReadModel Target)>
        LoadAssignmentAndVersionsAsync(
            IDocumentStore store,
            BrandTenantContext tenant,
            Guid assignmentId,
            int targetVersion,
            CancellationToken ct)
    {
        await using var tenantSession = store.QuerySession(tenant.TenantId.Value.ToString());
        var assignment = await tenantSession.LoadAsync<ConfigAssignmentReadModel>(assignmentId, ct)
            ?? throw new DomainException("Assignment not found.", 404);
        if (assignment.TenantId != tenant.TenantId.Value)
            throw new DomainException("Assignment does not belong to this brand tenant.");

        await using var platform = store.QuerySession();
        var current = await platform.LoadAsync<ConfigSchemaVersionReadModel>(
            ConfigSchemaVersionReadModel.BuildId(assignment.SchemaCode, assignment.SchemaVersion), ct)
            ?? throw new DomainException("Current schema version snapshot missing.", 404);

        if (targetVersion <= assignment.SchemaVersion)
            throw new DomainException("Target version must be higher than the current assignment version.");

        var target = await platform.LoadAsync<ConfigSchemaVersionReadModel>(
            ConfigSchemaVersionReadModel.BuildId(assignment.SchemaCode, targetVersion), ct)
            ?? throw new DomainException($"Target version {targetVersion} not found.", 404);

        return (assignment, current, target);
    }

    internal static ConfigAssignmentResponse ToResponse(ConfigAssignmentReadModel a) =>
        new(a.Id, a.RootOrgNodePublicId, a.SchemaCode, a.SchemaVersion, a.Archived, a.Version, a.AssignedAt, a.ArchivedAt);
}
