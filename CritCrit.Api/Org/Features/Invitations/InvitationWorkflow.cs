using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Identity;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Invitations;
using CritCrit.Api.Observability.Logging;
using Marten;
using Wolverine;
using Wolverine.Attributes;

namespace CritCrit.Api.Org.Features.Invitations;

[WolverineHandler]
public sealed class InvitationWorkflow(
    IDocumentStore store,
    IIdentityProviderProvisioning identityProvider,
    IInvitationEmailSender emailSender,
    InvitationTokenService tokens,
    IConfiguration configuration,
    IAuditWriter audit,
    ILogger<InvitationWorkflow> logger)
{
    public async Task Handle(ProvisionInvitation command, IMessageBus bus, CancellationToken ct)
    {
        await using var session = SessionFactory.PlatformSession(store);
        var invitation = await session.LoadAsync<InvitationReadModel>(command.InvitationId.Value, ct);
        if (invitation is null || invitation.Status is not InvitationStatus.Requested and not InvitationStatus.Provisioning)
            return;

        session.Events.Append(invitation.Id, new InvitationProvisioningStarted(command.InvitationId, TimeProvider.System.GetUtcNow()));
        await session.SaveChangesAsync(ct);

        invitation = await session.LoadAsync<InvitationReadModel>(command.InvitationId.Value, ct)
            ?? throw new InvalidOperationException("Invitation not found after provisioning start.");

        try
        {
            var providerUser = await identityProvider.EnsureUserAsync(invitation.Email, ct);
            var normalizedEmail = invitation.EmailNormalized;

            var existingLink = await session.LoadAsync<ExternalIdentityReadModel>(
                ExternalIdentityReadModel.BuildId(providerUser.Provider, providerUser.ProviderTenant, providerUser.ExternalId), ct);
            var existingSubject = await session.Query<SubjectReadModel>()
                .Where(x => x.EmailNormalized == normalizedEmail)
                .SingleOrDefaultAsync(ct);

            if (existingLink is not null && existingSubject is not null && existingLink.SubjectId != existingSubject.Id)
                throw new DomainException("Existing identity link conflicts with an existing CritCrit subject.");

            SubjectId subjectId;
            if (existingLink is not null)
            {
                subjectId = new SubjectId(existingLink.SubjectId);
            }
            else if (existingSubject is not null)
            {
                subjectId = new SubjectId(existingSubject.Id);
            }
            else
            {
                subjectId = SubjectId.New();
                session.Events.StartStream<SubjectReadModel>(subjectId.Value,
                    new SubjectCreated(subjectId, SubjectKind.User, providerUser.Email, null));
            }

            var subject = existingSubject ?? await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
            if (subject is { Active: false })
                throw new DomainException("This subject is deactivated. Reactivate them before re-inviting.");

            if (existingLink is null)
            {
                session.Events.Append(subjectId.Value, new ExternalIdentityLinked(subjectId, providerUser.Provider, providerUser.ProviderTenant, providerUser.ExternalId));
            }

            if (subject is not null &&
                !string.Equals(subject.EmailNormalized, providerUser.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                var emailInUse = await session.Query<SubjectReadModel>()
                    .Where(x => x.EmailNormalized == providerUser.Email.Trim().ToLowerInvariant() && x.Id != subject.Id)
                    .AnyAsync(ct);

                if (emailInUse)
                    throw new DomainException("The identity provider email is already bound to another CritCrit subject.");

                session.Events.Append(subjectId.Value, new SubjectEmailUpdated(subjectId, providerUser.Email));
            }

            session.Events.Append(invitation.Id,
                new InvitationSubjectBound(command.InvitationId, subjectId, providerUser.Email));

            var rawToken = tokens.GenerateRawToken();
            var expiresAt = TimeProvider.System.GetUtcNow().AddDays(1);
            session.Events.Append(invitation.Id,
                new InvitationTokenIssued(command.InvitationId, tokens.Hash(rawToken), expiresAt));
            session.Events.Append(invitation.Id,
                new InvitationMarkedPending(command.InvitationId, TimeProvider.System.GetUtcNow()));

            await session.SaveChangesAsync(ct);

            await bus.ScheduleAsync(new ExpireInvitation(command.InvitationId, expiresAt, command.Audit), expiresAt.UtcDateTime);

            if (providerUser.WasCreated)
            {
                // New IdP user: ask Keycloak to email a password-setup link. No
                // redirect — Keycloak's standard "account updated" page is the
                // terminus of that flow. The CritCrit invitation email below
                // carries the accept link and notes that the password email must
                // be acted on first.
                await identityProvider.SendPasswordSetupAsync(
                    new PasswordSetupRequest(providerUser.ExternalId, LifespanSeconds: 24 * 60 * 60),
                    ct);
            }

            await bus.InvokeAsync(new SendInvitationEmail(command.InvitationId, providerUser.WasCreated, 1, command.Audit));
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            RecordFailure(session, invitation, command.InvitationId, "provisioning_failed", "Invitation provisioning failed.", command.Audit?.CausationId);
            logger.InvitationWorkflowFailed(invitation.Id, "provisioning_failed", SupportId.Current);
            await session.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            RecordFailure(session, invitation, command.InvitationId, "domain_rejected", ex.Message, command.Audit?.CausationId);
            logger.InvitationWorkflowFailed(invitation.Id, "domain_rejected", SupportId.Current);
            await session.SaveChangesAsync(ct);
        }
    }

    public async Task Handle(SendInvitationEmail command, IMessageBus bus, CancellationToken ct)
    {
        await using var session = SessionFactory.PlatformSession(store);
        var invitation = await session.LoadAsync<InvitationReadModel>(command.InvitationId.Value, ct);
        if (invitation is null || invitation.Status != InvitationStatus.Pending)
            return;

        try
        {
            var rawToken = tokens.GenerateRawToken();
            var expiresAt = TimeProvider.System.GetUtcNow().AddDays(1);
            session.Events.Append(invitation.Id, new InvitationTokenIssued(command.InvitationId, tokens.Hash(rawToken), expiresAt));
            await session.SaveChangesAsync(ct);
            await bus.ScheduleAsync(new ExpireInvitation(command.InvitationId, expiresAt, command.Audit), expiresAt.UtcDateTime);

            var publicBaseUrl = configuration.GetValue<string>("Invitation:PublicBaseUrl")?.TrimEnd('/') ?? "";
            var acceptUrl = string.IsNullOrEmpty(publicBaseUrl)
                ? $"/accept-invite?token={Uri.EscapeDataString(rawToken)}"
                : $"{publicBaseUrl}/accept-invite?token={Uri.EscapeDataString(rawToken)}";

            var passwordSetupNotice = command.RequiresPasswordSetup
                ? "Heads up: you should also receive a separate email from Keycloak titled \"Update Password\". " +
                  "Click that one first, set your password, then come back to the link below.\n\n"
                : "";

            var body =
                $"You were invited to CritCrit.\n" +
                $"Brand: {invitation.TenantPublicId}\n" +
                $"Target: {invitation.TargetOrgNodePublicId}\n" +
                $"Role: {invitation.Role}\n\n" +
                $"{passwordSetupNotice}" +
                $"Accept: {acceptUrl}";

            await emailSender.SendInvitationAsync(
                new InvitationEmailMessage(invitation.Id, invitation.Email, "CritCrit invitation", body),
                ct);

            session.Events.Append(invitation.Id, new InvitationEmailDispatched(command.InvitationId, TimeProvider.System.GetUtcNow()));
            audit.Record(session, OrgAudit.SystemRecord(
                OrgAuditActions.InvitationEmailDispatched,
                AuditCategories.Invitation,
                AuditSeverities.Info,
                AuditActor.BackgroundSystem(),
                invitation.TenantId,
                invitation.TargetOrgNodeId,
                details: OrgAudit.InviteDetails(invitation, new { command.Attempt }),
                subjectId: invitation.SubjectId,
                causationId: command.Audit?.CausationId));
            await session.SaveChangesAsync(ct);
            logger.InvitationEmailDispatched(invitation.Id, command.Attempt, SupportId.Current);
        }
        catch (Exception ex) when (command.Attempt < 3)
        {
            logger.InvitationEmailSendFailed(ex, command.InvitationId.Value, command.Attempt, false, SupportId.Current);
            var delay = command.Attempt switch
            {
                1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                _ => TimeSpan.FromMinutes(15)
            };
            await bus.ScheduleAsync(
                new RetrySendInvitationEmail(command.InvitationId, command.RequiresPasswordSetup, command.Attempt + 1, command.Audit),
                DateTime.UtcNow + delay);
        }
        catch (Exception ex)
        {
            RecordFailure(session, invitation, command.InvitationId, "email_delivery_failed", "Invitation email delivery failed.", command.Audit?.CausationId);
            logger.InvitationEmailSendFailed(ex, invitation.Id, command.Attempt, true, SupportId.Current);
            await session.SaveChangesAsync(ct);
        }
    }

    public async Task Handle(ExpireInvitation command, CancellationToken ct)
    {
        await using var session = SessionFactory.PlatformSession(store);
        var invitation = await session.LoadAsync<InvitationReadModel>(command.InvitationId.Value, ct);
        if (invitation is null || invitation.Status != InvitationStatus.Pending)
            return;
        if (invitation.ExpiresAt is null || invitation.ExpiresAt.Value != command.ExpiresAt)
            return;
        if (invitation.ExpiresAt > TimeProvider.System.GetUtcNow())
            return;

        session.Events.Append(invitation.Id, new InvitationExpired(command.InvitationId, TimeProvider.System.GetUtcNow()));
        audit.Record(session, OrgAudit.SystemRecord(
            OrgAuditActions.InvitationExpired,
            AuditCategories.Invitation,
            AuditSeverities.Warn,
            AuditActor.BackgroundSystem(),
            invitation.TenantId,
            invitation.TargetOrgNodeId,
            details: OrgAudit.InviteDetails(invitation),
            subjectId: invitation.SubjectId,
            causationId: command.Audit?.CausationId));
        await session.SaveChangesAsync(ct);
    }

    public async Task Handle(RetrySendInvitationEmail command, IMessageBus bus, CancellationToken ct)
    {
        await bus.InvokeAsync(new SendInvitationEmail(command.InvitationId, command.RequiresPasswordSetup, command.Attempt, command.Audit), ct);
    }

    private void RecordFailure(IDocumentSession session, InvitationReadModel invitation, InvitationId invitationId, string code, string summary, string? causationId)
    {
        session.Events.Append(invitation.Id, new InvitationFailed(invitationId, TimeProvider.System.GetUtcNow(), code, summary));
        audit.Record(session, OrgAudit.SystemRecord(
            OrgAuditActions.InvitationFailed,
            AuditCategories.Invitation,
            AuditSeverities.Warn,
            AuditActor.BackgroundSystem(),
            invitation.TenantId,
            invitation.TargetOrgNodeId,
            details: OrgAudit.InviteDetails(invitation, new { FailureCode = code, FailureSummary = summary }),
            subjectId: invitation.SubjectId,
            causationId: causationId));
    }
}
