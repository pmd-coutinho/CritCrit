using System.Net;
using System.Text.Json;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Identity;
using CritCrit.Api.Org.Invitations;
using CritCrit.Api.Platform.Audit;
using CritCrit.Test.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using CritCrit.Api.Org.Features.Invitations;
using Wolverine;

namespace CritCrit.Test;

public class OrgHierarchyTests : ContractTestWithAlba
{
    public OrgHierarchyTests(ApiFixture fixture) : base(fixture)
    {
        var store = Host.Services.GetRequiredService<TestInvitationEmailStore>();
        store.Sent.Clear();
        store.FailAll = false;
        store.FailInvitationIds.Clear();
    }
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

    // ── Invitations ──

    [Fact]
    public async Task create_invitation_provisions_subject_and_sends_email()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "invitee@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        Assert.Equal("invitee@example.com", pending.Email);
        Assert.NotNull(pending.SubjectId);
        Assert.NotNull(pending.ExpiresAt);

        var sent = await WaitForSentInvitationEmailAsync(invitation.Id);
        Assert.Equal("invitee@example.com", sent.To);
        Assert.Contains("/accept-invite?token=", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task accepting_invitation_marks_subject_onboarded_and_creates_grant()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "accepted@example.com", OrgRole.Admin),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        var providerUser = FindProviderUser("accepted@example.com");
        var token = ExtractToken((await WaitForSentInvitationEmailAsync(invitation.Id)).Body);

        var accepted = await Host.Scenario(_ =>
        {
            AsUser(_, providerUser.ExternalId, "accepted@example.com");
            _.Get.Url($"/api/invitations/accept?token={Uri.EscapeDataString(token)}");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await accepted.ReadAsJsonAsync<AcceptInvitationResponse>();
        Assert.NotNull(result);
        Assert.True(result!.GrantCreated);
        Assert.True(result.SubjectOnboarded);
        Assert.Equal(InvitationStatus.Accepted, result.Status);

        var subject = await LoadSubjectByEmailAsync("accepted@example.com");
        Assert.NotNull(subject!.OnboardedAt);

        await using var tenantQuery = DocumentStore.QuerySession(ParseOrgNodeGuid(brand.Id).ToString());
        var grants = await tenantQuery.Query<OrgAccessGrantReadModel>()
            .Where(x => x.SubjectId == subject.Id && x.OrgNodeId == ParseOrgNodeGuid(brand.Id))
            .ToListAsync();
        Assert.Single(grants);
        Assert.Equal(OrgRole.Admin, grants[0].Role);
        Assert.Equal(OrgAccessGrantSource.Invitation, grants[0].Source);
    }

    [Fact]
    public async Task reinviting_an_onboarded_subject_to_a_new_node_is_allowed()
    {
        // Onboard the user with an initial Member grant at the brand root.
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var first = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "returner@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var providerUser = FindProviderUser("returner@example.com");
        var firstToken = ExtractToken((await WaitForSentInvitationEmailAsync(first.Id)).Body);
        await Host.Scenario(_ =>
        {
            AsUser(_, providerUser.ExternalId, "returner@example.com");
            _.Get.Url($"/api/invitations/accept?token={Uri.EscapeDataString(firstToken)}");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // Now create a country and re-invite the same email there. Should succeed
        // because the subject is active — onboarded-status no longer blocks it.
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "us", "United States"));

        var second = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(country.Id, "returner@example.com", OrgRole.Admin),
            HttpStatusCode.Accepted);

        await WaitForInvitationAsync(second.Id, InvitationStatus.Pending);
    }

    [Fact]
    public async Task inviting_a_deactivated_subject_is_rejected()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        // Create and deactivate the subject directly via the platform endpoints.
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("offboarded@example.com", null, "test", "default", "off-ext"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new DeactivateSubjectRequest("offboarding"), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/subjects/{subject.Id}/deactivate");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateInvitationRequest(brand.Id, "offboarded@example.com", OrgRole.Member), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/invitations");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task second_pending_invitation_for_same_node_supersedes_first()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var first = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "supersede@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        await WaitForInvitationAsync(first.Id, InvitationStatus.Pending);

