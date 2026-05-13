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
        if (!actor.IsSuperAdmin)
            throw new DomainException("Only SuperAdmin can create brands.");

        var id = OrgNodeId.New();
        await using var session = store.LightweightSession(id.Value.ToString());
        var normalized = ValidateCode(OrgNodeType.Brand, code);
        await EnsureCodeAvailableAsync(session, id, normalized, ct);

        var node = NewNode(id, id, (OrgNodeReadModel?)null, OrgNodeType.Brand, code.Trim(), normalized, name.Trim(), [], []);
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id,
            id,
            null,
            OrgNodeType.Brand,
            node.Code,
            normalized,
            node.Name));
        session.Store(node);
        session.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(id, normalized),
            TenantId = id.Value,
            CodeNormalized = normalized,
            OrgNodeId = id.Value
        });

        await session.SaveChangesAsync(ct);
        return node;
    }

    public async Task<OrgNodeReadModel> CreateBrandAsync(
        IDocumentSession session,
        ActorContext actor,
        string code,
        string name,
        CancellationToken ct)
    {
        if (!actor.IsSuperAdmin)
            throw new DomainException("Only SuperAdmin can create brands.");

        var id = OrgNodeId.New();
        var normalized = ValidateCode(OrgNodeType.Brand, code);
        await EnsureCodeAvailableAsync(session, id, normalized, ct);

        var node = NewNode(id, id, (OrgNodeReadModel?)null, OrgNodeType.Brand, code.Trim(), normalized, name.Trim(), [], []);
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id,
            id,
            null,
            OrgNodeType.Brand,
            node.Code,
            normalized,
            node.Name));
        session.Store(node);
        session.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(id, normalized),
            TenantId = id.Value,
            CodeNormalized = normalized,
            OrgNodeId = id.Value
        });

        await session.SaveChangesAsync(ct);
        return node;
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
        await RequireCreateAuthorityAsync(session, actor, parent, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, type);

        var normalized = ValidateCode(type, code);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        var node = NewNode(id, tenantId, parent, type, code.Trim(), normalized, name.Trim(), parent.AncestorIds, parent.AncestorPublicIds);
        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentId, type, node.Code, normalized, node.Name));
        session.Store(node);
        session.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(tenantId, normalized),
            TenantId = tenantId.Value,
            CodeNormalized = normalized,
            OrgNodeId = id.Value
        });

        await session.SaveChangesAsync(ct);
        return node;
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
        await RequireCreateAuthorityAsync(session, actor, parent, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, OrgNodeType.Store);

        var normalized = ValidateCode(OrgNodeType.Store, code);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        var node = NewNode(id, tenantId, parent, OrgNodeType.Store, code.Trim(), normalized, name.Trim(), parent.AncestorIds, parent.AncestorPublicIds);
        var profile = new StoreProfileReadModel { Id = id.Value, TenantId = tenantId.Value, TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim() };

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentId, OrgNodeType.Store, node.Code, normalized, node.Name));
        session.Store(node);
        session.Store(profile);
        session.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(tenantId, normalized),
            TenantId = tenantId.Value,
            CodeNormalized = normalized,
            OrgNodeId = id.Value
        });

        await session.SaveChangesAsync(ct);
        return node;
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
        await RequireCreateAuthorityAsync(session, actor, parent, ct);
        ValidateTenant(tenantId, parent);
        ValidateChildType(parent.Type, OrgNodeType.Device);

        var normalized = ValidateCode(OrgNodeType.Device, serialNumber);
        await EnsureCodeAvailableAsync(session, tenantId, normalized, ct);

        var id = OrgNodeId.New();
        var node = NewNode(id, tenantId, parent, OrgNodeType.Device, serialNumber.Trim(), normalized, name.Trim(), parent.AncestorIds, parent.AncestorPublicIds);
        var profile = new DeviceProfileReadModel
        {
            Id = id.Value,
            TenantId = tenantId.Value,
            SerialNumber = serialNumber.Trim(),
            SerialNumberNormalized = normalized,
            DeviceType = deviceType
        };

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(id, tenantId, parentStoreId, OrgNodeType.Device, node.Code, normalized, node.Name));
        session.Store(node);
        session.Store(profile);
        session.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(tenantId, normalized),
            TenantId = tenantId.Value,
            CodeNormalized = normalized,
            OrgNodeId = id.Value
        });

        await session.SaveChangesAsync(ct);
        return node;
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
        if (!actor.IsSuperAdmin)
            throw new DomainException("Only SuperAdmin can provision subjects directly.");

        var id = SubjectId.New();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var subject = new SubjectReadModel
        {
            Id = id.Value,
            PublicId = OrgPublicId.FormatSubject(id),
            Kind = SubjectKind.User,
            Email = email.Trim(),
            EmailNormalized = normalizedEmail,
            DisplayName = displayName?.Trim(),
            Active = true
        };
        var link = new ExternalIdentityReadModel
        {
            Id = ExternalIdentityReadModel.BuildId(provider, providerTenant, externalId),
            SubjectId = id.Value,
            Provider = provider,
            ProviderTenant = providerTenant,
            ExternalId = externalId
        };

        session.Events.StartStream<SubjectReadModel>(id.Value, new SubjectCreated(id, SubjectKind.User, subject.Email, subject.DisplayName));
        session.Events.Append(id.Value, new ExternalIdentityLinked(id, provider, providerTenant, externalId));
        session.Store(subject);
        session.Store(link);
        await session.SaveChangesAsync(ct);
        return subject;
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

        if (role == OrgRole.Owner)
        {
            if (!actor.IsSuperAdmin)
                throw new DomainException("Only SuperAdmin can grant Owner.");
        }
        else
        {
            var authorized = await authorization.RequireRoleAsync(session, actor, target, OrgRole.Admin, TimeProvider.System.GetUtcNow(), ct);
            if (!authorized.Succeeded)
                throw new DomainException(authorized.Error!);
        }

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

            session.Events.Append(id, new OrgAccessRoleChanged(tenantId, orgNodeId, subjectId, grant.Role, role));
            grant.Role = role;
            grant.ExpiresAt = expiresAt;
            session.Store(grant);
            await session.SaveChangesAsync(ct);
            return grant;
        }

        grant = new OrgAccessGrantReadModel
        {
            Id = id,
            TenantId = tenantId.Value,
            OrgNodeId = orgNodeId.Value,
            SubjectId = subjectId.Value,
            Role = role,
            ExpiresAt = expiresAt,
            Status = OrgAccessGrantStatus.Active,
            Source = source,
            InvitationId = invitationId?.Value
        };

        session.Events.StartStream<OrgAccessGrantReadModel>(Guid.CreateVersion7(), new OrgAccessGranted(tenantId, orgNodeId, subjectId, role, expiresAt, source, invitationId));
        session.Store(grant);
        await session.SaveChangesAsync(ct);
        return grant;
    }

    private static OrgNodeReadModel NewNode(
        OrgNodeId id,
        OrgNodeId tenantId,
        OrgNodeReadModel? parent,
        OrgNodeType type,
        string code,
        string normalized,
        string name,
        IReadOnlyList<Guid> parentAncestorIds,
        IReadOnlyList<string> parentAncestorPublicIds)
    {
        var publicId = OrgPublicId.Format(type, id);
        var ancestors = parent is null ? [] : parentAncestorIds.Append(parent.Id).ToList();
        var ancestorPublicIds = parent is null ? [] : parentAncestorPublicIds.Append(parent.PublicId).ToList();
        var segment = $"{type.ToString().ToLowerInvariant()}/{code}";
        var path = parent is null ? segment : $"{parent.Path}/{segment}";

        return new OrgNodeReadModel
        {
            Id = id.Value,
            PublicId = publicId,
            TenantId = tenantId.Value,
            TenantPublicId = OrgPublicId.Format(OrgNodeType.Brand, tenantId),
            ParentId = parent?.Id,
            ParentPublicId = parent?.PublicId,
            Type = type,
            Code = code,
            CodeNormalized = normalized,
            Name = name,
            AncestorIds = ancestors,
            AncestorPublicIds = ancestorPublicIds,
            Path = path
        };
    }

    private static OrgNodeReadModel NewNode(
        OrgNodeId id,
        OrgNodeId tenantId,
        OrgNodeId? parentId,
        OrgNodeType type,
        string code,
        string normalized,
        string name,
        IReadOnlyList<Guid> ancestorIds,
        IReadOnlyList<string> ancestorPublicIds)
    {
        return new OrgNodeReadModel
        {
            Id = id.Value,
            PublicId = OrgPublicId.Format(type, id),
            TenantId = tenantId.Value,
            TenantPublicId = OrgPublicId.Format(OrgNodeType.Brand, tenantId),
            ParentId = parentId?.Value,
            ParentPublicId = null,
            Type = type,
            Code = code,
            CodeNormalized = normalized,
            Name = name,
            AncestorIds = ancestorIds.ToList(),
            AncestorPublicIds = ancestorPublicIds.ToList(),
            Path = $"{type.ToString().ToLowerInvariant()}/{code}"
        };
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

    private async Task RequireCreateAuthorityAsync(IDocumentSession session, ActorContext actor, OrgNodeReadModel parent, CancellationToken ct)
    {
        var authorized = await authorization.RequireRoleAsync(session, actor, parent, OrgRole.Admin, TimeProvider.System.GetUtcNow(), ct);
        if (!authorized.Succeeded)
            throw new DomainException(authorized.Error!);
    }
}

public sealed class DomainException(string message) : Exception(message);
