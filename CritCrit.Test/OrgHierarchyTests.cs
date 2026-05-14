using System.Net;
using System.Text.Json;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public class OrgHierarchyTests(ApiFixture fixture) : ContractTestWithAlba(fixture)
{
    [Fact]
    public async Task superadmin_can_create_brand_store_and_device()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Burger Palace"));

        Assert.Equal(OrgNodeType.Brand, brand.Type);
        Assert.StartsWith("brand_", brand.Id, StringComparison.Ordinal);

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Main Street", "Europe/Lisbon"));

        Assert.Equal(OrgNodeType.Store, store.Type);
        Assert.Equal(brand.Id, store.ParentId);
        Assert.Contains("/store/", store.Path, StringComparison.Ordinal);

        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices",
            new CreateDeviceRequest(store.Id, "SN-AbC-123", "Front Kiosk", DeviceType.Kiosk));

        Assert.Equal(OrgNodeType.Device, device.Type);
        Assert.Equal("SN-AbC-123", device.Code);
        Assert.Equal(store.Id, device.ParentId);
    }

    [Fact]
    public async Task hierarchy_rules_reject_invalid_children()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateStoreRequest(store.Id, Code("store"), "Nested Store", "UTC"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/stores");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task grants_reject_redundant_descendant_access()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("critic@example.com", "Critic", "test", "default", "critic-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Authorization ──

    [Fact]
    public async Task non_superadmin_cannot_create_brand()
    {
        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Post.Json(new CreateBrandRequest(Code(), "Brand"), JsonStyle.MinimalApi).ToUrl("/api/platform/brands");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task non_superadmin_cannot_create_subject()
    {
        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Post.Json(new CreateSubjectRequest("user@example.com", "User", "test", "default", "regular-user"), JsonStyle.MinimalApi).ToUrl("/api/platform/subjects");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task non_admin_cannot_grant_role()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var granter = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("granter@example.com", "Granter", "test", "default", "granter-idp"));

        var target = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("target@example.com", "Target", "test", "default", "target-idp"));

        await Host.Scenario(_ =>
        {
            AsUser(_, "granter-idp", "granter@example.com");
            _.Post.Json(new GrantRoleRequest(store.Id, target.Id, OrgRole.Member, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task superadmin_can_grant_owner_at_brand()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));

        var grant = await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Owner, null));

        Assert.Equal(OrgRole.Owner, grant.Role);
    }

    // ── Data Integrity ──

    [Fact]
    public async Task code_uniqueness_enforced_within_tenant()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, "dup-store", "Store One", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateStoreRequest(brand.Id, "dup-store", "Store Two", "UTC"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/stores");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task same_code_allowed_across_tenants()
    {
        var brandA = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand A"));

        var brandB = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand B"));

        var storeA = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brandA.Id}/stores",
            new CreateStoreRequest(brandA.Id, "cross-tenant", "Store A", "UTC"));

        var storeB = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brandB.Id}/stores",
            new CreateStoreRequest(brandB.Id, "cross-tenant", "Store B", "UTC"));

        Assert.NotEqual(storeA.Id, storeB.Id);
    }

    [Fact]
    public async Task tenant_isolation_node_not_found_via_wrong_brand()
    {
        var brandA = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand A"));

        var brandB = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand B"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brandA.Id}/stores",
            new CreateStoreRequest(brandA.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brandB.Id}/org-nodes/{store.Id}");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task invalid_parent_type_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateDeviceRequest(brand.Id, "SN-001", "Device", DeviceType.Kiosk), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/devices");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task parent_not_found_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var fakeId = $"store_{Guid.CreateVersion7()}";

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateStoreRequest(fakeId, Code("store"), "Store", "UTC"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/stores");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Entity Retrieval ──

    [Fact]
    public async Task get_org_node_by_id()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/org-nodes/{store.Id}");
        });

        Assert.Equal(200, result.Context.Response.StatusCode);
        var node = await result.ReadAsJsonAsync<OrgNodeResponse>();
        Assert.Equal(store.Id, node!.Id);
    }

    [Fact]
    public async Task get_org_node_not_found()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var fakeId = $"store_{Guid.CreateVersion7()}";

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/org-nodes/{fakeId}");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task get_org_node_wrong_tenant()
    {
        var brandA = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand A"));

        var brandB = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand B"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brandA.Id}/stores",
            new CreateStoreRequest(brandA.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brandB.Id}/org-nodes/{store.Id}");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    // ── Grant Lifecycle ──

    [Fact]
    public async Task grant_role_escalation_from_member_to_admin()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("user@example.com", "User", "test", "default", "escalate-idp"));

        var grant = await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));
        Assert.Equal(OrgRole.Member, grant.Role);

        var escalated = await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));
        Assert.Equal(OrgRole.Admin, escalated.Role);
    }

    [Fact]
    public async Task grant_rejected_for_inactive_subject()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var fakeSubjectId = $"subj_{Guid.CreateVersion7()}";

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new GrantRoleRequest(brand.Id, fakeSubjectId, OrgRole.Member, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── FluentValidation ──

    [Fact]
    public async Task create_brand_empty_code_rejected()
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateBrandRequest("", "A Brand"), JsonStyle.MinimalApi).ToUrl("/api/platform/brands");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task create_brand_empty_name_rejected()
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateBrandRequest(Code(), ""), JsonStyle.MinimalApi).ToUrl("/api/platform/brands");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task create_subject_invalid_email_rejected()
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateSubjectRequest("not-an-email", "User", "test", "default", "some-id"), JsonStyle.MinimalApi).ToUrl("/api/platform/subjects");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task create_country_invalid_code_format_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreatePlainOrgNodeRequest(brand.Id, "usa", "United States"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/countries");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task create_country_with_valid_alpha2_code()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "PT", "Portugal"));

        Assert.Equal(OrgNodeType.Country, country.Type);
    }

    [Fact]
    public async Task superadmin_can_create_franchise()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var franchise = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/franchises",
            new CreatePlainOrgNodeRequest(brand.Id, Code("fr"), "Lisbon Franchise"));

        Assert.Equal(OrgNodeType.Franchise, franchise.Type);
        Assert.Equal(brand.Id, franchise.ParentId);
    }

    [Fact]
    public async Task superadmin_can_create_subject()
    {
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("test@example.com", "Test User", "test", "default", "test-idp"));

        Assert.StartsWith("subj_", subject.Id, StringComparison.Ordinal);
        Assert.Equal("test@example.com", subject.Email);
        Assert.Equal("Test User", subject.DisplayName);
    }

    [Fact]
    public async Task create_device_invalid_serial_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateDeviceRequest(store.Id, "", "Device", DeviceType.Kiosk), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/devices");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task create_device_requires_store_parent()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateDeviceRequest(brand.Id, "SN-001", "Device", DeviceType.Kiosk), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/devices");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Lifecycle: Archive ──

    [Fact]
    public async Task archive_store_success()
    {
        var (brand, store) = await CreateBrandAndStore();
        var result = await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive",
            new ArchiveOrgNodeRequest(false, "seasonal closure"), HttpStatusCode.OK);
        Assert.True(result.Archived);
    }

    [Fact]
    public async Task archive_force_cascades_to_children()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-FC-001", "Device", DeviceType.Kiosk));

        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive",
            new ArchiveOrgNodeRequest(true, "force cascade"), HttpStatusCode.OK);

        // Verify device is effectively archived
        var deviceAfter = await GetNode(brand.Id, device.Id);
        Assert.False(deviceAfter!.Archived);
        Assert.True(deviceAfter.EffectiveArchived);
    }

    [Fact]
    public async Task archive_without_force_rejected_when_has_children()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-NF-001", "Device", DeviceType.Kiosk));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ArchiveOrgNodeRequest(false, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task archive_already_archived_rejected()
    {
        var (brand, store) = await CreateBrandAndStore();
        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive",
            new ArchiveOrgNodeRequest(false, null), HttpStatusCode.OK);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ArchiveOrgNodeRequest(false, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task archive_nonexistent_node_returns_error()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var fakeId = $"store_{Guid.CreateVersion7()}";

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ArchiveOrgNodeRequest(false, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{fakeId}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Lifecycle: Restore ──

    [Fact]
    public async Task restore_archived_store()
    {
        var (brand, store) = await CreateBrandAndStore();
        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive",
            new ArchiveOrgNodeRequest(false, null), HttpStatusCode.OK);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/restore");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task restore_not_archived_rejected()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/restore");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task restore_hard_deleted_rejected()
    {
        var (brand, store) = await CreateBrandAndStore();
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("test"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/restore");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Lifecycle: Hard Delete ──

    [Fact]
    public async Task hard_delete_store_cascades_to_device()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-HD-001", "Device", DeviceType.Kiosk));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("cleanup"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task hard_delete_already_deleted_rejected()
    {
        var (brand, store) = await CreateBrandAndStore();
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("first"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("second"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task hard_delete_reason_required()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest(""), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    // ── Lifecycle: Move ──

    [Fact]
    public async Task move_store_to_country()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "DE", "Germany"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var moved = await PostAsSuperAdmin<MoveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/move",
            new MoveOrgNodeRequest(country.Id, "reorg"), HttpStatusCode.OK);

        Assert.Equal(country.Id, moved.ParentId);
    }

    [Fact]
    public async Task move_to_same_parent_rejected()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new MoveOrgNodeRequest(brand.Id, "same"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/move");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task move_under_own_descendant_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-MD-001", "Device", DeviceType.Kiosk));

        // Try to move store under its own device (cycle)
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new MoveOrgNodeRequest(device.Id, "cycle"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/move");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task move_to_invalid_parent_type_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-MT-001", "Device", DeviceType.Kiosk));

        // Try to move store under a device (invalid: Store cannot be under Device)
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new MoveOrgNodeRequest(device.Id, "invalid"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/move");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task move_updates_descendant_paths()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "FR", "France"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Paris Store", "UTC"));
        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices", new CreateDeviceRequest(store.Id, "SN-MP-001", "Kiosk 1", DeviceType.Kiosk));

        await PostAsSuperAdmin<MoveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/move",
            new MoveOrgNodeRequest(country.Id, "reorg"), HttpStatusCode.OK);

        // Verify device path updated
        var deviceAfter = await GetNode(brand.Id, device.Id);
        Assert.Contains("/country/", deviceAfter!.Path, StringComparison.Ordinal);
    }

    // ── Lifecycle: Authorization ──

    [Fact]
    public async Task non_admin_cannot_archive()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Post.Json(new ArchiveOrgNodeRequest(false, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task non_admin_cannot_move()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "PT", "Portugal"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Post.Json(new MoveOrgNodeRequest(country.Id, "reorg"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/move");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task non_superadmin_cannot_hard_delete()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Post.Json(new HardDeleteOrgNodeRequest("test"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    // ── Helpers ──

    private async Task<(OrgNodeResponse Brand, OrgNodeResponse Store)> CreateBrandAndStore()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code("lb"), "Lifecycle Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Lifecycle Store", "UTC"));
        return (brand, store);
    }

    private async Task<OrgNodeResponse?> GetNode(string brandId, string nodeId)
    {
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brandId}/org-nodes/{nodeId}");
        });
        if (result.Context.Response.StatusCode != 200)
            return null;
        return await result.ReadAsJsonAsync<OrgNodeResponse>();
    }

    private async Task<IScenarioResult> PostAsSuperAdminWithoutBody<TRequest>(string url, TRequest request)
    {
        return await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(request!, JsonStyle.MinimalApi).ToUrl(url);
        });
    }

    private async Task PostAsSuperAdminNoBody(string url)
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, JsonStyle.MinimalApi).ToUrl(url);
        });
    }

    private async Task<TResponse> PostAsSuperAdmin<TRequest, TResponse>(string url, TRequest request)
    {
        return await PostAsSuperAdmin<TRequest, TResponse>(url, request, HttpStatusCode.Created);
    }

    private async Task<TResponse> PostAsSuperAdmin<TRequest, TResponse>(string url, TRequest request, HttpStatusCode expectedStatus)
    {
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(request!, JsonStyle.MinimalApi).ToUrl(url);
            _.StatusCodeShouldBe(expectedStatus);
        });

        var body = await result.ReadAsTextAsync();
        return JsonSerializer.Deserialize<TResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static void AsSuperAdmin(Scenario scenario)
    {
        scenario.WithRequestHeader("X-Test-User", "superadmin-idp");
        scenario.WithRequestHeader("X-Test-Email", "superadmin@example.com");
        scenario.WithRequestHeader("X-Test-Roles", "critcrit.superadmin");
    }

    private static void AsPlainUser(Scenario scenario, string externalId)
    {
        scenario.WithRequestHeader("X-Test-User", externalId);
        scenario.WithRequestHeader("X-Test-Email", $"{externalId}@example.com");
    }

    private static void AsUser(Scenario scenario, string externalId, string email)
    {
        scenario.WithRequestHeader("X-Test-User", externalId);
        scenario.WithRequestHeader("X-Test-Email", email);
    }

    private static string Code(string prefix = "brand") => $"{prefix}-{Guid.NewGuid():N}"[..32];
}