        var second = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "supersede@example.com", OrgRole.Admin),
            HttpStatusCode.Accepted);

        var superseded = await WaitForInvitationAsync(first.Id, InvitationStatus.Superseded);
        var replacement = await WaitForInvitationAsync(second.Id, InvitationStatus.Pending);

        Assert.Equal(InvitationStatus.Superseded, superseded.Status);
        Assert.Equal(OrgRole.Admin, replacement.Role);
    }

    [Fact]
    public async Task first_acceptance_auto_applies_other_pending_invitations()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var storeInvitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(store.Id, "multi@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var brandInvitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "multi@example.com", OrgRole.Admin),
            HttpStatusCode.Accepted);

        await WaitForInvitationAsync(storeInvitation.Id, InvitationStatus.Pending);
        await WaitForInvitationAsync(brandInvitation.Id, InvitationStatus.Pending);

        var providerUser = FindProviderUser("multi@example.com");
        var token = ExtractToken((await WaitForSentInvitationEmailAsync(storeInvitation.Id)).Body);

        var accepted = await Host.Scenario(_ =>
        {
            AsUser(_, providerUser.ExternalId, "multi@example.com");
            _.Get.Url($"/api/invitations/accept?token={Uri.EscapeDataString(token)}");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await accepted.ReadAsJsonAsync<AcceptInvitationResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result!.AutoAppliedInvitations);

        var autoApplied = await WaitForInvitationAsync(brandInvitation.Id, InvitationStatus.AutoApplied);
        Assert.Equal(InvitationStatus.AutoApplied, autoApplied.Status);

        var subject = await LoadSubjectByEmailAsync("multi@example.com");
        await using var tenantQuery = DocumentStore.QuerySession(ParseOrgNodeGuid(brand.Id).ToString());
        var grants = await tenantQuery.Query<OrgAccessGrantReadModel>()
            .Where(x => x.SubjectId == subject!.Id)
            .ToListAsync();

        Assert.Contains(grants, x => x.OrgNodeId == ParseOrgNodeGuid(brand.Id) && x.Role == OrgRole.Admin);
        Assert.Contains(grants, x => x.OrgNodeId == ParseOrgNodeGuid(store.Id) && x.Role == OrgRole.Member);
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

    [Fact]
    public async Task brand_owner_can_hard_delete_store()
    {
        var (brand, store) = await CreateBrandAndStore();
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "owner-idp", "owner@example.com");
            _.Post.Json(new HardDeleteOrgNodeRequest("owner cleanup"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task hard_deleted_node_is_hidden_from_get()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("cleanup"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/org-nodes/{store.Id}");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
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

    [Fact]
    public async Task restoring_parent_reactivates_descendants_that_were_only_effectively_archived()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "PT", "Portugal"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(country.Id, Code("store"), "Store", "UTC"));

        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{country.Id}/archive",
            new ArchiveOrgNodeRequest(true, "temporary closure"), HttpStatusCode.OK);

        var archivedStore = await GetNode(brand.Id, store.Id);
        Assert.NotNull(archivedStore);
        Assert.True(archivedStore!.EffectiveArchived);
        Assert.False(archivedStore.Archived);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/org-nodes/{country.Id}/restore");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var restoredStore = await GetNode(brand.Id, store.Id);
        Assert.NotNull(restoredStore);
        Assert.False(restoredStore!.EffectiveArchived);
        Assert.False(restoredStore.Archived);
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

    [Fact]
    public async Task brand_admin_cannot_archive_brand()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Post.Json(new ArchiveOrgNodeRequest(true, "tenant shutdown"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task admin_cannot_downgrade_owner_grant()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Post.Json(new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Member, null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    // ── Audit ──

    [Fact]
    public async Task hard_delete_writes_immutable_audit_event()
    {
        var (brand, store) = await CreateBrandAndStore();

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("cleanup"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{store.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        var audit = Assert.Single(await LoadAuditEvents(
            OrgAuditActions.HardDeleteSubtree,
            ParseOrgNodeGuid(store.Id)));
        Assert.Equal(ParseOrgNodeGuid(brand.Id), audit.TenantId);
        Assert.Equal(ParseOrgNodeGuid(store.Id), audit.TargetOrgNodeId);
        Assert.Equal("cleanup", audit.Reason);
        Assert.Equal("superadmin-idp", audit.ActorExternalId);
    }

    [Fact]
    public async Task move_writes_immutable_audit_event()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "ES", "Spain"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        await PostAsSuperAdmin<MoveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/move",
            new MoveOrgNodeRequest(country.Id, "regional reorg"), HttpStatusCode.OK);

        var audit = Assert.Single(await LoadAuditEvents(
            OrgAuditActions.OrgNodeMove,
            ParseOrgNodeGuid(store.Id)));
        Assert.Equal(ParseOrgNodeGuid(brand.Id), audit.TenantId);
        Assert.Equal(ParseOrgNodeGuid(store.Id), audit.TargetOrgNodeId);
        Assert.Equal("regional reorg", audit.Reason);
    }

    [Fact]
    public async Task owner_grant_writes_immutable_audit_event()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));

        var audit = Assert.Single(await LoadAuditEvents(
            OrgAuditActions.OwnerGranted,
            ParseOrgNodeGuid(brand.Id)));
        Assert.Equal(ParseOrgNodeGuid(brand.Id), audit.TargetOrgNodeId);
        Assert.Equal("superadmin-idp", audit.ActorExternalId);
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
        return JsonSerializer.Deserialize<TResponse>(body, TestJsonOptions)!;
    }

    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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

    private IDocumentStore DocumentStore => Host.Services.GetRequiredService<IDocumentStore>();

    private async Task<IReadOnlyList<ImmutableAuditEvent>> LoadAuditEvents(string action, Guid targetOrgNodeId)
    {
        await using var query = DocumentStore.QuerySession();
        return await query.Query<ImmutableAuditEvent>()
            .Where(x => x.Action == action && x.TargetOrgNodeId == targetOrgNodeId)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync();
    }

    private static Guid ParseOrgNodeGuid(string publicId)
    {
        var parsed = OrgPublicId.TryParseOrgNode(publicId, out var id, out _);
        Assert.True(parsed);
        return id.Value;
    }

    private static string Code(string prefix = "brand") => $"{prefix}-{Guid.NewGuid():N}"[..32];

    private TestInvitationEmailStore InvitationEmailStore => Host.Services.GetRequiredService<TestInvitationEmailStore>();

    private TestIdentityProviderStore IdentityProviderStore => Host.Services.GetRequiredService<TestIdentityProviderStore>();

    private async Task<InvitationResponse> WaitForInvitationAsync(string invitationId, InvitationStatus status)
    {
        for (var i = 0; i < 50; i++)
        {
            await using var query = DocumentStore.QuerySession();
            var invitation = await query.LoadAsync<InvitationReadModel>(ParseInvitationGuid(invitationId));
            if (invitation is not null && invitation.Status == status)
                return new InvitationResponse(
                    invitation.PublicId,
                    invitation.TenantPublicId,
                    invitation.TargetOrgNodePublicId,
                    invitation.Email,
                    invitation.SubjectPublicId,
                    invitation.Role,
                    invitation.Status,
                    invitation.CreatedAt,
                    invitation.ExpiresAt,
                    invitation.AcceptedAt,
                    invitation.LastSentAt,
                    invitation.Failure);

            await Task.Delay(50);
        }

        throw new TimeoutException($"Invitation {invitationId} did not reach status {status}.");
    }

    private async Task<InvitationEmailMessage> WaitForSentInvitationEmailAsync(string invitationId)
    {
        for (var i = 0; i < 50; i++)
        {
            var sent = InvitationEmailStore.Sent.LastOrDefault(x => x.InvitationId == ParseInvitationGuid(invitationId));
            if (sent is not null)
                return sent;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Invitation email was not sent for {invitationId}.");
    }

    private FakeIdentityProviderUser FindProviderUser(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        Assert.True(IdentityProviderStore.Users.TryGetValue(normalized, out var user));
        return user!;
    }

    private async Task<SubjectReadModel?> LoadSubjectByEmailAsync(string email)
    {
        await using var query = DocumentStore.QuerySession();
        return await query.Query<SubjectReadModel>()
            .Where(x => x.EmailNormalized == email.Trim().ToLowerInvariant())
            .SingleOrDefaultAsync();
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-=]+)");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static Guid ParseInvitationGuid(string publicId)
    {
        var parsed = OrgPublicId.TryParseInvitation(publicId, out var id);
        Assert.True(parsed);
        return id.Value;
    }

    private static Guid ParseSubjectGuid(string publicId)
    {
        var parsed = OrgPublicId.TryParseSubject(publicId, out var id);
        Assert.True(parsed);
        return id.Value;
    }

    private async Task<OrgAccessGrantReadModel?> WaitForGrantStatusAsync(string tenantId, string grantId, OrgAccessGrantStatus expectedStatus)
    {
        for (var i = 0; i < 50; i++)
        {
            await using var query = DocumentStore.QuerySession(tenantId);
            var grant = await query.LoadAsync<OrgAccessGrantReadModel>(grantId);
            if (grant is not null && grant.Status == expectedStatus)
                return grant;
            await Task.Delay(50);
        }
        return null;
    }

    // ── Grant Expiration ──

    [Fact]
    public async Task grant_expiration_endpoint_changes_expires_at()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("exp@example.com", "Exp", "test", "default", "exp-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        var expiresAt = TimeProvider.System.GetUtcNow().AddHours(1);
        var updated = await PostAsSuperAdmin<SetGrantExpirationRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants/expiration",
            new SetGrantExpirationRequest(brand.Id, subject.Id, expiresAt), HttpStatusCode.OK);

        Assert.Equal(expiresAt, updated.ExpiresAt);
    }

    [Fact]
    public async Task generic_grant_endpoint_rejects_expires_at_change_on_active_grant()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("block@example.com", "Block", "test", "default", "block-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, TimeProvider.System.GetUtcNow().AddHours(1)), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task scheduled_grant_expiry_appends_org_access_expired()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("sched@example.com", "Sched", "test", "default", "sched-idp"));

        var grant = await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, TimeProvider.System.GetUtcNow().AddHours(1)));

        var tenantId = ParseOrgNodeGuid(brand.Id);
        var nodeId = new OrgNodeId(tenantId);
        var subjectId = ParseSubjectGuid(subject.Id);
        var grantId = OrgAccessGrantReadModel.BuildId(nodeId, nodeId, new SubjectId(subjectId));

        // Directly set expiration to the past via event so we can deterministically test expiry
        await using var directSession = DocumentStore.LightweightSession(tenantId.ToString());
        var past = TimeProvider.System.GetUtcNow().AddSeconds(-1);
        var readGrant = await directSession.LoadAsync<OrgAccessGrantReadModel>(grantId);
        directSession.Events.Append(readGrant!.StreamId,
            new OrgAccessExpirationChanged(nodeId, nodeId, new SubjectId(subjectId), readGrant.ExpiresAt, past));
        await directSession.SaveChangesAsync();

        var bus = Host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new ExpireGrant(nodeId, nodeId, new SubjectId(subjectId), past));

        await using var verify = DocumentStore.QuerySession(tenantId.ToString());
        var after = await verify.LoadAsync<OrgAccessGrantReadModel>(grantId);
        Assert.NotNull(after);
        Assert.Equal(OrgAccessGrantStatus.Expired, after.Status);
    }

    [Fact]
    public async Task expired_grant_disappears_from_effective_authorization()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("eff@example.com", "Eff", "test", "default", "eff-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        var tenantId = ParseOrgNodeGuid(brand.Id);
        var nodeId = new OrgNodeId(tenantId);
        var subjectId = ParseSubjectGuid(subject.Id);
        var grantId = OrgAccessGrantReadModel.BuildId(nodeId, nodeId, new SubjectId(subjectId));

        // Expire the grant directly
        await using var directSession = DocumentStore.LightweightSession(tenantId.ToString());
        var past = TimeProvider.System.GetUtcNow().AddSeconds(-1);
        var readGrant = await directSession.LoadAsync<OrgAccessGrantReadModel>(grantId);
        directSession.Events.Append(readGrant!.StreamId,
            new OrgAccessExpirationChanged(nodeId, nodeId, new SubjectId(subjectId), readGrant.ExpiresAt, past));
        await directSession.SaveChangesAsync();

        var bus = Host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new ExpireGrant(nodeId, nodeId, new SubjectId(subjectId), past));

        var auth = Host.Services.GetRequiredService<OrgAuthorizationService>();
        await using var querySession = DocumentStore.QuerySession(tenantId.ToString());
        var target = await querySession.LoadAsync<OrgNodeReadModel>(tenantId);
        var role = await auth.GetEffectiveRoleAsync(querySession, target!, new SubjectId(subjectId), TimeProvider.System.GetUtcNow(), default);
        Assert.Null(role);
    }

    // ── Redundant Cleanup ──

    [Fact]
    public async Task ancestor_grant_revokes_redundant_descendant_grants()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("red@example.com", "Red", "test", "default", "red-idp"));

        // Grant Member at store first, then Admin at brand -> store grant becomes redundant
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        // Poll for background cleanup to revoke the descendant grant
        var tenantId = ParseOrgNodeGuid(brand.Id);
        var storeGrantId = OrgAccessGrantReadModel.BuildId(
            new OrgNodeId(tenantId), new OrgNodeId(ParseOrgNodeGuid(store.Id)), new SubjectId(ParseSubjectGuid(subject.Id)));
        var after = await WaitForGrantStatusAsync(tenantId.ToString(), storeGrantId, OrgAccessGrantStatus.Revoked);
        Assert.NotNull(after);
        Assert.Equal(OrgAccessGrantStatus.Revoked, after.Status);
    }

    [Fact]
    public async Task upgraded_role_change_revokes_redundant_descendants()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("upg@example.com", "Upg", "test", "default", "upg-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        // Upgrade brand grant to Admin (triggers background cleanup)
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        // Poll for descendant grant to be revoked
        var tenantId = ParseOrgNodeGuid(brand.Id);
        var storeGrantId = OrgAccessGrantReadModel.BuildId(
            new OrgNodeId(tenantId), new OrgNodeId(ParseOrgNodeGuid(store.Id)), new SubjectId(ParseSubjectGuid(subject.Id)));
        var after = await WaitForGrantStatusAsync(tenantId.ToString(), storeGrantId, OrgAccessGrantStatus.Revoked);
        Assert.NotNull(after);
        Assert.Equal(OrgAccessGrantStatus.Revoked, after.Status);
    }

    [Fact]
    public async Task move_into_stronger_ancestry_revokes_redundant_subtree_grants()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var countryA = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries", new CreatePlainOrgNodeRequest(brand.Id, "DE", "Germany"));
        var countryB = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries", new CreatePlainOrgNodeRequest(brand.Id, "FR", "France"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(countryB.Id, Code("store"), "Store", "UTC"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("mv@example.com", "Mv", "test", "default", "mv-idp"));

        // Grant Member at store (under countryB with no grant)
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null));
        // Grant Admin at countryA
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(countryA.Id, subject.Id, OrgRole.Admin, null));

        // Move store under countryA -> store Member becomes redundant
        await PostAsSuperAdmin<MoveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/move",
            new MoveOrgNodeRequest(countryA.Id, "reorg"), HttpStatusCode.OK);

        // Move handler does inline cleanup; verify result
        var tenantId = ParseOrgNodeGuid(brand.Id);
        await using var verify = DocumentStore.QuerySession(tenantId.ToString());
        var storeGrantId = OrgAccessGrantReadModel.BuildId(
            new OrgNodeId(tenantId), new OrgNodeId(ParseOrgNodeGuid(store.Id)), new SubjectId(ParseSubjectGuid(subject.Id)));
        var after = await verify.LoadAsync<OrgAccessGrantReadModel>(storeGrantId);
        Assert.NotNull(after);
        Assert.Equal(OrgAccessGrantStatus.Revoked, after.Status);
    }

    [Fact]
    public async Task cleanup_leaves_historical_grant_rows_with_revoked_status()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("hist@example.com", "Hist", "test", "default", "hist-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        // Poll for background cleanup
        var tenantId = ParseOrgNodeGuid(brand.Id);
        var storeGrantId = OrgAccessGrantReadModel.BuildId(
            new OrgNodeId(tenantId), new OrgNodeId(ParseOrgNodeGuid(store.Id)), new SubjectId(ParseSubjectGuid(subject.Id)));
        var after = await WaitForGrantStatusAsync(tenantId.ToString(), storeGrantId, OrgAccessGrantStatus.Revoked);
        Assert.NotNull(after);
        Assert.Equal(OrgAccessGrantStatus.Revoked, after.Status);
        Assert.NotEqual(Guid.Empty, after.StreamId);
    }

    // ── Audit APIs ──

    [Fact]
    public async Task platform_audit_is_superadmin_only()
    {
        await Host.Scenario(_ =>
        {
            AsPlainUser(_, "regular-user");
            _.Get.Url("/api/platform/audit");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task brand_audit_is_owner_or_superadmin()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        // Admin should be forbidden
        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Get.Url($"/api/brands/{brand.Id}/audit");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });

        // Owner should succeed
        await Host.Scenario(_ =>
        {
            AsUser(_, "owner-idp", "owner@example.com");
            _.Get.Url($"/api/brands/{brand.Id}/audit");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // SuperAdmin should succeed
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/audit");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task audit_filters_and_pagination_work()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores", new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        // Perform actions that write audit: create a country and move store under it
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries", new CreatePlainOrgNodeRequest(brand.Id, "PT", "Portugal"));
        await PostAsSuperAdmin<MoveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{store.Id}/move",
            new MoveOrgNodeRequest(country.Id, "test move"), HttpStatusCode.OK);

        var tenantId = ParseOrgNodeGuid(brand.Id);
        var storeId = ParseOrgNodeGuid(store.Id);

        // Filter by action
        var actionResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/audit?action={OrgAuditActions.OrgNodeMove}");
        });
        Assert.Equal(200, actionResult.Context.Response.StatusCode);
        var actionItems = await actionResult.ReadAsJsonAsync<List<AuditEventResponse>>();
        Assert.NotNull(actionItems);
        Assert.All(actionItems!, x => Assert.Equal(OrgAuditActions.OrgNodeMove, x.Action));

        // Filter by targetOrgNodeId
        var targetResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/audit?targetOrgNodeId={store.Id}");
        });
        var targetItems = await targetResult.ReadAsJsonAsync<List<AuditEventResponse>>();
        Assert.NotNull(targetItems);
        Assert.All(targetItems!, x => Assert.Equal(storeId, x.TargetOrgNodeId));

        // Pagination with limit
        var pageResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/audit?limit=1");
        });
        var pageItems = await pageResult.ReadAsJsonAsync<List<AuditEventResponse>>();
        Assert.NotNull(pageItems);
        Assert.Single(pageItems!);
    }

    [Fact]
    public async Task archived_brand_audit_route_remains_accessible()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));

        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/archive",
            new ArchiveOrgNodeRequest(true, "archive test"), HttpStatusCode.OK);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/audit");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    // ── Brand Tombstones ──

    [Fact]
    public async Task hard_deleted_known_brand_returns_410()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("cleanup"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/hard-delete");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/org-nodes/{brand.Id}");
            _.StatusCodeShouldBe(HttpStatusCode.Gone);
        });
    }

    [Fact]
    public async Task unknown_brand_returns_404()
    {
        var fakeBrandId = $"brand_{Guid.CreateVersion7()}";
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{fakeBrandId}/org-nodes/{fakeBrandId}");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    // ── Owner Lifecycle ──

    [Fact]
    public async Task dedicated_owner_grant_endpoint_enforces_superadmin()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));
        var target = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("target@example.com", "Target", "test", "default", "target-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Post.Json(new GrantOwnerRequest(target.Id), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/owners");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task dedicated_owner_downgrade_enforces_superadmin()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Post.Json(new DowngradeOwnerRequest(OrgRole.Admin, "downgrade test"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/owners/{owner.Id}/downgrade");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task dedicated_owner_revoke_enforces_superadmin()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));
        var admin = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("admin@example.com", "Admin", "test", "default", "admin-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, admin.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "admin-idp", "admin@example.com");
            _.Post.Json(new RevokeOwnerRequest("revoke test"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/owners/{owner.Id}/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task owner_downgrade_writes_immutable_audit()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));

        await PostAsSuperAdmin<DowngradeOwnerRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/owners/{owner.Id}/downgrade",
            new DowngradeOwnerRequest(OrgRole.Admin, "step down"), HttpStatusCode.OK);

        var tenantId = ParseOrgNodeGuid(brand.Id);
        var audit = Assert.Single(await LoadAuditEvents(OrgAuditActions.OwnerDowngraded, tenantId));
        Assert.Equal("step down", audit.Reason);
        Assert.Equal("superadmin-idp", audit.ActorExternalId);
    }

    [Fact]
    public async Task owner_revoke_writes_immutable_audit()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var owner = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner@example.com", "Owner", "test", "default", "owner-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, owner.Id, OrgRole.Owner, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new RevokeOwnerRequest("remove owner"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/owners/{owner.Id}/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        var tenantId = ParseOrgNodeGuid(brand.Id);
        var audit = Assert.Single(await LoadAuditEvents(OrgAuditActions.OwnerRevoked, tenantId));
        Assert.Equal("remove owner", audit.Reason);
        Assert.Equal("superadmin-idp", audit.ActorExternalId);
    }

    [Fact]
    public async Task owner_grant_via_dedicated_endpoint_creates_grant()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects", new CreateSubjectRequest("owner2@example.com", "Owner2", "test", "default", "owner2-idp"));

        var grant = await PostAsSuperAdmin<GrantOwnerRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/owners",
            new GrantOwnerRequest(subject.Id), HttpStatusCode.Created);

        Assert.Equal(OrgRole.Owner, grant.Role);
    }

    // ── Invitation Enhancements ──

    [Fact]
    public async Task invitation_uses_absolute_link_when_configured()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));

        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "abs@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        var sent = await WaitForSentInvitationEmailAsync(invitation.Id);

        // By default no PublicBaseUrl is set in tests, so relative link is used
        Assert.Contains("/accept-invite?token=", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task pending_invitation_expires_to_expired()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "expire@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        var invitationId = new InvitationId(ParseInvitationGuid(invitation.Id));

        // Directly append InvitationExpired to avoid timing issues with ExpireInvitation handler
        await using var session = DocumentStore.LightweightSession();
        session.Events.Append(invitationId.Value, new InvitationExpired(invitationId, TimeProvider.System.GetUtcNow()));
        await session.SaveChangesAsync();

        await using var query = DocumentStore.QuerySession();
        var after = await query.LoadAsync<InvitationReadModel>(invitationId.Value);
        Assert.NotNull(after);
        Assert.Equal(InvitationStatus.Expired, after.Status);
        Assert.Null(after.TokenHash);
    }

    [Fact]
    public async Task expired_token_is_rejected_on_accept()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "reject@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        var invitationId = new InvitationId(ParseInvitationGuid(invitation.Id));

        // Expire the invitation directly
        await using var expireSession = DocumentStore.LightweightSession();
        expireSession.Events.Append(invitationId.Value, new InvitationExpired(invitationId, TimeProvider.System.GetUtcNow()));
        await expireSession.SaveChangesAsync();

        var providerUser = FindProviderUser("reject@example.com");
        var sent = await WaitForSentInvitationEmailAsync(invitation.Id);
        var token = ExtractToken(sent.Body);

        await Host.Scenario(_ =>
        {
            AsUser(_, providerUser.ExternalId, "reject@example.com");
            _.Get.Url($"/api/invitations/accept?token={Uri.EscapeDataString(token)}");
            // Expired invitations have token invalidated, so token lookup returns 404
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task invitation_email_retry_eventually_fails_after_three_attempts()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands", new CreateBrandRequest(Code(), "Brand"));
        var invitation = await PostAsSuperAdmin<CreateInvitationRequest, InvitationResponse>(
            $"/api/brands/{brand.Id}/invitations",
            new CreateInvitationRequest(brand.Id, "fail@example.com", OrgRole.Member),
            HttpStatusCode.Accepted);

        var pending = await WaitForInvitationAsync(invitation.Id, InvitationStatus.Pending);
        var invitationId = new InvitationId(ParseInvitationGuid(invitation.Id));

        InvitationEmailStore.FailAll = true;

        var bus = Host.Services.GetRequiredService<IMessageBus>();
        // Attempt 1 fails -> schedules retry
        await bus.InvokeAsync(new SendInvitationEmail(invitationId, "fake-token", false, 1));
        // Attempt 2 fails -> schedules retry
        await bus.InvokeAsync(new RetrySendInvitationEmail(invitationId, "fake-token", false, 2));
        // Attempt 3 fails -> InvitationFailed appended
        await bus.InvokeAsync(new RetrySendInvitationEmail(invitationId, "fake-token", false, 3));

        await using var query = DocumentStore.QuerySession();
        var after = await query.LoadAsync<InvitationReadModel>(invitationId.Value);
        Assert.NotNull(after);
        Assert.Equal(InvitationStatus.Failed, after.Status);
        Assert.NotNull(after.Failure);
        Assert.Null(after.TokenHash);
    }
}
