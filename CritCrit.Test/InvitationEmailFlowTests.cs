using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Handlers;
using CritCrit.Api.Org.Identity;
using CritCrit.Test.Fixtures;
using Wolverine.Tracking;

namespace CritCrit.Test;

public sealed class InvitationEmailFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task new_user_triggers_password_setup_and_email_has_notice()
    {
        var brand = await CreateBrand(Code("inv-new"));

        var (tracked, _) = await TrackedHttpCall(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(
                    new CreateInvitationRequest(brand.Id, "fresh@example.com", OrgRole.Member),
                    Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/invitations");
            _.StatusCodeShouldBe(HttpStatusCode.Accepted);
        });

        tracked.Executed.SingleMessage<ProvisionInvitation>();
        var sent = tracked.Executed.SingleMessage<SendInvitationEmail>();
        Assert.True(sent.RequiresPasswordSetup);

        Assert.Single(Provisioning.PasswordSetupCalls);
        var emailBody = InvitationEmailStore.Sent
            .Single(x => x.To == "fresh@example.com").Body;
        Assert.Contains("Update Password", emailBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task existing_user_skips_password_setup_and_omits_notice()
    {
        var brand = await CreateBrand(Code("inv-exist"));

        // Seed IdP store so EnsureUserAsync returns WasCreated=false
        IdentityProviderStore.Users.TryAdd(
            "preexisting@example.com",
            new FakeIdentityProviderUser(Guid.CreateVersion7().ToString(), "preexisting@example.com", true));

        var (tracked, _) = await TrackedHttpCall(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(
                    new CreateInvitationRequest(brand.Id, "preexisting@example.com", OrgRole.Member),
                    Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/invitations");
            _.StatusCodeShouldBe(HttpStatusCode.Accepted);
        });

        var sent = tracked.Executed.SingleMessage<SendInvitationEmail>();
        Assert.False(sent.RequiresPasswordSetup);

        Assert.Empty(Provisioning.PasswordSetupCalls);
        var emailBody = InvitationEmailStore.Sent
            .Single(x => x.To == "preexisting@example.com").Body;
        Assert.DoesNotContain("Update Password", emailBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task resend_does_not_re_trigger_password_setup()
    {
        var brand = await CreateBrand(Code("inv-resend"));

        // Initial create (new user → one password setup call)
        var (_, createResult) = await TrackedHttpCall(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(
                    new CreateInvitationRequest(brand.Id, "resend@example.com", OrgRole.Member),
                    Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/invitations");
            _.StatusCodeShouldBe(HttpStatusCode.Accepted);
        });
        var invitation = await createResult.ReadAsJsonAsync<InvitationResponse>();
        Assert.NotNull(invitation);
        Assert.Single(Provisioning.PasswordSetupCalls);

        var (tracked, _) = await TrackedHttpCall(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new { }, Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/invitations/{invitation!.Id}/resend");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var sent = tracked.Executed.SingleMessage<SendInvitationEmail>();
        Assert.False(sent.RequiresPasswordSetup);
        Assert.Single(Provisioning.PasswordSetupCalls); // unchanged
    }
}
