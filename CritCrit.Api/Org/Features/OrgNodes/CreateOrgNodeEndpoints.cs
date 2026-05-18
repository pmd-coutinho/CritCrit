using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Brands;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.OrgNodes;

// One class per endpoint — required by Wolverine's per-class convention-method
// resolution. See .scratch/deterministic-stream-ids/PRD.md "Prereq 4".

public static class CreateCountryEndpoint
{
    public static ProblemDetails Validate(CreatePlainOrgNodeRequest request) =>
        OrgValidators.ValidatePlainOrgNode(request);

    [WolverinePost("/api/brands/{brandId}/countries")]
    public static Task<IResult> Handle(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct) =>
        CreateOrgNode.HandleAsync(request, OrgNodeType.Country, brandId, store, authorization, audit, tenant, actor, ct);
}

public static class CreateFranchiseEndpoint
{
    public static ProblemDetails Validate(CreatePlainOrgNodeRequest request) =>
        OrgValidators.ValidatePlainOrgNode(request);

    [WolverinePost("/api/brands/{brandId}/franchises")]
    public static Task<IResult> Handle(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct) =>
        CreateOrgNode.HandleAsync(request, OrgNodeType.Franchise, brandId, store, authorization, audit, tenant, actor, ct);
}

public static class CreateStoreEndpoint
{
    public static ProblemDetails Validate(CreateStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParentId))
            return Problem("parentId", "parentId is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
            return Problem("code", "code is required.");
        if (request.Code.Length > 128)
            return Problem("code", "code must be 128 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem("name", "name is required.");
        if (request.Name.Length > 200)
            return Problem("name", "name must be 200 characters or fewer.");
        return WolverineContinue.NoProblems;
    }

    private static ProblemDetails Problem(string title, string detail) =>
        new() { Title = title, Detail = detail, Status = 400 };

    [WolverinePost("/api/brands/{brandId}/stores")]
    public static async Task<IResult> Handle(
        CreateStoreRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await OrgNodeParentValidation.ValidateParentAsync(session, actor, authorization, tenant.TenantId.Value, parentId, OrgNodeType.Store, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Store, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        var tz = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone.Trim();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentId, OrgNodeType.Store, request.Code.Trim(), normalized, request.Name.Trim()));
        session.Events.Append(id.Value, new StoreProfileCreated(id, tz));
        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OrgNodeCreated,
            AuditCategories.Org,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            id.Value,
            details: new { Type = OrgNodeType.Store.ToString(), Code = request.Code.Trim(), Name = request.Name.Trim(), ParentId = parent.PublicId, TimeZone = tz },
            targetPublicId: OrgPublicId.Format(OrgNodeType.Store, id),
            targetType: "store",
            targetLabel: request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        var publicId = OrgPublicId.Format(OrgNodeType.Store, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }
}

public static class CreateDeviceEndpoint
{
    public static ProblemDetails Validate(CreateDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParentStoreId))
            return Problem("parentStoreId", "parentStoreId is required.");
        if (string.IsNullOrWhiteSpace(request.SerialNumber))
            return Problem("serialNumber", "serialNumber is required.");
        if (request.SerialNumber.Length > 128)
            return Problem("serialNumber", "serialNumber must be 128 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem("name", "name is required.");
        if (request.Name.Length > 200)
            return Problem("name", "name must be 200 characters or fewer.");
        if (!Enum.IsDefined(request.DeviceType))
            return Problem("deviceType", "deviceType is not a recognised value.");
        return WolverineContinue.NoProblems;
    }

    private static ProblemDetails Problem(string title, string detail) =>
        new() { Title = title, Detail = detail, Status = 400 };

    [WolverinePost("/api/brands/{brandId}/devices")]
    public static async Task<IResult> Handle(
        CreateDeviceRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(request.ParentStoreId, OrgNodeType.Store, out var parentStoreId))
            throw new DomainException("Invalid parent store ID.");

        var parent = await OrgNodeParentValidation.ValidateParentAsync(session, actor, authorization, tenant.TenantId.Value, parentStoreId, OrgNodeType.Device, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Device, request.SerialNumber);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentStoreId, OrgNodeType.Device, request.SerialNumber.Trim(), normalized, request.Name.Trim()));
        session.Events.Append(id.Value, new DeviceProfileCreated(id, request.SerialNumber.Trim(), request.DeviceType));
        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OrgNodeCreated,
            AuditCategories.Org,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            id.Value,
            details: new { Type = OrgNodeType.Device.ToString(), SerialNumber = request.SerialNumber.Trim(), Name = request.Name.Trim(), ParentId = parent.PublicId, DeviceType = request.DeviceType.ToString() },
            targetPublicId: OrgPublicId.Format(OrgNodeType.Device, id),
            targetType: "device",
            targetLabel: request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        var publicId = OrgPublicId.Format(OrgNodeType.Device, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }
}

// Shared body for Country + Franchise (identical except OrgNodeType).
internal static class CreateOrgNode
{
    public static async Task<IResult> HandleAsync(
        CreatePlainOrgNodeRequest request,
        OrgNodeType nodeType,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await OrgNodeParentValidation.ValidateParentAsync(session, actor, authorization, tenant.TenantId.Value, parentId, nodeType, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(nodeType, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentId, nodeType, request.Code.Trim(), normalized, request.Name.Trim()));
        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OrgNodeCreated,
            AuditCategories.Org,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            id.Value,
            details: new { Type = nodeType.ToString(), Code = request.Code.Trim(), Name = request.Name.Trim(), ParentId = parent.PublicId },
            targetPublicId: OrgPublicId.Format(nodeType, id),
            targetType: nodeType.ToString().ToLowerInvariant(),
            targetLabel: request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        var publicId = OrgPublicId.Format(nodeType, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }
}
