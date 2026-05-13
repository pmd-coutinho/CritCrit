using System.Text.Json;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public sealed class OrgApiMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context, IDocumentStore store, OrgCommandService commands)
    {
        if (!context.Request.Path.StartsWithSegments("/api/platform") &&
            !context.Request.Path.StartsWithSegments("/api/brands"))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        try
        {
            if (context.Request.Method == HttpMethods.Post &&
                context.Request.Path == "/api/platform/brands")
            {
                var request = await ReadAsync<CreateBrandRequest>(context);
                await using var session = store.LightweightSession();
                var actor = await ActorContextResolver.ResolveAsync(session, context.User, context.RequestAborted);
                await WriteAsync(context, ToResponse(await commands.CreateBrandAsync(store, actor, request.Code, request.Name, context.RequestAborted)));
                return;
            }

            if (context.Request.Method == HttpMethods.Post &&
                context.Request.Path == "/api/platform/subjects")
            {
                var request = await ReadAsync<CreateSubjectRequest>(context);
                await using var session = store.LightweightSession();
                var actor = await ActorContextResolver.ResolveAsync(session, context.User, context.RequestAborted);
                var subject = await commands.CreateSubjectAsync(session, actor, request.Email, request.DisplayName, request.Provider, request.ProviderTenant, request.ExternalId, context.RequestAborted);
                await WriteAsync(context, new SubjectResponse(subject.PublicId, subject.Email, subject.DisplayName));
                return;
            }

            if (context.Request.Path.StartsWithSegments("/api/brands", out var remaining))
            {
                await HandleBrandRoute(context, store, commands, remaining);
                return;
            }

            await next(context);
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message }, JsonOptions, context.RequestAborted);
        }
    }

    private static async Task HandleBrandRoute(HttpContext context, IDocumentStore store, OrgCommandService commands, PathString remaining)
    {
        var parts = remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length < 2 || !OrgPublicId.TryParseOrgNode(parts[0], OrgNodeType.Brand, out var tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await using var session = store.LightweightSession(tenantId.Value.ToString());
        var actor = await ActorContextResolver.ResolveAsync(session, context.User, context.RequestAborted);

        if (context.Request.Method == HttpMethods.Get &&
            parts.Length == 3 &&
            parts[1] == "org-nodes" &&
            parts[2] is var orgNodeId &&
            OrgPublicId.TryParseOrgNode(orgNodeId, out var nodeId, out _))
        {
            var node = await session.LoadAsync<OrgNodeReadModel>(nodeId.Value, context.RequestAborted);
            if (node is null || node.TenantId != tenantId.Value)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await WriteAsync(context, ToResponse(node));
            return;
        }

        var resource = parts.ElementAtOrDefault(1);
        if (context.Request.Method == HttpMethods.Post && resource == "countries")
        {
            await CreatePlain(context, session, commands, actor, tenantId, OrgNodeType.Country);
            return;
        }

        if (context.Request.Method == HttpMethods.Post && resource == "franchises")
        {
            await CreatePlain(context, session, commands, actor, tenantId, OrgNodeType.Franchise);
            return;
        }

        if (context.Request.Method == HttpMethods.Post && resource == "stores")
        {
            var storeRequest = await ReadAsync<CreateStoreRequest>(context);
            if (!OrgPublicId.TryParseOrgNode(storeRequest.ParentId, out var storeParentId, out _))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await WriteAsync(context, ToResponse(await commands.CreateStoreAsync(session, actor, tenantId, storeParentId, storeRequest.Code, storeRequest.Name, storeRequest.TimeZone ?? "UTC", context.RequestAborted)));
            return;
        }

        if (context.Request.Method == HttpMethods.Post && resource == "devices")
        {
            var deviceRequest = await ReadAsync<CreateDeviceRequest>(context);
            if (!OrgPublicId.TryParseOrgNode(deviceRequest.ParentStoreId, OrgNodeType.Store, out var parentStoreId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await WriteAsync(context, ToResponse(await commands.CreateDeviceAsync(session, actor, tenantId, parentStoreId, deviceRequest.SerialNumber, deviceRequest.Name, deviceRequest.DeviceType, context.RequestAborted)));
            return;
        }

        if (context.Request.Method == HttpMethods.Post && resource == "access-grants")
        {
            var grantRequest = await ReadAsync<GrantRoleRequest>(context);
            if (!OrgPublicId.TryParseOrgNode(grantRequest.OrgNodeId, out var grantNodeId, out _) ||
                !OrgPublicId.TryParseSubject(grantRequest.SubjectId, out var subjectId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var grant = await commands.GrantRoleAsync(session, actor, tenantId, grantNodeId, subjectId, grantRequest.Role, grantRequest.ExpiresAt, OrgAccessGrantSource.DirectGrant, null, context.RequestAborted);
            await WriteAsync(context, new GrantResponse(grant.Id, grantRequest.OrgNodeId, grantRequest.SubjectId, grant.Role, grant.ExpiresAt));
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static async Task CreatePlain(HttpContext context, IDocumentSession session, OrgCommandService commands, ActorContext actor, OrgNodeId tenantId, OrgNodeType type)
    {
        var request = await ReadAsync<CreatePlainOrgNodeRequest>(context);
        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await WriteAsync(context, ToResponse(await commands.CreatePlainNodeAsync(session, actor, tenantId, parentId, type, request.Code, request.Name, context.RequestAborted)));
    }

    private static async Task<T> ReadAsync<T>(HttpContext context) =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, JsonOptions, context.RequestAborted) ??
        throw new DomainException("Invalid JSON body.");

    private static Task WriteAsync<T>(HttpContext context, T response)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context.Response.WriteAsJsonAsync(response, JsonOptions, context.RequestAborted);
    }

    private static OrgNodeResponse ToResponse(OrgNodeReadModel node) => new(
        node.PublicId,
        node.TenantPublicId,
        node.ParentPublicId,
        node.Type,
        node.Code,
        node.Name,
        node.Path,
        node.Archived,
        node.EffectiveArchived,
        node.HardDeleted);
}
