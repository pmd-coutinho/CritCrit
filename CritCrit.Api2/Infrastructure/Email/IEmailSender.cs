using System.Net.Mail;

namespace CritCrit.Api2.Infrastructure.Email;

public interface IEmailSender
{
    Task SendMessageAsync<T>(T message, CancellationToken ct) where T : EmailMessage;
}

public sealed class TestEmailStore
{
    public List<EmailMessage> Sent { get; } = [];
    public HashSet<Guid> FailIds { get; } = [];
    public bool FailAll { get; set; }
}

public sealed class InMemoryEmailSender(TestEmailStore store) : IEmailSender
{
    public Task SendMessageAsync<T>(T message, CancellationToken ct) where T : EmailMessage
    {
        if (store.FailAll || store.FailIds.Contains(message.Id))
            throw new InvalidOperationException("Simulated invitation email failure.");

        store.Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendMessageAsync<T>(T message, CancellationToken ct) where T : EmailMessage
    {
        using var client = new SmtpClient(configuration.GetValue<string>("MAILPIT_HOST"),  configuration.GetValue<int>("MAILPIT_PORT"));
        using var mail = new MailMessage("noreply@critcrit.local", message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, ct);
    }
}

