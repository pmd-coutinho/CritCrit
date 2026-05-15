using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace CritCrit.Api.Org.Invitations;

public sealed record InvitationEmailMessage(
    Guid InvitationId,
    string To,
    string Subject,
    string Body);

public interface IInvitationEmailSender
{
    Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken ct);
}

public sealed class TestInvitationEmailStore
{
    public List<InvitationEmailMessage> Sent { get; } = [];
    public HashSet<Guid> FailInvitationIds { get; } = [];
    public bool FailAll { get; set; }
}

public sealed class InMemoryInvitationEmailSender(TestInvitationEmailStore store) : IInvitationEmailSender
{
    public Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken ct)
    {
        if (store.FailAll || store.FailInvitationIds.Contains(message.InvitationId))
            throw new InvalidOperationException("Simulated invitation email failure.");

        store.Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class SmtpInvitationEmailSender(IConfiguration configuration) : IInvitationEmailSender
{
    public async Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(configuration.GetValue<string>("MAILPIT_HOST"),  configuration.GetValue<int>("MAILPIT_PORT"));
        using var mail = new MailMessage("noreply@critcrit.local", message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, ct);
    }

    private static (string Host, int Port) ParseMailpitConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return ("localhost", 1025);

        var parts = connectionString.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out var port))
            return (string.Join(':', parts[..^1]), port);

        return (parts[0], 1025);
    }
}
