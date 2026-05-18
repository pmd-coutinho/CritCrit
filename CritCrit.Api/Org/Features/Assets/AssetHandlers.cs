using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Assets;

public static class AssetHandlers
{
    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/assets")]
    public static async Task<IReadOnlyList<AssetLookupResponse>> ListAssets(
        string brandId,
        OrgNodeId nodeId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        AssetResolutionService resolver,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Viewer, ct);
        var resolved = await resolver.ResolveAllAsync(node, ct);
        return resolved.Select(r => ToResponse(brandId, node.PublicId,r)).ToArray();
    }

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}")]
    public static async Task<AssetLookupResponse> GetAsset(
        string brandId,
        OrgNodeId nodeId,
        string key,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        AssetResolutionService resolver,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        AssetKey.EnsureValid(key);
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Viewer, ct);
        var resolved = await resolver.ResolveOneAsync(node, key, ct);
        return ToResponse(brandId, node.PublicId,resolved);
    }

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}/content")]
    public static async Task<IResult> GetAssetContent(
        OrgNodeId nodeId,
        string key,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        AssetResolutionService resolver,
        IAssetStorage storage,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        AssetKey.EnsureValid(key);
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Viewer, ct);
        var resolved = await resolver.ResolveOneAsync(node, key, ct);
        if (resolved.Source is AssetResolutionSource.Missing or AssetResolutionSource.Unset || resolved.File is null)
            throw new DomainException("Asset not available.", 404);

        var stream = await storage.OpenReadAsync(resolved.File, ct);
        return Results.File(stream, resolved.File.ContentType, resolved.File.FileName, enableRangeProcessing: true);
    }

    [WolverinePut("/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}")]
    [RequestSizeLimit(AssetValidation.VideoMaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AssetValidation.VideoMaxBytes)]
    public static async Task<IResult> UploadAsset(
        string key,
        OrgNodeId nodeId,
        [FromForm] long expectedVersion,
        [FromForm] string? reason,
        IFormFile file,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAssetStorage storage,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        AssetKey.EnsureValid(key);
        if (file.Length == 0)
            throw new DomainException("Asset file is required.");

        var normalizedKey = AssetKey.Normalize(key);
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Admin, ct);
        var (kind, limit) = AssetValidation.Classify(file.ContentType, file.FileName);

        await using var input = file.OpenReadStream();
        var (content, sha256) = await AssetValidation.BufferAndHashAsync(input, limit, ct);
        await using (content)
        {
            var stored = new AssetStoredFile(
                BuildBlobName(tenant.TenantId.Value, node.Id, normalizedKey, file.FileName),
                Path.GetFileName(file.FileName),
                NormalizeContentType(file.ContentType, kind),
                kind,
                content.Length,
                sha256,
                TimeProvider.System.GetUtcNow(),
                actor.ExternalId);

            await storage.UploadAsync(stored, content, ct);
            await PatchAsync(
                node,
                expectedVersion,
                [new AssetPatchApplied(normalizedKey, AssetPatchOperationKind.Set, stored, actor.ExternalId)],
                reason,
                store,
                audit,
                tenant,
                actor,
                ct);
        }

        return Results.NoContent();
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}/inherit")]
    [EmptyResponse]
    public static async Task InheritAsset(
        string key,
        OrgNodeId nodeId,
        PatchAssetRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        AssetKey.EnsureValid(key);
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Admin, ct);
        await PatchAsync(
            node,
            request.ExpectedVersion,
            [new AssetPatchApplied(AssetKey.Normalize(key), AssetPatchOperationKind.Inherit, null, actor.ExternalId)],
            request.Reason,
            store,
            audit,
            tenant,
            actor,
            ct);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}/unset")]
    [EmptyResponse]
    public static async Task UnsetAsset(
        string key,
        OrgNodeId nodeId,
        PatchAssetRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        AssetKey.EnsureValid(key);
        var node = await LoadAuthorizedNode(nodeId, store, authorization, tenant, actor, OrgRole.Admin, ct);
        await PatchAsync(
            node,
            request.ExpectedVersion,
            [new AssetPatchApplied(AssetKey.Normalize(key), AssetPatchOperationKind.Unset, null, actor.ExternalId)],
            request.Reason,
            store,
            audit,
            tenant,
            actor,
            ct);
    }

    private static async Task<OrgNodeReadModel> LoadAuthorizedNode(
        OrgNodeId nodeId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        OrgRole required,
        CancellationToken ct)
    {
        var orgNodeId = nodeId;

        await using var session = SessionFactory.TenantSession(store, tenant);
        var node = await OrgValidation.LoadNodeAsync(session, orgNodeId, ct);
        if (node.TenantId != tenant.TenantId.Value || node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Org node not found.", 404);

        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(session, actor, node, required, ct);

        return node;
    }

    private static async Task PatchAsync(
        OrgNodeReadModel node,
        long expectedVersion,
        IReadOnlyList<AssetPatchApplied> applied,
        string? reason,
        IDocumentStore store,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var bagId = AssetNodeValueReadModel.BuildId(tenant.TenantId.Value, node.Id);
        var bag = await session.LoadAsync<AssetNodeValueReadModel>(bagId, ct);
        // Deterministic stream id for new asset bags; existing bags honour
        // their stored random StreamId per .scratch/deterministic-stream-ids/PRD.md.
        var streamId = bag?.StreamId ?? DeterministicGuid.From(tenant.TenantId.Value, node.Id);

        if (bag is null)
        {
            if (expectedVersion != 0)
                throw new DomainException($"Expected asset value-set version {expectedVersion}, found 0.", 409);

            session.Events.StartStream<AssetNodeValueReadModel>(
                streamId,
                new AssetNodeValueSetInitialized(streamId, tenant.TenantId, new OrgNodeId(node.Id)));
        }
        else if (bag.Version != expectedVersion)
        {
            throw new DomainException($"Expected asset value-set version {expectedVersion}, found {bag.Version}.", 409);
        }

        var now = TimeProvider.System.GetUtcNow();
        session.Events.Append(streamId, new AssetNodeValuesPatched(
            tenant.TenantId,
            new OrgNodeId(node.Id),
            applied,
            now,
            actor.ExternalId));

        audit.Record(session, new AuditRecord(
            AssetAuditActions.ValuesPatched,
            AuditCategories.Asset,
            AuditSeverities.Info,
            Actor: actor,
            TenantId: tenant.TenantId.Value,
            TenantPublicId: tenant.BrandPublicId,
            Target: new AuditResourceRef("org_node", node.Id, node.PublicId, node.Name),
            Reason: reason,
            Details: new
            {
                NodeId = node.Id,
                NodePublicId = node.PublicId,
                Operations = applied.Select(a => new { a.Key, Operation = a.Operation.ToString(), File = a.File?.FileName }).ToArray()
            }));

        await session.SaveChangesAsync(ct);
    }

    private static AssetLookupResponse ToResponse(
        string brandId,
        string nodePublicId,
        AssetResolutionService.ResolvedAsset asset)
    {
        var state = asset.Source switch
        {
            AssetResolutionSource.Local => "set",
            AssetResolutionSource.Inherited => "set",
            AssetResolutionSource.Unset => "unset",
            _ => "missing"
        };

        var contentUrl = asset.File is null
            ? null
            : $"/api/brands/{Uri.EscapeDataString(brandId)}/org-nodes/{Uri.EscapeDataString(nodePublicId)}/assets/{Uri.EscapeDataString(asset.Key)}/content";

        return new AssetLookupResponse(
            asset.Key,
            GroupFor(asset.Key),
            state,
            asset.Source.ToString(),
            asset.SourceNodeId,
            asset.ValueSetVersion,
            asset.File is null
                ? null
                : new AssetFileResponse(
                    asset.File.FileName,
                    asset.File.ContentType,
                    asset.File.Kind,
                    asset.File.Length,
                    asset.File.Sha256,
                    asset.File.UploadedAt,
                    asset.File.UploadedByExternalId),
            contentUrl);
    }

    private static string GroupFor(string key)
    {
        var dot = key.IndexOf('.');
        return dot < 0 ? "general" : key[..dot];
    }

    private static string BuildBlobName(Guid tenantId, Guid nodeId, string key, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var safeExt = ext.Length > 16 ? "" : ext.ToLowerInvariant();
        return $"{tenantId:N}/{nodeId:N}/{key}/{Guid.CreateVersion7():N}{safeExt}";
    }

    private static string NormalizeContentType(string contentType, AssetKind kind) =>
        kind == AssetKind.Markdown && string.IsNullOrWhiteSpace(contentType)
            ? "text/markdown"
            : contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
}
