using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public sealed class OrgCommandService(OrgAuthorizationService authorization)
{
    public async Task<OrgNodeReadModel> CreateBrandAsync(
        IDocumentStore store,
        ActorContext actor,
        string code,
        string name,
        CancellationToken ct)
    {
        var id = OrgNodeId.New();
        await using var session = store.LightweightSession(id.Value.ToString());
        var normalized = ValidateCode(OrgNodeType.Brand, code);
        await EnsureCodeAvailableAsync(session, id, normalized, ct);

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id,
            id,
            null,
            OrgNodeType.Brand,
            code.Trim(),
            normalized,
            name.Trim()));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
    }

    public async Task<OrgNodeReadModel> CreateBrandAsync(
        IDocumentSession session,
        ActorContext actor,
        string code,
        string name,
        CancellationToken ct)
    {
        var id = OrgNodeId.New();
        var normalized = ValidateCode(OrgNodeType.Brand, code);
        await EnsureCodeAvailableAsync(session, id, normalized, ct);

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id,
            id,
            null,
            OrgNodeType.Brand,
            code.Trim(),
            normalized,
            name.Trim()));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
    }

    public async Task<OrgNodeReadModel> CreatePlainNodeAsync(
        IDocumentSession session,
        ActorContext actor,
        OrgNodeId tenantId,
        OrgNodeId parentId,
        OrgNodeType type,
        string code,
        string name,
        CancellationToken ct)
    {
        if (type is OrgNodeType.Brand or OrgNodeType.Store or OrgNodeType.Device)
            throw new DomainException($"{type} must be created through its typed command.");

        var parent = await LoadActiveNodeAsync(session, parentId, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, type);

        var normalized = ValidateCode(type, code);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentId, type, code.Trim(), normalized, name.Trim()));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
    }

    public async Task<OrgNodeReadModel> CreateStoreAsync(
        IDocumentSession session,
        ActorContext actor,
        OrgNodeId tenantId,
        OrgNodeId parentId,
        string code,
        string name,
        string timeZone,
        CancellationToken ct)
    {
        var parent = await LoadActiveNodeAsync(session, parentId, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, OrgNodeType.Store);

        var normalized = ValidateCode(OrgNodeType.Store, code);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        var tz = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim();
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentId, OrgNodeType.Store, code.Trim(), normalized, name.Trim()));
        session.Events.Append(id.Value, new StoreProfileCreated(id, tz));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
    }

    public async Task<OrgNodeReadModel> CreateDeviceAsync(
        IDocumentSession session,
        ActorContext actor,
        OrgNodeId tenantId,
        OrgNodeId parentStoreId,
        string serialNumber,
        string name,
        DeviceType deviceType,
        CancellationToken ct)
    {
        var parent = await LoadActiveNodeAsync(session, parentStoreId, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, OrgNodeType.Device);

        var normalized = ValidateCode(OrgNodeType.Device, serialNumber);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentStoreId, OrgNodeType.Device, serialNumber.Trim(), normalized, name.Trim()));
        session.Events.Append(id.Value, new DeviceProfileCreated(id, serialNumber.Trim(), deviceType));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
    }

    public async Task<SubjectReadModel> CreateSubjectAsync(
        IDocumentSession session,
        ActorContext actor,
        string email,
        string? displayName,
        string provider,
        string providerTenant,
        string externalId,
        CancellationToken ct)
    {
        var id = SubjectId.New();
        session.Events.StartStream<SubjectReadModel>(id.Value, new SubjectCreated(id, SubjectKind.User, email.Trim(), displayName?.Trim()));
        session.Events.Append(id.Value, new ExternalIdentityLinked(id, provider, providerTenant, externalId));

        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<SubjectReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create SubjectReadModel.");
    }

    public async Task<OrgAccessGrantReadModel> GrantRoleAsync(
        IDocumentSession session,
        ActorContext actor,
        OrgNodeId tenantId,
        OrgNodeId orgNodeId,
        SubjectId subjectId,
        OrgRole role,
        DateTimeOffset? expiresAt,
        OrgAccessGrantSource source,
        InvitationId? invitationId,
        CancellationToken ct)
    {
        var target = await LoadNodeAsync(session, orgNodeId, ct);
        ValidateTenant(tenantId, target);
        if (target.HardDeleted || target.EffectiveArchived)
            throw new DomainException("Cannot grant access to inactive org nodes.");
        if (!OrgRules.CanGrantRoleAt(role, target.Type))
            throw new DomainException($"{role} can only be granted at the Brand root.");

        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        if (subject is null || !subject.Active)
            throw new DomainException("Subject does not exist or is inactive.");

        if (await authorization.WouldBeRedundantAsync(session, target, subjectId, role, TimeProvider.System.GetUtcNow(), ct))
            throw new DomainException("Grant would be redundant with inherited access.");

        var id = OrgAccessGrantReadModel.BuildId(tenantId, orgNodeId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is { Status: OrgAccessGrantStatus.Active })
        {
            if (grant.Role == role)
                throw new DomainException("Equivalent direct grant already exists.");

            session.Events.Append(grant.StreamId, new OrgAccessRoleChanged(tenantId, orgNodeId, subjectId, grant.Role, role));
            await session.SaveChangesAsync(ct);
            return (await session.LoadAsync<OrgAccessGrantReadModel>(id, ct))!;
        }

        var streamId = Guid.CreateVersion7();
        session.Events.StartStream<OrgAccessGrantReadModel>(streamId, new OrgAccessGranted(tenantId, orgNodeId, subjectId, role, expiresAt, source, invitationId));
        await session.SaveChangesAsync(ct);
        return await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");
    }

    private static string ValidateCode(OrgNodeType type, string code)
    {
        if (!OrgCode.IsValid(type, code))
            throw new DomainException($"Invalid {type} code.");

        return OrgCode.Normalize(type, code);
    }

    private static async Task EnsureCodeAvailableAsync(IDocumentSession session, OrgNodeId tenantId, string normalized, CancellationToken ct)
    {
        var existing = await session.LoadAsync<OrgNodeCodeIndex>(OrgNodeCodeIndex.BuildId(tenantId, normalized), ct);
        if (existing is { HardDeleted: false })
            throw new DomainException("Org node code is already reserved in this tenant.");
    }

    private static async Task<OrgNodeReadModel> LoadNodeAsync(IQuerySession session, OrgNodeId id, CancellationToken ct)
    {
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct);
        return node ?? throw new DomainException("Org node was not found.");
    }

    private static async Task<OrgNodeReadModel> LoadActiveNodeAsync(IQuerySession session, OrgNodeId id, CancellationToken ct)
    {
        var node = await LoadNodeAsync(session, id, ct);
        if (node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Org node is inactive.");
        return node;
    }

    private static void ValidateTenant(OrgNodeId tenantId, OrgNodeReadModel node)
    {
        if (node.TenantId != tenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
    }

    private static void ValidateChildType(OrgNodeType parent, OrgNodeType child)
    {
        if (!OrgRules.CanContain(parent, child))
            throw new DomainException($"{parent} cannot contain {child}.");
    }
}

public sealed class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
