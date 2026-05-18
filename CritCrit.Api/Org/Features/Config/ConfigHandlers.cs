using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Config;

public static class ConfigHandlers
{
    // ─── List effective schemas for node ───

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/config")]
    public static async Task<IReadOnlyList<NodeConfigSchemaSummary>> ListEffectiveSchemas(
        string brandId,
        string nodeId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseOrgNode(nodeId, out var orgNodeId, out _))
            throw new DomainException("Invalid org node ID.");

        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        var node = await OrgValidation.LoadNodeAsync(tenantSession, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted)
            throw new DomainException("Org node not found.", 404);

        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(tenantSession, actor, node, OrgRole.Viewer, ct);

        var path = node.AncestorIds.Append(node.Id).ToArray();
        var assignments = await tenantSession.Query<ConfigAssignmentReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value
                        && !x.Archived
                        && path.Contains(x.RootOrgNodeId))
            .ToListAsync(ct);

        if (assignments.Count == 0) return [];

        // Pick deepest per schema code.
        var depth = path.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
        var deepestPerSchema = assignments
            .GroupBy(a => a.SchemaCode)
            .Select(g => g.OrderByDescending(a => depth[a.RootOrgNodeId]).First())
            .ToArray();

        await using var platform = store.QuerySession();
        var snapIds = deepestPerSchema
            .Select(a => ConfigSchemaVersionReadModel.BuildId(a.SchemaCode, a.SchemaVersion))
            .ToArray();
        var snaps = (await platform.Query<ConfigSchemaVersionReadModel>()
            .Where(x => snapIds.Contains(x.Id))
            .ToListAsync(ct))
            .ToDictionary(s => s.Id);

        var bags = await tenantSession.Query<ConfigNodeValueReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value && x.OrgNodeId == orgNodeId.Value)
            .ToListAsync(ct);
        var bagByCode = bags.ToDictionary(b => b.SchemaCode);

        return deepestPerSchema.Select(a =>
        {
            snaps.TryGetValue(ConfigSchemaVersionReadModel.BuildId(a.SchemaCode, a.SchemaVersion), out var snap);
            bagByCode.TryGetValue(a.SchemaCode, out var bag);
            return new NodeConfigSchemaSummary(
                a.SchemaCode,
                snap?.Definition.Name ?? a.SchemaCode,
                a.SchemaVersion,
                a.Id,
                a.RootOrgNodePublicId,
                bag?.Version ?? 0);
        }).ToArray();
    }

    // ─── Full-object pure lookup ───

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/config/{path}")]
    public static async Task<IResult> LookupConfig(
        string brandId,
        string nodeId,
        string path,
        bool? includeMetadata,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigResolutionService resolver,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseOrgNode(nodeId, out var orgNodeId, out _))
            throw new DomainException("Invalid org node ID.");

        var (schemaCode, keyCode) = SplitPath(path);

        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        var node = await OrgValidation.LoadNodeAsync(tenantSession, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Org node not found.", 404);

        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(tenantSession, actor, node, OrgRole.Viewer, ct);

        var lookup = await resolver.ResolveAssignmentAsync(node, schemaCode, ct);
        if (lookup is null)
            throw new DomainException("Schema is not assigned at or above this node.", 404);

        var resolved = await resolver.ResolveAllAsync(node, lookup, ct);

        if (keyCode is null)
            return includeMetadata == true
                ? Results.Ok(BuildMetadataResponse(node, lookup, resolved))
                : Results.Ok(BuildPureObjectResponse(resolved));

        var single = resolved.FirstOrDefault(r => string.Equals(r.KeyCode, keyCode, StringComparison.OrdinalIgnoreCase));
        if (single is null)
            throw new DomainException("Unknown key.", 404);

        if (includeMetadata == true)
            return Results.Ok(BuildSingleMetadata(single));

        if (single.Source is ConfigValueResolutionSource.Missing or ConfigValueResolutionSource.Unset)
            throw new DomainException("Value not available.", 404);
        if (single.Encrypted)
            throw new DomainException("Encrypted values are not readable in v1.", 404);

        return Results.Content(single.JsonValue ?? "null", "application/json");
    }

    // ─── Patch values ───

    [WolverinePatch("/api/brands/{brandId}/org-nodes/{nodeId}/config/{schemaCode}")]
    public static async Task<IResult> PatchValues(
        string brandId,
        string nodeId,
        string schemaCode,
        PatchConfigValuesRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IConfigEncryptionService encryption,
        ConfigResolutionService resolver,
        IAuditWriter audit,
        IMartenOutbox outbox,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseOrgNode(nodeId, out var orgNodeId, out _))
            throw new DomainException("Invalid org node ID.");

        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(tenantSession, actor);

        var node = await OrgValidation.LoadNodeAsync(tenantSession, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Org node not found.", 404);

        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(tenantSession, actor, node, OrgRole.Admin, ct);

        var lookup = await resolver.ResolveAssignmentAsync(node, schemaCode, ct);
        if (lookup is null)
            throw new DomainException("Schema is not assigned at or above this node.", 404);

        var keyByCode = lookup.SchemaVersion.Definition.Keys
            .ToDictionary(k => k.Code, StringComparer.OrdinalIgnoreCase);

        var bagId = ConfigNodeValueReadModel.BuildId(tenant.TenantId.Value, orgNodeId.Value, lookup.Assignment.SchemaCode);
        var bag = await tenantSession.LoadAsync<ConfigNodeValueReadModel>(bagId, ct);
        // Deterministic stream id for new value bags; existing bags honour
        // their stored random StreamId per .scratch/deterministic-stream-ids/PRD.md.
        var streamId = bag?.StreamId ?? DeterministicGuid.From(tenant.TenantId.Value, orgNodeId.Value, lookup.Assignment.SchemaCode);

        if (bag is null)
        {
            tenantSession.Events.StartStream<ConfigNodeValueReadModel>(streamId, new ConfigNodeValueSetInitialized(
                streamId, tenant.TenantId, orgNodeId, lookup.Assignment.SchemaCode));
        }
        else if (bag.Version != request.ExpectedVersion)
        {
            throw new DomainException($"Expected value-set version {request.ExpectedVersion}, found {bag.Version}.", 409);
        }

        var applied = new List<ConfigValuePatchApplied>(request.Operations.Count);
        foreach (var op in request.Operations)
        {
            if (!keyByCode.TryGetValue(op.KeyCode, out var key))
                throw new DomainException($"Unknown key '{op.KeyCode}' for schema '{lookup.Assignment.SchemaCode}'.");

            switch (op.Operation)
            {
                case ConfigValuePatchOperationKind.Set:
                {
                    if (op.JsonValue is null)
                        throw new DomainException($"Set requires JSON value for key '{op.KeyCode}'.");

                    var validationResult = validation.ValidateValue(key, op.JsonValue);
                    if (!validationResult.IsValid)
                        throw new DomainException(
                            $"Invalid value for '{op.KeyCode}': {string.Join("; ", validationResult.Errors)}");

                    ConfigStoredValue stored;
                    if (key.ValueType == ConfigValueType.EncryptedString)
                    {
                        var plaintext = JsonSerializer.Deserialize<string>(op.JsonValue)
                            ?? throw new DomainException($"Encrypted key '{op.KeyCode}' must be a JSON string.");
                        stored = new ConfigStoredValue(
                            ConfigValueType.EncryptedString,
                            JsonValue: null,
                            Ciphertext: encryption.Protect(plaintext),
                            ContentHash: null);
                    }
                    else
                    {
                        stored = new ConfigStoredValue(
                            key.ValueType,
                            JsonValue: op.JsonValue,
                            Ciphertext: null,
                            ContentHash: HashJson(op.JsonValue));
                    }

                    applied.Add(new ConfigValuePatchApplied(op.KeyCode, op.Operation, stored, actor.ExternalId));
                    break;
                }
                case ConfigValuePatchOperationKind.Inherit:
                case ConfigValuePatchOperationKind.Unset:
                    applied.Add(new ConfigValuePatchApplied(op.KeyCode, op.Operation, null, actor.ExternalId));
                    break;
            }
        }

        if (applied.Count == 0)
            throw new DomainException("Patch must contain at least one operation.");

        var now = TimeProvider.System.GetUtcNow();
        tenantSession.Events.Append(streamId, new ConfigNodeValuesPatched(
            tenant.TenantId,
            orgNodeId,
            lookup.Assignment.SchemaCode,
            applied,
            now,
            actor.ExternalId));

        audit.Record(tenantSession, new AuditRecord(
            ConfigAuditActions.ValuesPatched,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Reason: request.Reason,
            Details: new
            {
                NodeId = orgNodeId.Value,
                SchemaCode = lookup.Assignment.SchemaCode,
                SchemaVersion = lookup.SchemaVersion.Version,
                Operations = applied.Select(a => new { a.KeyCode, Operation = a.Operation.ToString() }).ToArray()
            }));

        outbox.Enroll(tenantSession);
        await outbox.PublishAsync(new ConfigInvalidationRequested(
            ConfigChangeKind.ValuesChanged,
            tenant.TenantId.Value,
            orgNodeId.Value,
            lookup.Assignment.SchemaCode,
            lookup.SchemaVersion.Version,
            applied.Select(a => a.KeyCode).ToArray()));

        await tenantSession.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ─── helpers ───

    private static (string SchemaCode, string? KeyCode) SplitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DomainException("Path is required.");
        var dot = path.IndexOf('.');
        if (dot < 0) return (path, null);
        return (path[..dot], path[(dot + 1)..]);
    }

    private static IDictionary<string, object?> BuildPureObjectResponse(IReadOnlyList<ConfigResolutionService.ResolvedKey> resolved)
    {
        var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var r in resolved)
        {
            if (r.Encrypted) continue;
            if (r.Source is ConfigValueResolutionSource.Missing or ConfigValueResolutionSource.Unset) continue;
            obj[r.KeyCode] = r.JsonValue is null ? null : JsonDocument.Parse(r.JsonValue).RootElement.Clone();
        }
        return obj;
    }

    private static ConfigLookupMetadataResponse BuildMetadataResponse(
        OrgNodeReadModel node,
        ConfigResolutionService.AssignmentLookup lookup,
        IReadOnlyList<ConfigResolutionService.ResolvedKey> resolved)
    {
        var values = new Dictionary<string, ConfigLookupValueMetadata>(StringComparer.Ordinal);
        foreach (var r in resolved)
            values[r.KeyCode] = MapMetadata(r);

        return new ConfigLookupMetadataResponse(
            lookup.Assignment.SchemaCode,
            lookup.Assignment.SchemaVersion,
            node.PublicId,
            new ConfigAssignmentSummary(lookup.Assignment.Id, lookup.Assignment.RootOrgNodePublicId, lookup.Assignment.SchemaCode, lookup.Assignment.SchemaVersion),
            lookup.Assignment.Version,
            values);
    }

    private static ConfigLookupValueMetadata MapMetadata(ConfigResolutionService.ResolvedKey r)
    {
        var state = r.Source switch
        {
            ConfigValueResolutionSource.Local => "set",
            ConfigValueResolutionSource.Inherited => "set",
            ConfigValueResolutionSource.Default => "default",
            ConfigValueResolutionSource.Unset => "unset",
            _ => "missing",
        };
        object? value = null;
        if (!r.Encrypted && r.JsonValue is not null && state != "missing" && state != "unset")
            value = JsonDocument.Parse(r.JsonValue).RootElement.Clone();

        return new ConfigLookupValueMetadata(
            state,
            value,
            r.SourceNodeId,
            r.Encrypted,
            r.Encrypted ? state == "set" : null,
            r.Encrypted && state == "set" ? "********" : null);
    }

    private static object BuildSingleMetadata(ConfigResolutionService.ResolvedKey r) =>
        new
        {
            r.KeyCode,
            metadata = MapMetadata(r),
        };

    private static string HashJson(string json)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..32];
    }
}
